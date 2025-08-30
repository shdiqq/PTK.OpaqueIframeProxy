using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PTK.OpaqueIframeProxy.Interfaces;
using PTK.OpaqueIframeProxy.Internal;
using PTK.OpaqueIframeProxy.Options;

using System;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Reflection;

namespace PTK.OpaqueIframeProxy.Extensions
{
  /// <summary>Extension methods untuk registrasi layanan OpaqueIframeProxy.</summary>
  public static class DependencyInjection
  {
    /// <summary>
    /// Registrasi Options + DataProtection + HttpClient bernama untuk OpaqueIframeProxy.
    /// Belum menambahkan endpoint; pemetaan endpoint akan dilakukan pada langkah berikutnya.
    /// </summary>
    public static IServiceCollection AddOpaqueContentProxy(this IServiceCollection services, IConfiguration config)
    {
      // Bind + validasi OpaqueProxyOptions (termasuk Routes)
      var optBuilder = services.AddOptions<OpaqueProxyOptions>().Bind(config.GetSection("OpaqueProxy")).ValidateDataAnnotations();

#if NET6_0_OR_GREATER
      optBuilder
        .Validate(o => o.Routes is not null, "OpaqueProxy:Routes required")
        .Validate(o => o.Routes!.HtmlTokenTemplate.Contains("{token}"), "OpaqueProxy:Routes:HtmlTokenTemplate must contain {token}")
        .Validate(o => o.Routes!.ImageSlugTemplate.Contains("{slug}"), "OpaqueProxy:Routes:ImageSlugTemplate must contain {slug}");
#endif

      // Bind + validasi OpaqueProxyMapOptions
      services.AddOptions<OpaqueProxyMapOptions>().Bind(config.GetSection("OpaqueProxyMap")).ValidateDataAnnotations();

      services.PostConfigure<OpaqueProxyMapOptions>(o =>
      {
        var key = o.GetKeyBytes(); // decode base64url atau UTF-8
        if (key.Length < 16)
          throw new ValidationException("OpaqueProxyMap:SlugSecret must be at least 16 bytes.");
      });

      // Data protection (konsumen dapat override/persist sendiri di app)
      services.AddDataProtection();

      // HttpClient bernama untuk fetch origin (dipakai nanti oleh service internal)
      services.AddHttpClient("opaque-origin", c =>
      {
        c.Timeout = TimeSpan.FromSeconds(15);

        // ambil versi assembly saat ini
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";

        c.DefaultRequestHeaders.UserAgent.Add(
        new ProductInfoHeaderValue("PTK.OpaqueIframeProxy", version));

        c.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("*/*"));
      });

      // Tambah registrasi service & builder:
      services.AddScoped<IOpaqueContentProxyService, OpaqueContentProxyServiceImpl>();
      services.AddScoped<IOpaqueIframeUrlBuilder, OpaqueIframeUrlBuilder>();

      return services;
    }

  }
}