using System.Text.Json.Serialization;

namespace AkelEtiket.Core;

/// <summary>Etiketlerde kullanılan firma bilgileri. GS1 kimliklerinden bağımsızdır.</summary>
public sealed class CompanyProfile
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string Website { get; set; } = "";

    /// <summary>Taşınabilir PNG; kaynak dosyanın yoluna bağımlı değildir.</summary>
    public byte[]? LogoPng { get; set; }

    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Firma Adınız" : Name;
    [JsonIgnore] public string Footer => string.Join(" • ", new[] { Name, Address, Website }
        .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.ReplaceLineEndings(" ")));

    public CompanyProfile Clone() => new()
    {
        Name = Name, Address = Address, Website = Website, LogoPng = LogoPng?.ToArray(),
    };

    public void Normalize()
    {
        Name = (Name ?? "").Trim();
        Address = (Address ?? "").Trim();
        Website = (Website ?? "").Trim();
        if (LogoPng is { Length: 0 }) LogoPng = null;
    }
}
