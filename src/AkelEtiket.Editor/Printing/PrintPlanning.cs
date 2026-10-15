using AkelEtiket.BarcodeEngine;
using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.Gs1;
using AkelEtiket.Rendering;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.Printing;

/// <summary>Baskı planı: sıradaki etiketler + atlanan (hatalı) satırlar.</summary>
public sealed class PrintPlan
{
    public List<PrintItem> Items { get; } = [];
    public List<string> Skipped { get; } = [];
    public int TotalLabels => Items.Sum(i => i.Copies);
    public bool UsesData { get; init; }

    /// <summary>
    /// Tasarımda {SSCC} var: her fiziksel etiket ayrı bir öğedir (adet 1) ve baskı anında kendi SSCC'sini alır.
    /// </summary>
    public bool UsesSscc { get; init; }
}

/// <summary>Baskı sırasını oluşturur. Seçilen satırlar seçilen sırada, satır adetleriyle basılır.</summary>
public static class PrintPlanner
{
    public static PrintPlan Plan(LabelDocument document, DataSourceViewModel data, bool useData, int copies, int startNumber, DateTime now)
    {
        copies = Math.Clamp(copies, 1, 99999);
        // {SSCC} ve sayaçlar ({SAYAC}) her etikette farklı olmalı: aynı görüntü "adet" kadar tekrar basılamaz, her etiket
        // ayrı üretilir.
        bool usesSscc = Gs1FieldSource.DocumentUsesSscc(document);
        bool perLabel = usesSscc || CounterFields.DocumentUsesCounters(document);
        if (useData && data.HasData && data.Table is { } table)
        {
            var plan = new PrintPlan { UsesData = true, UsesSscc = usesSscc };
            int sequence = startNumber;
            foreach (var row in data.Rows)
            {
                if (!row.IsSelected || row.Copies <= 0) continue;
                if (row.Status == RowStatus.Error)
                {
                    plan.Skipped.Add(L10n.T($"Satır {row.RowNumber}: {row.Problem}"));
                    continue;
                }
                var fields = new CompositeFieldSource(row.Record.ToFieldSource(table.Columns), EditorFields.BuiltIns(sequence, now));
                if (perLabel)
                {
                    for (int i = 0; i < row.Copies; i++)
                        plan.Items.Add(new PrintItem(sequence, row.RowNumber, 1, fields));
                }
                else
                {
                    plan.Items.Add(new PrintItem(sequence, row.RowNumber, row.Copies, fields));
                }
                sequence++;
            }
            return plan;
        }

        var single = new PrintPlan { UsesData = false, UsesSscc = usesSscc };
        bool usesSequence = document.Elements.Any(e => e switch
        {
            TextElement t => FieldTemplate.GetFieldNames(t.Text).Any(IsSequenceField),
            BarcodeElement b => FieldTemplate.GetFieldNames(b.Content).Any(IsSequenceField),
            _ => false,
        });
        if (usesSequence || perLabel)
        {
            // {SIRA} kullanılıyorsa her etiket ayrı numara alır (otomatik numaralandırma); {SSCC} ya da sayaç varsa her
            // etiket baskı anında kendi değerini alır.
            for (int i = 0; i < copies; i++)
                single.Items.Add(new PrintItem(startNumber + i, null, 1, EditorFields.BuiltIns(startNumber + i, now)));
        }
        else
        {
            single.Items.Add(new PrintItem(startNumber, null, copies, EditorFields.BuiltIns(startNumber, now)));
        }
        return single;
    }

    private static bool IsSequenceField(string name) => FieldNames.Normalize(name) is "sira" or "sirano";
}
