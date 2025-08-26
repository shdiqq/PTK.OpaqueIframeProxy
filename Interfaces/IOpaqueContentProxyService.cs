namespace PTK.OpaqueIframeProxy.Interfaces
{
  /// <summary>
  /// Layanan inti untuk melayani konten proxy (HTML and gambar).
  /// </summary>
  public interface IOpaqueContentProxyService
  {
    /// <summary>Ambil HTML dari origin berdasarkan token, lalu (nantinya) rewrite resource.</summary>
    Task<HtmlResult> GetHtmlByTokenAsync(string token, CancellationToken ct = default);

    /// <summary>Ambil gambar dari origin berdasarkan slug yang sudah dimapping (mode lama, tidak dipakai endpoint baru).</summary>
    Task<ImageResult> GetImageBySlugAsync(string slug, bool debug = false, CancellationToken ct = default);

    /// <summary>
    /// Siapkan penyalinan konten gambar (streaming) ke response secara aman.
    /// Endpoint akan memanggil <see cref="ImageCopyHandle.CopyToAsync"/> untuk menyalin,
    /// lalu <see cref="ImageCopyHandle.DisposeAsync"/> untuk membersihkan resource.
    /// </summary>
    Task<ImageCopyHandle?> PrepareImageCopyAsync(string slug, bool preferOriginal = false, CancellationToken ct = default);
  }

  /// <summary>Hasil pemrosesan HTML.</summary>
  public sealed record HtmlResult(
      string Html,
      string ContentType = "text/html; charset=utf-8",
      bool NoCache = true
  );

  /// <summary>Hasil pemrosesan gambar (mode non-delegate).</summary>
  public sealed record ImageResult(
      Stream Content,
      string ContentType,
      long? Length = null,
      string? ETag = null,
      TimeSpan? MaxAge = null
  );

  /// <summary>
  /// Handle untuk menyalin konten gambar secara streaming dan mengelola lifecycle resource (HttpResponseMessage/Stream).
  /// </summary>
  public sealed class ImageCopyHandle : IAsyncDisposable
  {
    private readonly Func<Stream, CancellationToken, Task> _copy;
    private readonly IDisposable? _owner; // NOTE: HttpResponseMessage = IDisposable, bukan IAsyncDisposable

    /// <summary>Content-Type sumber yang akan dikirim ke klien.</summary>
    public string ContentType { get; }

    /// <summary>Ukuran konten bila diketahui (dari Content-Length); null jika tidak ada.</summary>
    public long? Length { get; }

    /// <summary>ETag dari origin (jika ada) untuk caching.</summary>
    public string? ETag { get; }

    /// <summary>Masa cache maksimum yang direkomendasikan.</summary>
    public TimeSpan? MaxAge { get; }

    /// <summary>Buat handle penyalinan streaming.</summary>
    public ImageCopyHandle(
      Func<Stream, CancellationToken, Task> copy,
      string contentType,
      long? length,
      string? etag,
      TimeSpan? maxAge,
      IDisposable? owner)
    {
      _copy = copy ?? throw new ArgumentNullException(nameof(copy));
      ContentType = contentType ?? "application/octet-stream";
      Length = length;
      ETag = etag;
      MaxAge = maxAge;
      _owner = owner;
    }

    /// <summary>Salin konten sumber ke <paramref name="destination"/> secara streaming.</summary>
    public Task CopyToAsync(Stream destination, CancellationToken ct) => _copy(destination, ct);

    /// <summary>Bersihkan resource sumber.</summary>
    public ValueTask DisposeAsync()
    {
      _owner?.Dispose(); // HttpResponseMessage hanya IDisposable
      return ValueTask.CompletedTask;
    }
  }
}
