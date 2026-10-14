using System.Diagnostics;
using AkelEtiket.BarcodeEngine;
using AkelEtiket.Core.Model;
using AkelEtiket.DataImport;
using AkelEtiket.Editor.ViewModels;

namespace AkelEtiket.Tests;

/// <summary>Veri tablosu: arama, hata/seçim süzgeci, satır numarasıyla seçim ve büyük tabloda hız.</summary>
public class DataTableFilterTests
{
    private static readonly string[] Columns = ["Kod", "UrunAdi", "Barkod"];

    /// <summary>Başlık 1. satırda; veri satırları 2'den başlar (Excel'deki gibi).</summary>
    private static DataSourceViewModel Load(params string[][] rows)
    {
        var records = rows.Select((values, i) => new DataRecord(i + 2, values, Columns)).ToList();
        var data = new DataSourceViewModel();
        data.ApplyTable(new DataSourceTable("urunler.xlsx", DataSourceTable.KindExcel, "Sayfa1", 1, Columns, records, []));
        return data;
    }

    private static DataSourceViewModel Sample() => Load(
        ["TST001", "IŞIKLI Servis Tabağı", "8691234567890"],
        ["TST002", "Şişe Kapağı", "8691234567891"],
        ["TST003", "kase ılık", "8691234567890"],
        ["TST004", "İnce Kase", "8691234567890"],
        ["TST005", "Çay Tabağı", "8691234567890"]);

    private static List<int> Visible(DataSourceViewModel data) => data.VisibleRows.Select(r => r.RowNumber).ToList();

    [Theory]
    [InlineData("isikli", new[] { 2 })]
    [InlineData("IŞIKLI", new[] { 2 })]
    [InlineData("sise", new[] { 3 })]
    [InlineData("ŞİŞE", new[] { 3 })]
    [InlineData("KASE", new[] { 4, 5 })]
    [InlineData("ince", new[] { 5 })]
    [InlineData("ilik", new[] { 4 })]
    [InlineData("tabagi cay", new[] { 6 })]
    [InlineData("TST00", new[] { 2, 3, 4, 5, 6 })]
    [InlineData("yok", new int[0])]
    public void Search_IsTurkishInsensitive_AcrossAllCells(string query, int[] expected)
    {
        var data = Sample();
        data.SearchText = query;
        Assert.Equal(expected, Visible(data));
        Assert.True(data.IsFiltered);
        Assert.Equal($"{expected.Length} / 5 satır gösteriliyor", data.VisibleSummary);

        data.SearchText = "  ";
        Assert.False(data.IsFiltered);
        Assert.Same(data.Rows, data.VisibleRows);
        Assert.Equal("5 satır", data.VisibleSummary);
    }

    [Fact]
    public void ErrorFilter_AndSearchInProblemText()
    {
        var data = Sample();
        var doc = LabelDocument.CreateBlank(60, 40);
        doc.Elements.Add(new BarcodeElement { Symbology = BarcodeType.Ean13, Content = "{Barkod}", X = 2, Y = 2, Width = 50, Height = 20 });
        data.ValidateAgainst(doc);

        data.OnlyErrorRows = true;
        Assert.Equal([3], Visible(data));
        data.OnlyErrorRows = false;
        data.SearchText = "kontrol basamagi";
        Assert.Equal([3], Visible(data));
    }

    [Fact]
    public void SelectedFilter_AndVisibleSelectionCommands()
    {
        var data = Sample();
        data.SearchText = "kase";
        Assert.Equal("Görünen 2 satırı seç", data.SelectVisibleLabel);
        data.DeselectVisibleRows();
        Assert.Equal([2, 3, 6], data.Rows.Where(r => r.IsSelected).Select(r => r.RowNumber));

        data.SearchText = "";
        Assert.Equal("Tümünü seç", data.SelectVisibleLabel);
        data.OnlySelectedRows = true;
        Assert.Equal([2, 3, 6], Visible(data));
        // Gizlenen satırların seçimi değişmez; görünenlerin kaldırılması listeyi boşaltır.
        data.DeselectVisibleRows();
        Assert.Empty(data.VisibleRows);
        Assert.Equal(0, data.SelectedLabelCount);
        data.OnlySelectedRows = false;
        data.SelectVisibleRows();
        Assert.All(data.Rows, r => Assert.True(r.IsSelected));
    }

    [Fact]
    public void RangeSelection_ReplacesSelection_ByFileRowNumber()
    {
        var data = Sample();
        data.RowRangeText = "3-4, 6";
        data.SelectRowRange();
        Assert.Equal([3, 4, 6], data.Rows.Where(r => r.IsSelected).Select(r => r.RowNumber));
        Assert.False(data.RangeMessageIsError);
        Assert.Equal("3 satır seçildi.", data.RangeMessage);

        data.RowRangeText = "1, 5-, 40-42";
        data.SelectRowRange();
        Assert.Equal([5, 6], data.Rows.Where(r => r.IsSelected).Select(r => r.RowNumber));
        Assert.Equal("2 satır seçildi. Dosyada veri satırı olmayan: 1 (başlık satırı), 40-42.", data.RangeMessage);
    }

