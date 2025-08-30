using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace PTK.OpaqueIframeProxy.Options
{
  /// <summary>
  /// Konfigurasi utama paket (whitelist host, base path, TTL token, dan template route).
  /// </summary>
  public sealed class OpaqueProxyOptions
  {
    /// <summary>Whitelist host HTML yang boleh di-proxy.</summary>
    [Required, MinLength(1)]
    public List<string> AllowedHtmlHosts { get; set; } = new();

    /// <summary>Whitelist host gambar yang boleh di-proxy.</summary>
    [Required, MinLength(1)]
    public List<string> AllowedImageHosts { get; set; } = new();

    /// <summary>Prefix path default untuk endpoint (boleh kosong untuk root).</summary>
    [RegularExpression("^[a-zA-Z0-9\\-/]*$")]
    public string BasePath { get; set; } = "proxy";

    /// <summary>Umur token HTML.</summary>
    [Required]
    public TimeSpan HtmlTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Template route yang bisa dikustomisasi di appsettings.</summary>
    [Required]
    public OpaqueProxyRouteOptions Routes { get; set; } = new();

    /// <summary>Maksimum ukuran HTML (bytes) sebelum ditolak. Default: 2 MB.</summary>
    [Range(1, int.MaxValue)]
    public int MaxHtmlSizeBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>Batas maksimum jumlah &lt;img&gt; yang direwrite. Default: 300.</summary>
    [Range(1, int.MaxValue)]
    public int MaxImgRewrite { get; set; } = 300;
  }

  /// <summary>
  /// Template route fleksibel. Wajib mengandung placeholder {token} (HTML) dan {slug} (gambar).
  /// Placeholder opsional: {basePath}.
  /// </summary>
  public sealed class OpaqueProxyRouteOptions
  {
    /// <summary>Template endpoint HTML. Contoh: "/{basePath}/t/{token}" atau "/embed/page/{token}"</summary>
    [Required]
    public string HtmlTokenTemplate { get; set; } = "/{basePath}/t/{token}";

    /// <summary>Template endpoint gambar. Contoh: "/{basePath}/img/s/{slug}" atau "/assets/proxy/{slug}"</summary>
    [Required]
    public string ImageSlugTemplate { get; set; } = "/{basePath}/img/s/{slug}";

    /// <summary>Jika false, paket tidak akan automap endpoint—consumer dapat memetakan manual.</summary>
    public bool AutoMapEndpoints { get; set; } = true;
  }
}