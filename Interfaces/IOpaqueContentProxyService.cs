namespace PTK.OpaqueIframeProxy;

/// <summary>
/// Layanan inti untuk melayani konten proxy.
/// </summary>
public interface IOpaqueContentProxyService
{
  /// <summary>Ambil HTML dari origin berdasarkan token, lalu (nantinya) rewrite resource.</summary>
  Task<HtmlResult> GetHtmlByTokenAsync(string token, CancellationToken ct = default);

  /// <summary>Ambil gambar dari origin berdasarkan slug yang sudah dimapping.</summary>
  Task<ImageResult> GetImageBySlugAsync(string slug, bool debug = false, CancellationToken ct = default);
}

/// <summary>Hasil pemrosesan HTML.</summary>
public sealed record HtmlResult(
    string Html,
    string ContentType = "text/html; charset=utf-8",
    bool NoCache = true);

/// <summary>Hasil pemrosesan gambar.</summary>
public sealed record ImageResult(
    Stream Content,
    string ContentType,
    long? Length = null,
    string? ETag = null,
    TimeSpan? MaxAge = null);
