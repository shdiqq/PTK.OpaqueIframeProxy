using HtmlAgilityPack;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PTK.OpaqueIframeProxy.Helpers;
using PTK.OpaqueIframeProxy.Interfaces;
using PTK.OpaqueIframeProxy.Options;

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PTK.OpaqueIframeProxy.Internal
{
  internal sealed class OpaqueContentProxyServiceImpl : IOpaqueContentProxyService
  {
    private const int MaxHtmlBytes = 2 * 1024 * 1024;   // 2 MB cap untuk HTML
    private const int MaxImgRewrite = 300;              // batas rewrite <img> agar hemat resource

    private readonly IHttpClientFactory _http;
    private readonly OpaqueProxyOptions _opt;
    private readonly OpaqueProxyMapOptions _map;
    private readonly IDataProtector _protector;
    private readonly ILogger<OpaqueContentProxyServiceImpl>? _log;
    private readonly IHttpContextAccessor _httpCtx;

    public OpaqueContentProxyServiceImpl(
        IHttpClientFactory http,
        IOptions<OpaqueProxyOptions> opt,
        IOptions<OpaqueProxyMapOptions> map,
        IDataProtectionProvider dp,
        ILogger<OpaqueContentProxyServiceImpl>? log = null,
        IHttpContextAccessor? httpCtx = null)
    {
      _http = http;
      _opt = opt.Value;
      _map = map.Value;
      _protector = dp.CreateProtector("PTK.OpaqueIframeProxy:html-token");
      _log = log;
      _httpCtx = httpCtx ?? new HttpContextAccessor();

      Directory.CreateDirectory(_map.MapRoot);
    }

    public async Task<HtmlResult> GetHtmlByTokenAsync(string token, CancellationToken ct = default)
    {
      string json;
      try
      {
        try
        {
          var protectedString = Base64Url.Decode(token);
          json = _protector.Unprotect(protectedString);
        }
        catch
        {
          json = _protector.Unprotect(Uri.UnescapeDataString(token));
        }
      }
      catch (Exception ex)
      {
        _log?.LogWarning(ex, "Invalid/Corrupted token");
        return new HtmlResult("<html><body>Invalid token</body></html>");
      }

      using var docPayload = JsonDocument.Parse(json);
      var root = docPayload.RootElement;

      var ver = root.TryGetProperty("ver", out var v) ? v.GetInt32() : 0;
      if (ver != 0 && ver != 1)
      {
        _log?.LogWarning("Unsupported token version: {Ver}", ver);
        return new HtmlResult("<html><body>Invalid token version</body></html>");
      }

      var origin = root.GetProperty("origin").GetString();
      if (string.IsNullOrWhiteSpace(origin))
        return new HtmlResult("<html><body>Invalid token</body></html>");

      var expUnix = root.GetProperty("exp").GetInt64();
      if (DateTimeOffset.FromUnixTimeSeconds(expUnix) < DateTimeOffset.UtcNow)
      {
        _log?.LogWarning("Token expired for {Origin}", origin);
        return new HtmlResult("<html><body>Token expired</body></html>");
      }

      var originUri = new Uri(origin);
      if (!_opt.AllowedHtmlHosts.Contains(originUri.Host, StringComparer.OrdinalIgnoreCase))
      {
        _log?.LogWarning("Host not allowed for HTML: {Host}", originUri.Host);
        return new HtmlResult("<html><body>Host not allowed</body></html>");
      }

      var cli = _http.CreateClient("opaque-origin");
      using var resp = await cli.GetAsync(origin, HttpCompletionOption.ResponseContentRead, ct);
      resp.EnsureSuccessStatusCode();

      var contentType = resp.Content.Headers.ContentType?.ToString();
      if (string.IsNullOrWhiteSpace(contentType))
        contentType = "text/html; charset=utf-8";

#if NET5_0_OR_GREATER
      var html = await resp.Content.ReadAsStringAsync(ct);
#else
      var html = await resp.Content.ReadAsStringAsync();
#endif

      if (Encoding.UTF8.GetByteCount(html) > MaxHtmlBytes)
      {
        _log?.LogWarning("HTML too large (> {Max} bytes) from {Origin}", MaxHtmlBytes, origin);
        return new HtmlResult("<html><body>Content too large</body></html>", contentType);
      }

      var hap = new HtmlDocument();
      hap.LoadHtml(html);

      var imgs = hap.DocumentNode.SelectNodes("//img[@src]");
      if (imgs != null)
      {
        int count = 0;
        foreach (var img in imgs)
        {
          if (count++ >= _opt.MaxImgRewrite) break;

          var src = img.GetAttributeValue("src", string.Empty);
          if (string.IsNullOrWhiteSpace(src)) continue;

          Uri abs;
          try { abs = new Uri(originUri, src); }
          catch { continue; }

          if (!_opt.AllowedImageHosts.Contains(abs.Host, StringComparer.OrdinalIgnoreCase)) continue;

          var slug = SlugHelper.ComputeSlug(abs.ToString(), _map.GetKeyBytes());
          await PersistMappingAsync(slug, abs.ToString(), ct);

          var basePath = (_opt.BasePath ?? "proxy").Trim('/');
          var tpl = _opt.Routes.ImageSlugTemplate ?? "/{basePath}/img/s/{slug}";
          var imgUrl = tpl.Replace("{basePath}", basePath, StringComparison.OrdinalIgnoreCase)
                          .Replace("{slug}", slug, StringComparison.OrdinalIgnoreCase);
          if (!imgUrl.StartsWith('/')) imgUrl = "/" + imgUrl;

          var pathBase = _httpCtx.HttpContext?.Request.PathBase.Value?.TrimEnd('/');
          if (!string.IsNullOrEmpty(pathBase) &&
              !imgUrl.StartsWith(pathBase!, StringComparison.OrdinalIgnoreCase))
          {
            imgUrl = pathBase + imgUrl;
          }

          img.SetAttributeValue("src", imgUrl);
        }

        _log?.LogInformation("Rewrote {Count} <img> tag(s) for {Origin}", Math.Min(count, MaxImgRewrite), origin);
      }

      using var ms = new MemoryStream();
      hap.Save(ms);
      var rewritten = Encoding.UTF8.GetString(ms.ToArray());

      return new HtmlResult(rewritten, contentType, NoCache: true);
    }

    public async Task<ImageResult> GetImageBySlugAsync(string slug, bool debug = false, CancellationToken ct = default)
    {
      // Dipertahankan untuk kompatibilitas (endpoint baru pakai PrepareImageCopyAsync)
      var mapFile = GetMapPath(slug);
      if (!File.Exists(mapFile))
        return TinyPngFallback();

      var url = await File.ReadAllTextAsync(mapFile, ct);
      var abs = new Uri(url);

      if (!_opt.AllowedImageHosts.Contains(abs.Host, StringComparer.OrdinalIgnoreCase))
        return TinyPngFallback();

      var cli = _http.CreateClient("opaque-origin");
      using var resp = await cli.GetAsync(abs, HttpCompletionOption.ResponseHeadersRead, ct);
      resp.EnsureSuccessStatusCode();

      var max = (long)_map.MaxFileSizeBytes;
      var contentLength = resp.Content.Headers.ContentLength;
      if (contentLength.HasValue && contentLength.Value > max)
      {
        _log?.LogWarning("Image too large: {Len} > {Max} for {Url}", contentLength.Value, max, abs);
        return TinyPngFallback();
      }

      var contentTypeImg = resp.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
      var etag = resp.Headers.ETag?.ToString();

#if NET5_0_OR_GREATER
      var networkStream = await resp.Content.ReadAsStreamAsync(ct);
#else
      var networkStream = await resp.Content.ReadAsStreamAsync();
#endif

      if (!contentLength.HasValue)
      {
        var buffered = new MemoryStream(capacity: (int)Math.Min(max, 128 * 1024));
        var buffer = new byte[8192];
        int read;
        long total = 0;
        while ((read = await networkStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
          total += read;
          if (total > max)
          {
            _log?.LogWarning("Image exceeded max size while streaming ({Total} > {Max}) {Url}", total, max, abs);
            return TinyPngFallback();
          }
          await buffered.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        buffered.Position = 0;
        return new ImageResult(buffered, contentTypeImg, total, etag, TimeSpan.FromDays(30));
      }

      return new ImageResult(networkStream, contentTypeImg, contentLength, etag, TimeSpan.FromDays(30));
    }

    /// <summary>
    /// Siapkan penyalinan streaming aman untuk gambar berdasarkan slug yang sudah dimapping.
    /// </summary>
    public async Task<ImageCopyHandle?> PrepareImageCopyAsync(string slug, bool preferOriginal = false, CancellationToken ct = default)
    {
      var mapFile = GetMapPath(slug);
      if (!File.Exists(mapFile))
        return null;

      var url = await File.ReadAllTextAsync(mapFile, ct);
      var abs = new Uri(url);

      if (!_opt.AllowedImageHosts.Contains(abs.Host, StringComparer.OrdinalIgnoreCase))
        return null;

      var cli = _http.CreateClient("opaque-origin");
      var req = new HttpRequestMessage(HttpMethod.Get, abs);

      var resp = await cli.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
      if (!resp.IsSuccessStatusCode)
      {
        resp.Dispose();
        return null;
      }

      var content = resp.Content;
      var contentType = content.Headers.ContentType?.ToString() ?? "application/octet-stream";
      var length = content.Headers.ContentLength;
      var etag = resp.Headers.ETag?.ToString();
      var max = (long)_map.MaxFileSizeBytes;

      if (length.HasValue && length.Value > max)
      {
        _log?.LogWarning("Image too large: {Len} > {Max} for {Url}", length.Value, max, abs);
        resp.Dispose();
        return null;
      }

      async Task Copy(Stream destination, CancellationToken token)
      {
        Stream? source = null;
        try
        {

#if NET5_0_OR_GREATER
          source = await content.ReadAsStreamAsync(token);
#else
          source = await content.ReadAsStreamAsync();
#endif

          if (!length.HasValue)
          {
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
              var n = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
              if (n <= 0) break;
              total += n;
              if (total > max)
              {
                _log?.LogWarning("Image exceeded max size while streaming ({Total} > {Max}) {Url}", total, max, abs);
                return; // early stop
              }
              await destination.WriteAsync(buffer.AsMemory(0, n), token);
            }
          }
          else
          {
            await source.CopyToAsync(destination, 81920, token);
          }
        }
        finally
        {
          source?.Dispose();
          resp.Dispose();
        }
      }

      return new ImageCopyHandle(
        Copy,
        contentType,
        length,
        etag,
        TimeSpan.FromDays(30),
        resp // IDisposable
      );
    }

    // --- helpers ---
    private static ImageResult TinyPngFallback()
    {
      var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNgYAAAAAMAASsJTYQAAAAASUVORK5CYII=");
      return new ImageResult(new MemoryStream(png), "image/png", png.Length, null, TimeSpan.FromDays(30));
    }

    private async Task PersistMappingAsync(string slug, string url, CancellationToken ct)
    {
      var path = GetMapPath(slug);
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);
      await File.WriteAllTextAsync(path, url, ct);
    }

    private string GetMapPath(string slug) => Path.Combine(_map.MapRoot, $"{slug}.bin");
  }
}
