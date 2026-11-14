using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using AkelEtiket.Editor.Ai;

namespace AkelEtiket.Tests;

/// <summary>
/// Windows dışındaki sistemlerde testler gerçek Anahtar Zinciri'ne dokunmaz: şifreleme anahtarı bellekte üretilir.
/// Gerçek zincir yalnızca <see cref="SecretStorageTests.MacKeychain_StoresReadsAndDeletesSecret"/> içinde,
/// teste özel ve sonunda silinen bir öğeyle denenir.
/// </summary>
internal static class TestSecretKey
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (!OperatingSystem.IsWindows()) SecretProtector.MasterKeySource = () => Key;
    }
}

public class SecretStorageTests
{
    [Fact]
    public void KeyedFormat_RoundTrips_AndNeverContainsPlainText()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        string first = SecretProtector.ProtectWithKey(Encoding.UTF8.GetBytes("sk-ant-çok-gizli"), key);
        string second = SecretProtector.ProtectWithKey(Encoding.UTF8.GetBytes("sk-ant-çok-gizli"), key);

        Assert.StartsWith("k1:", first);
        Assert.DoesNotContain("gizli", first);
        Assert.NotEqual(first, second); // her şifrelemede yeni nonce
        Assert.Equal("sk-ant-çok-gizli", Encoding.UTF8.GetString(SecretProtector.UnprotectWithKey(first, key)!));
    }

    [Fact]
    public void KeyedFormat_RejectsWrongKey_Tampering_AndForeignValues()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        string value = SecretProtector.ProtectWithKey(Encoding.UTF8.GetBytes("sk-test-123"), key);

        Assert.ThrowsAny<CryptographicException>(() => SecretProtector.UnprotectWithKey(value, RandomNumberGenerator.GetBytes(32)));

        byte[] raw = Convert.FromBase64String(value[3..]);
        raw[^1] ^= 0x01;
        Assert.ThrowsAny<CryptographicException>(() => SecretProtector.UnprotectWithKey("k1:" + Convert.ToBase64String(raw), key));

        // Windows'ta (DPAPI) üretilmiş ya da tanınmayan değer: çözülmez, hata da vermez.
        Assert.Null(SecretProtector.UnprotectWithKey(Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)), key));
        Assert.Null(SecretProtector.UnprotectWithKey("k1:" + Convert.ToBase64String(new byte[8]), key));
    }

    [MacFact]
    public void MacKeychain_StoresReadsAndDeletesSecret()
    {
        string service = "LabelHarbor.Tests." + Guid.NewGuid().ToString("N");
        const string account = "test";
        try
        {
            Assert.Null(MacKeychain.Find(service, account));

            byte[] created = MacKeychain.GetOrCreate(service, account, 32);
            Assert.Equal(32, created.Length);
            Assert.Equal(created, MacKeychain.Find(service, account));
            Assert.Equal(created, MacKeychain.GetOrCreate(service, account, 32)); // ikinci çağrı yeni anahtar üretmez

            Assert.False(MacKeychain.TryAdd(service, account, RandomNumberGenerator.GetBytes(32))); // var olanın üzerine yazmaz
            Assert.Equal(created, MacKeychain.Find(service, account));
        }
        finally
        {
            MacKeychain.Delete(service, account);
        }
        Assert.Null(MacKeychain.Find(service, account));
        Assert.False(MacKeychain.Delete(service, account));
    }
}

/// <summary>
/// Paylaşılan anahtar kaynağını geçici olarak değiştirir; bu yüzden onu kullanan masaüstü testleriyle aynı
/// koleksiyondadır (aynı anda çalışmazlar).
/// </summary>
[Collection("Yapay zekâ masaüstü")]
public class SecretStoreUnavailableTests
{
    [NonWindowsFact]
    public void ProtectThrows_AndUnprotectReturnsNull_WhenSecureStoreIsUnavailable()
    {
        string value = SecretProtector.Protect("sk-test-123");
        var previous = SecretProtector.MasterKeySource;
        try
        {
            SecretProtector.MasterKeySource = () => throw new SecretStorageException("erişilemiyor");
            Assert.Null(SecretProtector.Unprotect(value));
            Assert.Throws<SecretStorageException>(() => SecretProtector.Protect("sk-test-123"));
        }
        finally
        {
            SecretProtector.MasterKeySource = previous;
        }
        Assert.Equal("sk-test-123", SecretProtector.Unprotect(value));
    }
}
