using System.Security.Cryptography;
using System.Text;

namespace AkelEtiket.Editor.Ai;

/// <summary>
/// API anahtarlarını Windows DPAPI (CurrentUser) ile şifreler: dosyadaki değer yalnızca bu Windows kullanıcısı
/// tarafından ve bu bilgisayarda çözülebilir. Düz metin anahtar diske yazılmaz.
/// </summary>
public static class SecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AkelEtiket2.Asistan.v1");

    public static string Protect(string secret)
    {
        if (string.IsNullOrEmpty(secret)) return "";
        byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(data);
    }

    /// <summary>Çözer; bozuk/başka kullanıcıya ait değer için null.</summary>
    public static string? Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return null;
        try
        {
            byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
