using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Data;

namespace AkelEtiket.Design.Workspace;

/// <summary>Bir tasarımın okunmuş hâli (belge kopyası + bilgiler). Üzerinde değişiklik yapmak canlı tasarımı etkilemez.</summary>
public sealed class DesignState
{
    public required string DesignId { get; init; }
    public required DesignKind Kind { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required LabelDocument Document { get; init; }
    public required AssetStore Assets { get; init; }
    public string? FilePath { get; init; }
    public string? RelativePath { get; init; }

    /// <summary>Bu istemci şu anda bu tasarımı doğrudan değiştirebilir mi?</summary>
    public bool Editable { get; init; }

    /// <summary>Değiştirilemiyorsa nasıl değiştirileceğine dair Türkçe yönlendirme.</summary>
    public string? EditHint { get; init; }

    /// <summary>Önizlemede kullanılacak alan değerleri (veri satırı/ürün + yerleşik alanlar).</summary>
    public IFieldValueSource? PreviewFields { get; init; }

    /// <summary>Önizleme verisinin kaynağı (ör. "Ürün AKL001", "Veri satırı 3").</summary>
    public string? PreviewSource { get; init; }

    /// <summary>Taslaklar için: taslağın açıldığı andaki hedef belge.</summary>
    public LabelDocument? BaseDocument { get; init; }

    /// <summary>Taslak/yeni tasarım bilgileri.</summary>
    public WorkingDesignMeta? WorkingMeta { get; init; }

    /// <summary>Uygulamada açık mı (canlı ya da dosyası açık).</summary>
    public bool IsOpenInApp { get; init; }

    /// <summary>Açık belgede kullanıcının seçili öğeleri.</summary>
    public IReadOnlyList<string> SelectedElementIds { get; init; } = [];
}

/// <summary>Bir değişikliğin uygulandığı ortam: belge, görseller, kapsam ve toplanan uyarılar.</summary>
public sealed class DesignEditContext
{
    private readonly LabelDocument? _baseDocument;

    public DesignEditContext(LabelDocument document, AssetStore assets, IReadOnlySet<string>? allowedElementIds, LabelDocument? baseDocument,
        IFieldValueSource? previewFields)
    {
        Document = document;
        Assets = assets;
        AllowedElementIds = allowedElementIds;
        _baseDocument = baseDocument;
        PreviewFields = previewFields;
    }

    public LabelDocument Document { get; }
    public AssetStore Assets { get; }
    public IReadOnlySet<string>? AllowedElementIds { get; }
    public IFieldValueSource? PreviewFields { get; }
    public List<string> Warnings { get; } = [];
    public HashSet<string> ChangedElementIds { get; } = new(StringComparer.Ordinal);

    /// <summary>Öğeyi bulur; yazma için istenirse kilit ve kapsam denetlenir.</summary>
    public LabelElement GetElement(string? id, bool forWrite)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ToolException(ToolErrorCodes.InvalidArgument, "element_id gerekli.");
        var element = Document.FindElement(id.Trim())
                      ?? throw new ToolException(ToolErrorCodes.NotFound,
                          $"Öğe bulunamadı: {id}. Geçerli kimlikler için get_design çağırın.");
        if (forWrite) EnsureWritable(element);
        return element;
    }

    public void EnsureWritable(LabelElement element)
    {
        if (element.Locked)
            throw new ToolException(ToolErrorCodes.Locked,
                $"\"{element.DisplayName}\" ({element.Id}) kilitli. Kilitli öğeler yapay zekâ tarafından değiştirilemez; kullanıcı kilidi açmalı.");
        if (AllowedElementIds is not null && !AllowedElementIds.Contains(element.Id) && !IsCreatedHere(element.Id))
            throw new ToolException(ToolErrorCodes.PermissionDenied,
                $"\"{element.DisplayName}\" ({element.Id}) seçili nesnelerin dışında. Kullanıcı yalnızca seçili nesneler üzerinde çalışılmasını istedi.");
    }

    /// <summary>Öğe bu taslakta mı oluşturuldu (kapsam dışı olsa da değiştirilebilir).</summary>
    public bool IsCreatedHere(string id) => _baseDocument is not null && _baseDocument.FindElement(id) is null;

    public void MarkChanged(LabelElement element) => ChangedElementIds.Add(element.Id);
}

/// <summary>Değişiklik sonucu.</summary>
public sealed record MutationResult<T>(T Value, string DesignId, string Version, bool Changed, IReadOnlyList<string> Warnings);