    [Fact]
    public void RangeSelection_ReportsErrorRows_AndKeepsSelectionOnBadInput()
    {
        var data = Sample();
        var doc = LabelDocument.CreateBlank(60, 40);
        doc.Elements.Add(new BarcodeElement { Symbology = BarcodeType.Ean13, Content = "{Barkod}", X = 2, Y = 2, Width = 50, Height = 20 });
        data.ValidateAgainst(doc);
        data.RowRangeText = "2–3";
        data.SelectRowRange();
        Assert.Equal("2 satır seçildi; 1 tanesi hatalı olduğu için basılmaz.", data.RangeMessage);

        data.RowRangeText = "2-x";
        data.SelectRowRange();
        Assert.True(data.RangeMessageIsError);
        Assert.Contains("\"2-x\" anlaşılamadı", data.RangeMessage);
        Assert.Equal([2, 3], data.Rows.Where(r => r.IsSelected).Select(r => r.RowNumber));

        data.RowRangeText = "100-200";
        data.SelectRowRange();
        Assert.True(data.RangeMessageIsError);
        Assert.Contains("seçim değiştirilmedi", data.RangeMessage);
        Assert.Contains("2–6", data.RangeMessage);
        Assert.Equal([2, 3], data.Rows.Where(r => r.IsSelected).Select(r => r.RowNumber));
    }

    [Theory]
    [InlineData("2-20, 35, 40-42", "2-20|35|40-42")]
    [InlineData("2 - 20;35 40", "2-20|35|40")]
    [InlineData("5–7", "5-7")]
    [InlineData("40-", "40-")]
    [InlineData(" 7 ", "7")]
    public void RangeParser_AcceptsCommonForms(string text, string expected)
    {
        Assert.True(RowRangeSet.TryParse(text, out var set, out string? error), error);
        Assert.Equal(expected, string.Join("|", set!.Ranges));
    }

    [Theory]
    [InlineData("", "Satır numaralarını yazın")]
    [InlineData("abc", "\"abc\" anlaşılamadı")]
    [InlineData("-5", "\"-5\" anlaşılamadı")]
    [InlineData("1.5", "\"1.5\" anlaşılamadı")]
    [InlineData("20-5", "aralığın başı sonundan büyük")]
    [InlineData("0-3", "1'den başlar")]
    [InlineData("12345678901", "anlaşılamadı")]
    public void RangeParser_RejectsInvalidInput_WithTurkishMessage(string text, string expectedMessage)
    {
        Assert.False(RowRangeSet.TryParse(text, out var set, out string? error));
        Assert.Null(set);
        Assert.Contains(expectedMessage, error);
    }

    [Fact]
    public void RangeSet_ContainsUsesMergedRanges()
    {
        Assert.True(RowRangeSet.TryParse("40-42, 2-20, 10-25, 100-", out var set, out _));
        Assert.True(set!.Contains(2));
        Assert.True(set.Contains(25));
        Assert.False(set.Contains(26));
        Assert.True(set.Contains(41));
        Assert.False(set.Contains(99));
        Assert.True(set.Contains(int.MaxValue));
        Assert.False(set.Contains(1));
    }

    [Fact]
    public void LargeTable_StaysFast_AndBulkSelectionNotifiesOnce()
    {
        var rows = Enumerable.Range(0, 10_000)
            .Select(i => new[] { $"TST{i:00000}", i % 7 == 0 ? "Işıklı Tabak" : "Kase", "8691234567890" })
            .ToArray();
        var data = Load(rows);
        int notifications = 0;
        data.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DataSourceViewModel.SelectedLabelCount)) notifications++;
        };

        var watch = Stopwatch.StartNew();
        data.SearchText = "ISIKLI";
        Assert.Equal(1429, data.VisibleRows.Count);
        data.DeselectVisibleRows();
        data.SearchText = "";
        data.SelectVisibleRows();
        data.RowRangeText = "2-5001, 9000-";
        data.SelectRowRange();
        watch.Stop();

        Assert.Equal(3, notifications);
        Assert.Equal(5000 + 1002, data.Rows.Count(r => r.IsSelected)); // 2–5001 ve 9000–10001
        Assert.True(watch.ElapsedMilliseconds < 3000, $"{watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task LoadAsync_OldExcel_ListsSheetsAndRows()
    {
        var data = new DataSourceViewModel();
        await data.LoadAsync(EditorTestHelpers.FindRepoFile("tests", "AkelEtiket.DataImport.Tests", "Fixtures", "eski-excel.xls"));
        Assert.True(data.IsExcel);
        Assert.Equal(["Boş Sayfa", "Gizli", "Ürünler", "Fiyatlar"], data.SheetNames);
        Assert.Equal("Ürünler", data.SelectedSheet);
        Assert.Equal([3, 4, 6], data.Rows.Select(r => r.RowNumber));
        data.SearchText = "ışıklı";
        Assert.Equal([4], Visible(data));
    }

    [Fact]
    public void NewTable_ResetsFilters()
    {
        var data = Sample();
        data.SearchText = "kase";
        data.OnlyErrorRows = true;
        data.RowRangeText = "x";
        data.SelectRowRange();
        data.ApplyTable(new DataSourceTable("b.csv", DataSourceTable.KindCsv, null, 1, Columns, [new DataRecord(2, ["A", "B", "C"], Columns)], []));
        Assert.Equal("", data.SearchText);
        Assert.False(data.OnlyErrorRows);
        Assert.Null(data.RangeMessage);
        Assert.Same(data.Rows, data.VisibleRows);
        Assert.Single(data.VisibleRows);
    }
}
