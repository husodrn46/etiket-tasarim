using AkelEtiket.DataImport;

namespace AkelEtiket.DataImport.Tests;

/// <summary>Regresyon: kodlama listesi, hiçbir CSV okunmadan önce de (ör. önce Excel açılınca) oluşturulabilmeli.</summary>
public class ImportChoicesTests
{
    [Fact]
    public void Encodings_IncludeTurkishCodePages_WithoutPriorCsvImport()
    {
        Assert.Contains(ImportChoices.Encodings, c => c.Encoding?.CodePage == 1254);
        Assert.Contains(ImportChoices.Encodings, c => c.Encoding?.CodePage == 28599);
        Assert.Null(ImportChoices.Encodings[0].Encoding);
    }
}
