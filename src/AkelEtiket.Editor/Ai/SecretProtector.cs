using System.Security.Cryptography;
using System.Text;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.Ai;

/// <summary>API anahtarı bu bilgisayarın güvenli deposunda saklanamadı. İleti kullanıcıya gösterilebilir.</summary>
public sealed class SecretStorageException(string message) : Exception(message)
{
    internal static SecretStorageException Keychain(int status) =>
        new(L10n.T("API anahtarı macOS Anahtar Zinciri'ne kaydedilemedi veya oradan okunamadı.") + $" (OSStatus {status})");

    internal static SecretStorageException Unsupported() =>
        new(L10n.T("Bu işletim sisteminde API anahtarı güvenli biçimde saklanamıyor."));
}

/// <summary>
/// API anahtarlarını şifreler; düz metin anahtar diske yazılmaz.
/// Windows: DPAPI (CurrentUser); değer yalnızca bu Windows kullanıcısı tarafından ve bu bilgisayarda çözülebilir.
/// macOS: AES-256-GCM; şifreleme anahtarı kullanıcının Anahtar Zinciri'nde durur, ayar dosyasında yalnızca şifreli
/// değer bulunur. Bir sistemde şifrelenen değer diğerinde çözülemez (anahtar yeniden girilir).
/// </summary>
public static class SecretProtector
{
    private const string KeyedPrefix = "k1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MasterKeySize = 32;
    private const string KeychainService = "LabelHarbor";
    private const string KeychainAccount = "api-key-encryption-v1";

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AkelEtiket2.Asistan.v1");
    private static readonly Lock MasterKeyLock = new();
    private static byte[]? _masterKey;

    /// <summary>Windows dışındaki sistemlerde şifreleme anahtarının kaynağı (testlerde değiştirilir).</summary>
    internal static Func<byte[]> MasterKeySource { get; set; } = DefaultMasterKey;

    /// <summary>Şifreler. Güvenli depoya erişilemezse <see cref="SecretStorageException"/>.</summary>
    public static string Protect(string secret)
    {
        if (string.IsNullOrEmpty(secret)) return "";
        byte[] plain = Encoding.UTF8.GetBytes(secret);
        if (OperatingSystem.IsWindows())
            return Convert.ToBase64String(ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
        return ProtectWithKey(plain, MasterKeySource());
    }

    /// <summary>Çözer; bozuk/başka kullanıcıya ait değer ya da erişilemeyen güvenli depo için null.</summary>
    public static string? Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return null;
        try
        {
            byte[]? data = OperatingSystem.IsWindows()
                ? ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser)
                : UnprotectWithKey(protectedValue, MasterKeySource());
            return data is null ? null : Encoding.UTF8.GetString(data);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or SecretStorageException)
        {
            return null;
        }
    }

    /// <summary>"k1:" + Base64(nonce | etiket | şifreli veri).</summary>
    internal static string ProtectWithKey(byte[] plain, byte[] key)
    {
        byte[] output = new byte[NonceSize + TagSize + plain.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize), Entropy);
        return KeyedPrefix + Convert.ToBase64String(output);
    }

    /// <summary>Biçim tanınmıyorsa null; veri değiştirilmişse ya da anahtar yanlışsa <see cref="CryptographicException"/>.</summary>
    internal static byte[]? UnprotectWithKey(string protectedValue, byte[] key)
    {
        if (!protectedValue.StartsWith(KeyedPrefix, StringComparison.Ordinal)) return null;
        byte[] input = Convert.FromBase64String(protectedValue[KeyedPrefix.Length..]);
        if (input.Length < NonceSize + TagSize) return null;
        byte[] plain = new byte[input.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(input.AsSpan(0, NonceSize), input.AsSpan(NonceSize + TagSize), input.AsSpan(NonceSize, TagSize), plain, Entropy);
        return plain;
    }

    private static byte[] DefaultMasterKey()
    {
        if (!OperatingSystem.IsMacOS()) throw SecretStorageException.Unsupported();
        lock (MasterKeyLock)
        {
            // Yalnızca başarılı okuma saklanır: kullanıcı zincir iznini sonradan verirse yeniden denenir.
            return _masterKey ??= MacKeychain.GetOrCreate(KeychainService, KeychainAccount, MasterKeySize);
        }
    }
}
