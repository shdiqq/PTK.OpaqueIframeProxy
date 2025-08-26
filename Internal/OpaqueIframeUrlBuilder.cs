using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using PTK.OpaqueIframeProxy.Interfaces;
using PTK.OpaqueIframeProxy.Options;

namespace PTK.OpaqueIframeProxy.Internal
{
  internal sealed class OpaqueIframeUrlBuilder : IOpaqueIframeUrlBuilder
  {
    private readonly OpaqueProxyOptions _opt;
    private readonly IDataProtector _protector;

    public OpaqueIframeUrlBuilder(IOptions<OpaqueProxyOptions> opt, IDataProtectionProvider dp)
    {
      _opt = opt.Value;
      _protector = dp.CreateProtector("PTK.OpaqueIframeProxy:html-token");
    }

    public string Build(string originIframeSrc, TimeSpan? ttl = null, IDictionary<string, string>? extraClaims = null)
    {
      if (!Uri.TryCreate(originIframeSrc, UriKind.Absolute, out var uri))
        throw new ArgumentException("originIframeSrc harus URL absolut.", nameof(originIframeSrc));

      // whitelist host HTML (sudah ada)
      if (!_opt.AllowedHtmlHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Host '{uri.Host}' tidak diizinkan.");

      var now = DateTimeOffset.UtcNow;
      var payload = new
      {
        ver = 1, // <-- tambahkan versi skema
        origin = originIframeSrc,
        iat = now.ToUnixTimeSeconds(),
        exp = now.Add(ttl ?? _opt.HtmlTokenLifetime).ToUnixTimeSeconds(),
        extra = extraClaims
      };

      var token = _protector.Protect(JsonSerializer.Serialize(payload));

      // rakit URL sesuai template (sudah ada)
      var basePath = (_opt.BasePath ?? "proxy").Trim('/');
      var tpl = _opt.Routes.HtmlTokenTemplate ?? "/{basePath}/t/{token}";
      var path = tpl.Replace("{basePath}", basePath, StringComparison.OrdinalIgnoreCase)
                    .Replace("{token}", Uri.EscapeDataString(token), StringComparison.OrdinalIgnoreCase);
      if (!path.StartsWith('/')) path = "/" + path;
      return path;
    }
  }
}