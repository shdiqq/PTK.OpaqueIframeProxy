using System.Text;
using System.Text.Json;
using HtmlAgilityPack;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PTK.OpaqueIframeProxy.Helpers;
using PTK.OpaqueIframeProxy.Options;

namespace PTK.OpaqueIframeProxy.Internal;

internal sealed class OpaqueContentProxyServiceImpl : IOpaqueContentProxyService
{
  private const int MaxHtmlBytes = 2 * 1024 * 1024;   // 2 MB cap untuk HTML
  private const int MaxImgRewrite = 300;              // batas rewrite <img> agar hemat resource

  private readonly IHttpClientFactory _http;
  private readonly OpaqueProxyOptions _opt;
  private readonly OpaqueProxyMapOptions _map;
  private readonly IDataProtector _protector;
  private readonly ILogger<OpaqueContentProxyServiceImpl>? _log;

  public OpaqueContentProxyServiceImpl(
      IHttpClientFactory http,
      IOptions<OpaqueProxyOptions> opt,
      IOptions<OpaqueProxyMapOptions> map,
      IDataProtectionProvider dp,
      ILogger<OpaqueContentProxyServiceImpl>? log = null)
  {
    _http = http;
    _opt = opt.Value;
    _map = map.Value;
    _protector = dp.CreateProtector("PTK.OpaqueIframeProxy:html-token");
    _log = log;

    Directory.CreateDirectory(_map.MapRoot);
  }

  public async Task<HtmlResult> GetHtmlByTokenAsync(string token, CancellationToken ct = default)
  {
    // 1) Unprotect & parse token (+ hardening)
    string json;
    try
    {
      json = _protector.Unprotect(Uri.UnescapeDataString(token));
    }
    catch (Exception ex)
    {
      _log?.LogWarning(ex, "Invalid/Corrupted token");
      return new HtmlResult("<html><body>Invalid token</body></html>");
    }

    using var docPayload = JsonDocument.Parse(json);
    var root = docPayload.RootElement;

    // versi skema token (untuk kompatibilitas ke depan)
    var ver = root.TryGetProperty("ver", out var v) ? v.GetInt32() : 0;
    if (ver != 0 && ver != 1) // izinkan 0 (legacy) & 1 (baru)
    {
      _log?.LogWarning("Unsupported token version: {Ver}", ver);
      return new HtmlResult("<html><body>Invalid token version</body></html>");
    }

    var origin = root.GetProperty("origin").GetString();
    if (string.IsNullOrWhiteSpace(origin))
      return new HtmlResult("<html><body>Invalid token</body></html>");

    var expUnix = root.GetProperty("exp").GetInt64();

    // 2) Expiry check
    if (DateTimeOffset.FromUnixTimeSeconds(expUnix) < DateTimeOffset.UtcNow)
    {
      _log?.LogWarning("Token expired for {Origin}", origin);
      return new HtmlResult("<html><body>Token expired</body></html>");
    }

    // 3) Host whitelist (HTML)
    var originUri = new Uri(origin);
    if (!_opt.AllowedHtmlHosts.Contains(originUri.Host, StringComparer.OrdinalIgnoreCase))
    {
      _log?.LogWarning("Host not allowed for HTML: {Host}", originUri.Host);
      return new HtmlResult("<html><body>Host not allowed</body></html>");
    }

    // 4) Fetch HTML
    var cli = _http.CreateClient("opaque-origin");
    using var resp = await cli.GetAsync(origin, HttpCompletionOption.ResponseContentRead, ct);
    resp.EnsureSuccessStatusCode();

    var contentType = resp.Content.Headers.ContentType?.ToString();
    if (string.IsNullOrWhiteSpace(contentType))
      contentType = "text/html; charset=utf-8";

    var html = await resp.Content.ReadAsStringAsync(ct);

    // Hard cap ukuran HTML (anti-DoS)
    if (Encoding.UTF8.GetByteCount(html) > MaxHtmlBytes)
    {
      _log?.LogWarning("HTML too large (> {Max} bytes) from {Origin}", MaxHtmlBytes, origin);
      return new HtmlResult("<html><body>Content too large</body></html>", contentType);
    }

    // 5) Rewrite <img src="..."> (dengan limit)
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
    var mapFile = GetMapPath(slug);
    if (!File.Exists(mapFile))
      return TinyPngFallback();

    var url = await File.ReadAllTextAsync(mapFile, ct);

    // validasi host lagi (defense-in-depth)
    var abs = new Uri(url);
    if (!_opt.AllowedImageHosts.Contains(abs.Host, StringComparer.OrdinalIgnoreCase))
      return TinyPngFallback();

    var cli = _http.CreateClient("opaque-origin");
    using var resp = await cli.GetAsync(abs, HttpCompletionOption.ResponseHeadersRead, ct);
    resp.EnsureSuccessStatusCode();

    var max = (long)_map.MaxFileSizeBytes;

    // Jika Content-Length ada dan melebihi batas → fallback, no-throw
    var contentLength = resp.Content.Headers.ContentLength;
    if (contentLength.HasValue && contentLength.Value > max)
    {
      _log?.LogWarning("Image too large: {Len} > {Max} for {Url}", contentLength.Value, max, abs);
      return TinyPngFallback();
    }

    // Stream dengan guard ukuran saat Content-Length tidak ada
    var contentTypeImg = resp.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
    var etag = resp.Headers.ETag?.ToString();

    var networkStream = await resp.Content.ReadAsStreamAsync(ct);
    if (!contentLength.HasValue)
    {
      // enforce limit while buffering
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

    // Content-Length ada dan <= max → stream langsung
    return new ImageResult(networkStream, contentTypeImg, contentLength, etag, TimeSpan.FromDays(30));
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
