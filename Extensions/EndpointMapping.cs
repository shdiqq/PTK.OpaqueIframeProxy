using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PTK.OpaqueIframeProxy.Interfaces;
using PTK.OpaqueIframeProxy.Options;

namespace PTK.OpaqueIframeProxy.Extensions
{
  /// <summary>
  /// Ekstensi untuk mendaftarkan endpoint HTML &amp; gambar
  /// sesuai konfigurasi <see cref="OpaqueProxyOptions"/>.
  /// </summary>
  public static class EndpointMapping
  {
    /// <summary>
    /// Mendaftarkan endpoint Opaque Content Proxy sesuai template route
    /// yang diatur di <see cref="OpaqueProxyOptions.Routes"/>.
    /// </summary>
    /// <param name="endpoints">Builder endpoint yang sedang digunakan.</param>
    /// <returns>
    /// Objek <see cref="IEndpointRouteBuilder"/> yang sama,
    /// untuk memudahkan chaining konfigurasi.
    /// </returns>
    /// <remarks>
    /// Pastikan method ini dipanggil setelah <c>UseRouting()</c>.
    /// </remarks>
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
        // =========================
        // HTML endpoint — manual write (tanpa IResult)
        // =========================
        endpoints.MapGet(htmlRoute,
          async (string token, IOpaqueContentProxyService svc, HttpContext ctx, CancellationToken ct) =>
          {
            var r = await svc.GetHtmlByTokenAsync(token, ct);

            if (r.NoCache)
              ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";

            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.ContentType = r.ContentType;

            await ctx.Response.WriteAsync(r.Html, ct);
          });

        // =========================
        // Gambar endpoint — manual streaming total (tanpa IResult)
        // =========================
        endpoints.MapGet(imgRoute,
        async (string slug, IOpaqueContentProxyService svc, HttpContext ctx, CancellationToken ct) =>
        {
          var log = ctx.RequestServices
          .GetRequiredService<ILoggerFactory>()
          .CreateLogger("OpaqueProxy");

          log.LogInformation("IMG handler start: slug={Slug}", slug);

          var copy = await svc.PrepareImageCopyAsync(slug, preferOriginal: false, ct);
          if (copy is null)
          {
            log.LogWarning("IMG not found: slug={Slug}", slug);
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
          }

          try
          {
            if (copy.ETag is not null) ctx.Response.Headers.ETag = copy.ETag;
            if (copy.MaxAge is not null)
              ctx.Response.Headers.CacheControl = $"public, max-age={(int)copy.MaxAge.Value.TotalSeconds}, immutable";
            else
              ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";

            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["Accept-Ranges"] = "none";
            ctx.Response.ContentType = copy.ContentType;

            ctx.Response.Headers.Remove("Content-Length");
            ctx.Response.ContentLength = null;

            await ctx.Response.StartAsync(ct);
            await copy.CopyToAsync(ctx.Response.Body, ct);

            log.LogInformation("IMG stream finished: slug={Slug}", slug);
          }
          catch (OperationCanceledException)
          {
            log.LogWarning("IMG stream aborted by client: slug={Slug}", slug);
          }
          catch (Exception ex)
          {
            log.LogError(ex, "IMG stream error: slug={Slug}", slug);
            throw;
          }
          finally
          {
            await copy.DisposeAsync();
          }
        });
      }

      return endpoints;
    }
  }
}
