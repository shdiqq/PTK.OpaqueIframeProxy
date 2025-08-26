using System.Security.Cryptography;
using System.Text;

namespace PTK.OpaqueIframeProxy.Helpers
{
  /// <summary>
  /// Utility untuk membuat slug dari URL gambar menggunakan HMAC-SHA256.
  /// </summary>
  internal static class SlugHelper
  {
    public static string ComputeSlug(string input, byte[] key)
    {
      using var h = new HMACSHA256(key);
      var hash = h.ComputeHash(Encoding.UTF8.GetBytes(input));
      return Convert.ToHexString(hash).ToLowerInvariant();
    }
  }
}