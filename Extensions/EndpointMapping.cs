using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PTK.OpaqueIframeProxy.Interfaces;
using PTK.OpaqueIframeProxy.Options;

namespace PTK.OpaqueIframeProxy.Extensions
{
  /// <summary>
  /// Daftarkan endpoint HTML and gambar sesuai template route di konfigurasi.
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
      var imgRoute = Resolve(opt.Routes.ImageSlugTemplate, basePath);  // harus punya {slug}

      if (opt.Routes.AutoMapEndpoints)
      {
        // HTML endpoint (tetap)
        endpoints.MapGet(htmlRoute,
          async (string token, IOpaqueContentProxyService svc, HttpContext ctx, CancellationToken ct) =>
          {
            var r = await svc.GetHtmlByTokenAsync(token, ct);
            if (r.NoCache)
              ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";

            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.ContentType = r.ContentType;
            await ctx.Response.WriteAsync(r.Html, ct);

            return Results.StatusCode(StatusCodes.Status200OK);
          });

        // Gambar endpoint — tulis langsung ke Response.Body (delegate style manual)
        endpoints.MapGet(imgRoute,
          async (string slug, IOpaqueContentProxyService svc, HttpContext ctx, CancellationToken ct) =>
          {
            var copy = await svc.PrepareImageCopyAsync(slug, preferOriginal: false, ct);
            if (copy is null)
              return Results.NotFound();

            // Set header sebelum body
            if (copy.ETag is not null) ctx.Response.Headers.ETag = copy.ETag;
            if (copy.MaxAge is not null)
              ctx.Response.Headers.CacheControl = $"public, max-age={(int)copy.MaxAge.Value.TotalSeconds}, immutable";
            if (copy.Length is not null) ctx.Response.ContentLength = copy.Length.Value;

            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.ContentType = copy.ContentType;

            try
            {
              await copy.CopyToAsync(ctx.Response.Body, ct);
              await ctx.Response.Body.FlushAsync(ct);
            }
            finally
            {
              await copy.DisposeAsync(); // aman: hanya IDisposable di dalam
            }

            return Results.StatusCode(StatusCodes.Status200OK);
          });
      }

      return endpoints;
    }
  }
}