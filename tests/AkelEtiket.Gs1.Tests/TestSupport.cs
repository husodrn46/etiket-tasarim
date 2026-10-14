namespace AkelEtiket.Gs1.Tests;

/// <summary>Testlere özel geçici klasör; gerçek kullanıcı klasörlerine asla yazılmaz.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AkelEtiket2-Gs1Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal static class TestData
{
    /// <summary>Uydurma test öneki (gerçek bir firma öneki değildir ve GLN'den türetilmemiştir).</summary>
    public const string TestGcp = "8691234";

    /// <summary>Doğrulanmış önekli örnek profil (yalnızca test içindir).</summary>
    public static Gs1CompanyProfile VerifiedProfile(string gcp = TestGcp, long? start = null, long? end = null, int ext = 0) => new()
    {
        CompanyName = "Sentetik Test Firması",
        Gln = "1234567890128",
        GcpPrefix = gcp,
        GcpVerified = true,
        GcpVerifiedAt = DateTimeOffset.Now,
        SsccExtensionDigit = ext,
        SerialStart = start,
        SerialEnd = end,
    };

    /// <summary>Bağımsız (barkod motorundan ayrı yazılmış) GS1 mod 10 hesabı.</summary>
    public static int ReferenceCheckDigit(string digitsWithoutCheck)
    {
        int sum = 0;
        for (int i = 0; i < digitsWithoutCheck.Length; i++)
        {
            int d = digitsWithoutCheck[digitsWithoutCheck.Length - 1 - i] - '0';
            sum += i % 2 == 0 ? d * 3 : d;
        }
        return (10 - sum % 10) % 10;
    }

    /// <summary>SSCC'den seri referansını okur.</summary>
    public static long SerialOf(string sscc, int gcpLength) =>
        long.Parse(sscc.Substring(1 + gcpLength, 16 - gcpLength), System.Globalization.CultureInfo.InvariantCulture);
}
