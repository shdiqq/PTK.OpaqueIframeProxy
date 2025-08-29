using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using PTK.OpaqueIframeProxy.Interfaces;

namespace PTK.OpaqueIframeProxy.TagHelpers
{
  /// <summary>
  /// TagHelper untuk menghasilkan &lt;iframe&gt; yang menunjuk ke URL proxy.
  /// Contoh pemakaian di Razor:
  /// &lt;opaque-iframe src="https://example.com/report?id=42" width="100%" height="800" style="border:0"&gt;&lt;/opaque-iframe&gt;
  /// </summary>
  [HtmlTargetElement("opaque-iframe", Attributes = "src")]
  public sealed class OpaqueIframeTagHelper : TagHelper
  {
    private readonly IOpaqueIframeUrlBuilder _builder;

    /// <summary>DI: builder URL proxy.</summary>
    public OpaqueIframeTagHelper(IOpaqueIframeUrlBuilder builder) => _builder = builder;

    /// <summary>
    /// Context view saat rendering Razor, otomatis diisi oleh ASP.NET Core.
    /// Dipakai untuk membaca <c>HttpContext.Request.PathBase</c>
    /// sehingga <c>src</c> iframe bisa diprefix jika aplikasi tidak di-root.
    /// </summary>
    [ViewContext]
    public ViewContext? ViewContext { get; set; }

    /// <summary>URL absolut halaman origin yang akan di-embed.</summary>
    [HtmlAttributeName("src")]
    public string Source { get; set; } = default!;

    /// <summary>Lebar iframe (opsional).</summary>
    [HtmlAttributeName("width")]
    public string? Width { get; set; }

    /// <summary>Tinggi iframe (opsional).</summary>
    [HtmlAttributeName("height")]
    public string? Height { get; set; }

    /// <summary>Inline style (opsional).</summary>
    [HtmlAttributeName("style")]
    public string? Style { get; set; }

    /// <summary>Override TTL token (opsional), contoh: "00:30:00".</summary>
    [HtmlAttributeName("ttl")]
    public TimeSpan? TtlOverride { get; set; }

    /// <summary>
    /// Atribut sandbox opsional untuk iframe. Contoh:
    /// <c>allow-scripts allow-same-origin</c>.
    /// </summary>
    [HtmlAttributeName("sandbox")]
    public string? Sandbox { get; set; }

    /// <summary>
    /// Atribut referrerpolicy opsional untuk iframe.
    /// Contoh: <c>no-referrer</c> atau <c>strict-origin</c>.
    /// </summary>
    [HtmlAttributeName("referrerpolicy")]
    public string? ReferrerPolicy { get; set; }

    /// <summary>Render elemen &lt;iframe&gt; dengan src yang sudah diproxy.</summary>
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
      var proxied = _builder.Build(Source, TtlOverride);

      // Prefix PathBase jika aplikasi tidak di root
      var pathBase = ViewContext?.HttpContext?.Request.PathBase.Value ?? string.Empty;
      if (!string.IsNullOrEmpty(pathBase) && proxied.StartsWith("/"))
        proxied = pathBase + proxied;

      output.TagName = "iframe";
      output.Attributes.SetAttribute("src", proxied);
      if (!string.IsNullOrWhiteSpace(Width)) output.Attributes.SetAttribute("width", Width);
      if (!string.IsNullOrWhiteSpace(Height)) output.Attributes.SetAttribute("height", Height);
      if (!string.IsNullOrWhiteSpace(Style)) output.Attributes.SetAttribute("style", Style);
      if (!string.IsNullOrWhiteSpace(Sandbox)) output.Attributes.SetAttribute("sandbox", Sandbox);
      if (!string.IsNullOrWhiteSpace(ReferrerPolicy)) output.Attributes.SetAttribute("referrerpolicy", ReferrerPolicy);

      output.TagMode = TagMode.StartTagAndEndTag;
    }
  }
}