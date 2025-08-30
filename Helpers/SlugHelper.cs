using System.Security.Cryptography;
using System.Text;

namespace PTK.OpaqueIframeProxy.Helpers
{
  internal static class SlugHelper
  {
    public static string ComputeSlug(string input, byte[] key)
    {
      using var h = new HMACSHA256(key);
      var hash = h.ComputeHash(Encoding.UTF8.GetBytes(input));

#if NET5_0_OR_GREATER
      return Convert.ToHexString(hash).ToLowerInvariant();
#else
      var sb = new StringBuilder(hash.Length * 2);
      foreach (var b in hash) sb.Append(b.ToString("x2"));
      return sb.ToString();
#endif
    }
  }
}
