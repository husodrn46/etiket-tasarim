using AkelEtiket.BarcodeEngine;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.DataImport;
using AkelEtiket.Editor.ViewModels;

namespace AkelEtiket.Tests;

/// <summary>Koşullu gösterme: özellik paneli (geri al dahil) ve veri doğrulaması.</summary>
public class ConditionEditingTests
{
    [Fact]
    public void PropertiesPanel_EditsCondition_AndIsUndoable()
    {
        using var session = EditorTestHelpers.CreateSession();
        var text = new TextElement { Text = "İNDİRİM" };
        session.AddElement(text);
        var vm = session.GetViewModel(text)!;
        Assert.False(vm.ConditionEnabled);
        Assert.Null(vm.ConditionStatusText);

        // Alan seçilmeden işaretlemek modeli değiştirmez (yarım koşul kaydedilmez).
        vm.ConditionEnabled = true;
        Assert.True(vm.ConditionEnabled);
        Assert.Null(text.VisibleWhen);
        Assert.Equal("Koşul için bir alan seçin.", vm.ConditionStatusText);

        vm.ConditionField = "{Indirim}";
        Assert.Equal(new VisibilityCondition("Indirim"), text.VisibleWhen);
        Assert.False(vm.ConditionNeedsValue);
        vm.ConditionOperator = ConditionOperator.IsEqual;
        Assert.True(vm.ConditionNeedsValue);
        vm.ConditionValue = "Evet";
        Assert.Equal(new VisibilityCondition("Indirim", ConditionOperator.IsEqual, "Evet"), text.VisibleWhen);
        Assert.Equal(2, session.Document.GetRequiredFormatVersion());

        session.History.BreakCoalescing();
        vm.ConditionEnabled = false;
        Assert.Null(text.VisibleWhen);
        Assert.Equal(1, session.Document.GetRequiredFormatVersion());

        // Yeniden işaretlenince son koşul geri gelir.
        vm.ConditionEnabled = true;
        Assert.Equal(new VisibilityCondition("Indirim", ConditionOperator.IsEqual, "Evet"), text.VisibleWhen);
        vm.ConditionEnabled = false;

        session.Undo();
        var restored = session.Document.Elements.Single();
        Assert.Equal(new VisibilityCondition("Indirim", ConditionOperator.IsEqual, "Evet"), restored.VisibleWhen);
        var restoredVm = session.GetViewModel(restored)!;
        Assert.True(restoredVm.ConditionEnabled);
        Assert.Equal("Indirim", restoredVm.ConditionField);
        Assert.Equal("Evet", restoredVm.ConditionValue);
    }

    [Fact]
    public void PreviewRow_ShowsWhyElementIsHidden()
    {
        using var session = EditorTestHelpers.CreateSession();
        var shape = new ShapeElement { Fill = FillStyle.Black, VisibleWhen = new VisibilityCondition("Indirim") };
        session.AddElement(shape);
        var vm = session.GetViewModel(shape)!;

        session.SetFields(new RecordFieldSource(["Indirim"], [""]));
        EditorTestHelpers.WaitForRenders(session);
        Assert.True(vm.RenderResult!.HiddenByCondition);
        Assert.StartsWith("Bu önizleme satırında koşul sağlanmıyor", vm.ConditionStatusText);

        session.SetFields(new RecordFieldSource(["Indirim"], ["%20"]));
        EditorTestHelpers.WaitForRenders(session);
        Assert.False(vm.RenderResult!.HiddenByCondition);
        Assert.Equal("Yalnızca {Indirim} doluysa basılır.", vm.ConditionStatusText);
    }

    [Fact]
    public void DataValidation_SkipsBarcodesHiddenByCondition_AndReportsDateProblems()
    {
        var doc = LabelDocument.CreateBlank(60, 40);
        doc.Elements.Add(new BarcodeElement
        {
            Symbology = BarcodeType.Ean13,
            Content = "{Barkod}",
            X = 2,
            Y = 2,
            Width = 50,
            Height = 20,
            VisibleWhen = new VisibilityCondition("Bas", ConditionOperator.IsEqual, "evet"),
        });
        doc.Elements.Add(new TextElement { Text = "SKT {Uretim+365}", X = 2, Y = 25, Width = 50, Height = 8 });
        IReadOnlyList<string> columns = ["Barkod", "Bas", "Uretim"];
        var rows = new List<DataRecord>
        {
            new(2, ["8691234567890", "Evet", "13.10.2026"], columns),
            new(3, ["8691234567891", "Hayır", "13.10.2026"], columns), // hatalı barkod ama bu satırda basılmıyor
            new(4, ["8691234567891", "EVET", "13.10.2026"], columns),
            new(5, ["8691234567890", "Evet", "yarın"], columns),
        };
        var data = new DataSourceViewModel();
        data.ApplyTable(new DataSourceTable("bellek.csv", DataSourceTable.KindCsv, null, 1, columns, rows, []));
        data.ValidateAgainst(doc);

        DataRowViewModel Row(int number) => data.Rows.Single(r => r.RowNumber == number);
        Assert.NotEqual(RowStatus.Error, Row(2).Status);
        Assert.NotEqual(RowStatus.Error, Row(3).Status);
        Assert.Equal(RowStatus.Error, Row(4).Status);
        Assert.Contains("kontrol basamağı", Row(4).Problem);
        Assert.Equal(RowStatus.Error, Row(5).Status);
        Assert.Contains("{Uretim+365}", Row(5).Problem);
        Assert.Contains("tarih değil", Row(5).Problem);
    }

    [Fact]
    public void DataValidation_ReportsMissingConditionColumn_ButAcceptsArithmeticOnKnownColumns()
    {
        var doc = LabelDocument.CreateBlank(60, 40);
        doc.Elements.Add(new TextElement { Text = "{UrunAdi:BUYUK} {TARIH+30}", VisibleWhen = new VisibilityCondition("Yok") });
        IReadOnlyList<string> columns = ["UrunAdi"];
        var data = new DataSourceViewModel();
        data.ApplyTable(new DataSourceTable("bellek.csv", DataSourceTable.KindCsv, null, 1, columns, [new DataRecord(2, ["kase"], columns)], []));
        data.ValidateAgainst(doc);

        var row = Assert.Single(data.Rows);
        Assert.Equal(RowStatus.Error, row.Status);
        Assert.Contains("{Yok}", row.Problem);
        Assert.DoesNotContain("TARIH", row.Problem);
        Assert.DoesNotContain("UrunAdi", row.Problem);
    }
}
