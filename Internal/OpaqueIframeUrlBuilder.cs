using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using PTK.OpaqueIframeProxy.Interfaces;
using PTK.OpaqueIframeProxy.Options;

namespace PTK.OpaqueIframeProxy.Internal
{
  internal static class Base64Url
  {
    public static string Encode(string s)
      => Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Decode(string u)
    {
      var s = u.Replace('-', '+').Replace('_', '/');
      switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
      return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }
  }

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

      if (!_opt.AllowedHtmlHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Host '{uri.Host}' tidak diizinkan.");

      var now = DateTimeOffset.UtcNow;
      var payload = new
      {
        ver = 1,
        origin = originIframeSrc,
        iat = now.ToUnixTimeSeconds(),
        exp = now.Add(ttl ?? _opt.HtmlTokenLifetime).ToUnixTimeSeconds(),
        extra = extraClaims
      };

      var protectedString = _protector.Protect(JsonSerializer.Serialize(payload));
      var tokenB64Url = Base64Url.Encode(protectedString);

      var basePath = (_opt.BasePath ?? "proxy").Trim('/');
      var tpl = _opt.Routes.HtmlTokenTemplate ?? "/{basePath}/t?token={token}";
      var url = tpl.Replace("{basePath}", basePath, StringComparison.OrdinalIgnoreCase)
                   .Replace("{token}", tokenB64Url, StringComparison.OrdinalIgnoreCase);

      if (!url.StartsWith('/')) url = "/" + url;
      return url;
    }
  }
}
