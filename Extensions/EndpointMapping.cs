using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PTK.OpaqueIframeProxy.Options;

namespace PTK.OpaqueIframeProxy.Extensions;

/// <summary>
/// Daftarkan endpoint HTML &amp; gambar sesuai template route di konfigurasi.
/// </summary>
public static class EndpointMapping
{
  /// <summary>
  /// Mendaftarkan endpoint fleksibel berdasarkan <c>OpaqueProxyOptions.Routes</c>.
  /// </summary>
  public static IEndpointRouteBuilder MapOpaqueContentProxy(this IEndpointRouteBuilder endpoints)
  {
    var opt = endpoints.ServiceProvider.GetRequiredService<IOptions<OpaqueProxyOptions>>().Value;

    // substitusi {basePath} + normalisasi leading slash
    static string Resolve(string? tpl, string basePath)
    {
      var s = (tpl ?? "/").Replace("{basePath}", basePath, StringComparison.OrdinalIgnoreCase);
      return s.StartsWith('/') ? s : "/" + s;
    }

    var basePath = (opt.BasePath ?? "proxy").Trim('/');
    var htmlRoute = Resolve(opt.Routes.HtmlTokenTemplate, basePath); // harus punya {token}
    var imgRoute = Resolve(opt.Routes.ImageSlugTemplate, basePath); // harus punya {slug}

    if (opt.Routes.AutoMapEndpoints)
    {
      endpoints.MapGet(htmlRoute,
        async (string token, IOpaqueContentProxyService svc, HttpContext ctx, CancellationToken ct) =>
        {
          var r = await svc.GetHtmlByTokenAsync(token, ct);
          if (r.NoCache)
            ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";

          // hardening headers:
          ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
          // ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN"; // aktifkan jika kebijakan mengizinkan

          return Results.Content(r.Html, r.ContentType);
        });

      endpoints.MapGet(imgRoute,
        async (string slug, IOpaqueContentProxyService svc, HttpContext ctx, CancellationToken ct) =>
        {
          var r = await svc.GetImageBySlugAsync(slug, false, ct);
          if (r.ETag is not null) ctx.Response.Headers.ETag = r.ETag;
          if (r.MaxAge is not null)
            ctx.Response.Headers.CacheControl = $"public, max-age={(int)r.MaxAge.Value.TotalSeconds}, immutable";
          if (r.Length is not null) ctx.Response.ContentLength = r.Length.Value;

          // hardening header:
          ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";

          return Results.Stream(r.Content, r.ContentType);
        });
    }

    return endpoints;
  }
}
