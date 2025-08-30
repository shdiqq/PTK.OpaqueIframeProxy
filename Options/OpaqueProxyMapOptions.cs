using System;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace PTK.OpaqueIframeProxy.Options
{
  /// <summary>
  /// Konfigurasi penyimpanan mapping slug dan pembatas ukuran konten gambar.
  /// </summary>
  public sealed class OpaqueProxyMapOptions
  {
    /// <summary>
    /// Folder untuk menyimpan file mapping <c>{slug}.bin</c>.
    /// Contoh: <c>App_Data/img-map</c>.
    /// </summary>
    [Required]
    public string MapRoot { get; set; } = "App_Data/img-map";

    /// <summary>
    /// Rahasia HMAC untuk membangkitkan slug.
    /// Mendukung format base64url (karakter '-' dan '_') maupun plaintext UTF-8.
    /// </summary>
    [Required]
    public string SlugSecret { get; set; } = string.Empty;

    /// <summary>
    /// Batas ukuran maksimum file gambar (dalam byte).
    /// Default: 10 MB.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Mengubah <see cref="SlugSecret"/> menjadi array byte.
    /// Jika bisa di-decode sebagai base64url, gunakan itu; jika tidak, gunakan UTF-8.
    /// </summary>
    public byte[] GetKeyBytes()
    {
      try
      {
        var s = SlugSecret.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
      }
      catch
      {
        return Encoding.UTF8.GetBytes(SlugSecret);
      }
    }
  }
}