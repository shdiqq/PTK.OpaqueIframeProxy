using System;
using System.Collections.Generic;

namespace PTK.OpaqueIframeProxy.Interfaces
{
  /// <summary>
  /// Builder untuk menghasilkan URL proxy yang siap dipasang pada atribut <c>src</c> di &lt;iframe&gt;.
  /// Consumer cukup memberi URL sumber (origin), paket yang mengurus token, TTL, dsb.
  /// </summary>
  public interface IOpaqueIframeUrlBuilder
  {
    /// <summary>
    /// Bangun URL proxy untuk &lt;iframe src="..."&gt; dari URL origin.
    /// </summary>
    /// <param name="originIframeSrc">URL absolut halaman yang akan di-embed.</param>
    /// <param name="ttl">
    /// Override umur token; jika <c>null</c> maka menggunakan <c>OpaqueProxyOptions.HtmlTokenLifetime</c>.
    /// </param>
    /// <param name="extraClaims">Metadata opsional yang ikut diproteksi dalam token.</param>
    /// <returns>Path relatif (mis. <c>/proxy/t/{token}</c>) sesuai template route.</returns>
    string Build(string originIframeSrc, TimeSpan? ttl = null, IDictionary<string, string>? extraClaims = null);
  }
}
