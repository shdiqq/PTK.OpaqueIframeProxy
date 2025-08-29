using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PTK.OpaqueIframeProxy.Interfaces;
using PTK.OpaqueIframeProxy.Options;

namespace PTK.OpaqueIframeProxy.Extensions
{
  /// <summary>Ekstensi untuk mendaftarkan endpoint HTML &amp; gambar.</summary>
  public static class EndpointMapping
  {
    /// <summary>Mendaftarkan endpoint sesuai <see cref="OpaqueProxyOptions.Routes"/>.</summary>
    public static IEndpointRouteBuilder MapOpaqueContentProxy(this IEndpointRouteBuilder endpoints)
    {
      var opt = endpoints.ServiceProvider.GetRequiredService<IOptions<OpaqueProxyOptions>>().Value;

      static string Resolve(string? tpl, string basePath)
      {
        var s = (tpl ?? "/").Replace("{basePath}", basePath, StringComparison.OrdinalIgnoreCase);
        return s.StartsWith('/') ? s : "/" + s;
      }

      var basePath = (opt.BasePath ?? "proxy").Trim('/');
      var htmlRoute = Resolve(opt.Routes.HtmlTokenTemplate, basePath);
      var imgRoute = Resolve(opt.Routes.ImageSlugTemplate, basePath);

      if (opt.Routes.AutoMapEndpoints)
      {
        var htmlPattern = htmlRoute;
        var qIdx = htmlPattern.IndexOf('?', StringComparison.Ordinal);
        if (qIdx >= 0) htmlPattern = htmlPattern[..qIdx];
        htmlPattern = htmlPattern.Replace("{token}", "{token?}", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(htmlPattern)) htmlPattern = "/";

        endpoints.MapGet(htmlPattern,
          async (HttpContext ctx, IOpaqueContentProxyService svc, CancellationToken ct) =>
          {
            string? token = ctx.GetRouteValue("token") as string;

            if (string.IsNullOrEmpty(token) && ctx.Request.Query.TryGetValue("token", out var qv))
              token = qv.ToString();

            if (string.IsNullOrEmpty(token))
            {
              ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
              await ctx.Response.WriteAsync("Missing token", ct);
              return;
            }

            var r = await svc.GetHtmlByTokenAsync(token, ct);

            if (r.NoCache)
              ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";

            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.ContentType = r.ContentType;
            await ctx.Response.WriteAsync(r.Html, ct);
          });

        endpoints.MapGet(imgRoute,
          async (string slug, IOpaqueContentProxyService svc, HttpContext ctx, CancellationToken ct) =>
          {
            var copy = await svc.PrepareImageCopyAsync(slug, preferOriginal: false, ct);
            if (copy is null)
            {
              ctx.Response.StatusCode = StatusCodes.Status404NotFound;
              return;
            }

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

            try { await copy.CopyToAsync(ctx.Response.Body, ct); }
            catch (OperationCanceledException) { }
            finally { await copy.DisposeAsync(); }
          });
      }

      return endpoints;
    }
  }
}
