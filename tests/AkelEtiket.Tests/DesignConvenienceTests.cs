using AkelEtiket.Core;
using AkelEtiket.Core.Geometry;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Documents;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.ProjectManager;

namespace AkelEtiket.Tests;

/// <summary>
/// Tasarım kolaylıkları (oturum düzeyi): tuvalde yazı düzenleme, biçim boyacısı, açıklamalı geri alma geçmişi,
/// grup boyutlandırma ve kopyalanan grupların yeni grup kimliği alması.
/// </summary>
public sealed class DesignConvenienceTests
{
    [Fact]
    public void InlineCommit_IsOneUndoStep_AndNormalizesLineBreaks()
    {
        using var session = EditorTestHelpers.CreateSession();
        var vm = session.AddElement(new TextElement { Text = "Eski", X = 10, Y = 10, Width = 40, Height = 8 });
        var text = (TextElement)vm.Model;
        int steps = session.History.UndoEntries.Count;

        Assert.True(InlineTextEditing.Commit(session, text, "Yeni\r\nsatır\r{UrunAdi}"));

        Assert.Equal("Yeni\nsatır\n{UrunAdi}", text.Text);
        Assert.Equal(steps + 1, session.History.UndoEntries.Count);
        Assert.Equal("Metin düzenlendi", session.History.UndoDescription);
        session.Undo();
        Assert.Equal("Eski", Assert.IsType<TextElement>(session.Document.FindElement(text.Id)).Text);
    }

    [Fact]
    public void InlineCommit_UnchangedLockedOrDeleted_DoesNothing()
    {
        using var session = EditorTestHelpers.CreateSession();
        var text = (TextElement)session.AddElement(new TextElement { Text = "Aynı", X = 10, Y = 10 }).Model;
        int steps = session.History.UndoEntries.Count;
        Assert.False(InlineTextEditing.Commit(session, text, "Aynı"));
        Assert.Equal(steps, session.History.UndoEntries.Count);

        var locked = (TextElement)session.AddElement(new TextElement { Text = "Kilitli", X = 10, Y = 30, Locked = true }).Model;
        Assert.False(InlineTextEditing.CanEdit(locked));
        Assert.False(InlineTextEditing.Commit(session, locked, "Değişti"));
        Assert.Equal("Kilitli", locked.Text);

        session.Select(session.GetViewModel(text));
        session.DeleteSelection();
        Assert.False(InlineTextEditing.Commit(session, text, "Silinmiş öğe"));

        Assert.False(InlineTextEditing.CanEdit(new BarcodeElement()));
        Assert.False(InlineTextEditing.CanEdit(new TextElement { Visible = false }));
        Assert.False(InlineTextEditing.CanEdit(null));
    }

    [Fact]
    public void InlineEditorBox_FollowsZoomAndRotation_AndHasMinimumSize()
    {
        double scale = 2 * 96 / 25.4; // %200 yakınlaştırma
        var text = new TextElement { X = 10, Y = 20, Width = 40, Height = 10, FontSize = 12, Rotation = 90 };
        var box = InlineTextEditing.ComputeBox(text, scale, 56);
        Assert.Equal(56 + 10 * scale, box.Left, 6);
        Assert.Equal(56 + 20 * scale, box.Top, 6);
        Assert.Equal(40 * scale, box.Width, 6);
        Assert.Equal(10 * scale, box.Height, 6);
        Assert.Equal(90, box.Angle);
        Assert.Equal(Units.PointsToMm(12) * scale, box.FontSizeDip, 6);

        // Küçük kutu: düzenleyici merkezi koruyarak en küçük ölçüye büyür.
        double actual = 96 / 25.4;
        var small = new TextElement { X = 10, Y = 10, Width = 5, Height = 2, FontSize = 6 };
        var smallBox = InlineTextEditing.ComputeBox(small, actual, 56);
        Assert.Equal(InlineTextEditing.MinWidthDip, smallBox.Width, 6);
        Assert.True(smallBox.Height >= InlineTextEditing.MinHeightDip);
        Assert.Equal(56 + 12.5 * actual, smallBox.Left + smallBox.Width / 2, 6);
        Assert.Equal(56 + 11 * actual, smallBox.Top + smallBox.Height / 2, 6);
    }

    [Fact]
    public void FormatPainter_CopiesStyleNotContent_ToSameKindOnly_InOneUndoStep()
    {
        using var session = EditorTestHelpers.CreateSession();
        var source = new TextElement
        {
            Text = "Kaynak", FontFamily = "Arial", FontSize = 18, Bold = true, Italic = true,
            HorizontalAlignment = TextHorizontalAlignment.Center, Background = FillStyle.Black, TextColor = InkColor.White,
            X = 5, Y = 5, Width = 50, Height = 10,
        };
        var target = new TextElement { Text = "Hedef", FontSize = 9, X = 5, Y = 30, Width = 30, Height = 6, Name = "Ürün adı" };
        var locked = new TextElement { Text = "Kilitli", FontSize = 9, X = 5, Y = 50, Locked = true };
        var barcode = new BarcodeElement { Symbology = BarcodeType.Code128, Content = "AKEL1", X = 50, Y = 50, ShowText = true };
        foreach (var element in new LabelElement[] { source, target, locked, barcode }) session.AddElement(element, select: false);

        var style = ElementStyle.Capture(source);
        Assert.Equal("Metin", style.KindName);
        Assert.DoesNotContain(nameof(TextElement.Text), style.PropertyNames);
        Assert.False(style.CanApplyTo(locked));
        Assert.False(style.CanApplyTo(barcode));

        int applied;
        using (session.BeginTransaction("Biçim yapıştırıldı"))
            applied = style.ApplyTo([target, locked, barcode]);

        Assert.Equal(1, applied);
        Assert.Equal(18, target.FontSize);
        Assert.True(target.Bold && target.Italic);
        Assert.Equal(TextHorizontalAlignment.Center, target.HorizontalAlignment);
        Assert.Equal(FillStyle.Black, target.Background);
        Assert.Equal(InkColor.White, target.TextColor);
        Assert.Equal("Hedef", target.Text);
        Assert.Equal("Ürün adı", target.Name);
        Assert.Equal((5.0, 30.0, 30.0, 6.0), (target.X, target.Y, target.Width, target.Height));
        Assert.Equal(9, locked.FontSize);
        Assert.True(barcode.ShowText);
        Assert.Equal("Biçim yapıştırıldı", session.History.UndoDescription);

        session.Undo();
        var restored = Assert.IsType<TextElement>(session.Document.FindElement(target.Id));
        Assert.Equal(9, restored.FontSize);
        Assert.False(restored.Bold);
    }

    [Fact]
    public void UndoHistory_HasReadableDescriptions_AndUndoStepsGoesBackToChosenPoint()
    {
        using var session = EditorTestHelpers.CreateSession();
        var vm = session.AddElement(new TextElement { Text = "A", X = 10, Y = 10, Name = "Başlık" });
        string id = vm.Model.Id;
        session.MoveSelection(5, 0);
        ((TextElement)vm.Model).FontSize = 20;

        var history = session.GetUndoHistory();
        Assert.Equal(["Başlık: yazı boyutu", "Başlık taşındı", "Metin eklendi"], history.Take(3).Select(h => h.Description));
        Assert.Equal([1, 2, 3], history.Take(3).Select(h => h.Steps));
        Assert.All(history, h => Assert.Equal(DateTimeKind.Local, h.Time.Kind));

        Assert.Equal(2, session.UndoSteps(history[1].Steps));
        var element = Assert.IsType<TextElement>(session.Document.FindElement(id));
        Assert.Equal(10, element.X, 3);
        Assert.NotEqual(20, element.FontSize);
        Assert.True(session.CanRedo);
        Assert.Equal("Metin eklendi", session.History.UndoDescription);
    }

    [Fact]
    public void ScaleGroup_ScalesPositionsSizesAndFonts_FromStartState()
    {
        var text = new TextElement { X = 10, Y = 10, Width = 20, Height = 10, FontSize = 10 };
        var box = new ShapeElement { X = 30, Y = 20, Width = 10, Height = 10 };
        var starts = new[] { TransformMath.GroupMemberStart.Of(text), TransformMath.GroupMemberStart.Of(box) };
        var from = new RectMm(10, 10, 30, 20);

        // Sürükleme sırasında ara kareler: sonuç yalnız son hedefe bağlıdır (hata birikmez).
        TransformMath.ScaleGroup(starts, from, new RectMm(10, 10, 45, 30));
        TransformMath.ScaleGroup(starts, from, new RectMm(10, 10, 60, 40));

        Assert.Equal((10.0, 10.0, 40.0, 20.0), (text.X, text.Y, text.Width, text.Height));
        Assert.Equal(20, text.FontSize, 2);
        Assert.Equal((50.0, 30.0, 20.0, 20.0), (box.X, box.Y, box.Width, box.Height));

        // Serbest (orantısız): yazı küçük katsayıyla ölçeklenir; 90° döndürülmüş öğede eksenler yer değiştirir.
        var rotated = new TextElement { X = 0, Y = 0, Width = 20, Height = 4, Rotation = 90, FontSize = 8 };
        TransformMath.ScaleGroup([TransformMath.GroupMemberStart.Of(rotated)], new RectMm(0, 0, 20, 20), new RectMm(0, 0, 40, 20));
        Assert.Equal((10.0, -2.0, 20.0, 8.0), (rotated.X, rotated.Y, rotated.Width, rotated.Height));
        Assert.Equal(8, rotated.FontSize, 2);
    }

    [Fact]
    public void GroupResize_InTransaction_IsOneUndoStep()
    {
        using var session = EditorTestHelpers.CreateSession();
        var a = session.AddElement(new TextElement { Text = "A", X = 10, Y = 10, Width = 20, Height = 10, FontSize = 10 });
        var b = session.AddElement(new ShapeElement { X = 30, Y = 20, Width = 10, Height = 10 });
        session.SelectMany([a, b]);
        Assert.True(session.GroupSelection());
        var starts = session.SelectedModels.Select(TransformMath.GroupMemberStart.Of).ToList();
        int steps = session.History.UndoEntries.Count;

        using (session.BeginTransaction("Grup boyutlandırıldı"))
        {
            TransformMath.ScaleGroup(starts, new RectMm(10, 10, 30, 20), new RectMm(10, 10, 45, 30));
            TransformMath.ScaleGroup(starts, new RectMm(10, 10, 30, 20), new RectMm(10, 10, 60, 40));
        }

        Assert.Equal(steps + 1, session.History.UndoEntries.Count);
        Assert.Equal("Grup boyutlandırıldı", session.History.UndoDescription);
        session.Undo();
        var restored = Assert.IsType<TextElement>(session.Document.FindElement(a.Model.Id));
        Assert.Equal((20.0, 10.0, 10.0), (restored.Width, restored.Height, restored.FontSize));
    }

    [Fact]
    public void DuplicateAndPaste_GiveCopiesTheirOwnGroup()
    {
        using var session = EditorTestHelpers.CreateSession();
        var a = session.AddElement(new TextElement { Text = "A", X = 10, Y = 10 });
        var b = session.AddElement(new TextElement { Text = "B", X = 10, Y = 30 });
        session.SelectMany([a, b]);
        Assert.True(session.GroupSelection());
        string original = a.Model.GroupId!;

        session.DuplicateSelection();
        var copies = session.SelectedModels;
        Assert.Equal(2, copies.Count);
        Assert.Equal("2 öğe çoğaltıldı", session.History.UndoDescription);
        string duplicateGroup = Assert.Single(copies.Select(c => c.GroupId).Distinct())!;
        Assert.NotEqual(original, duplicateGroup);

        // Asıl gruba tıklamak yalnız asıl iki öğeyi seçer (kopyalar asıl gruba katılmaz).
        session.Select(a);
        Assert.Equal(2, session.SelectedElements.Count);
        Assert.All(session.SelectedModels, m => Assert.Equal(original, m.GroupId));

        string payload = session.BuildClipboardPayload()!;
        Assert.True(session.PastePayload(payload, 2));
        Assert.Equal("Yapıştırıldı", session.History.UndoDescription);
        string pastedGroup = Assert.Single(session.SelectedModels.Select(m => m.GroupId).Distinct())!;
        Assert.NotEqual(original, pastedGroup);
        Assert.NotEqual(duplicateGroup, pastedGroup);
        Assert.Equal(6, session.Document.Elements.Count);
    }
}

/// <summary>Tasarım kolaylıkları (görünüm modeli): hazır simge ekleme, yeni metinde yerinde düzenleme, biçim boyacısı, geçmişe dönme.</summary>
[Collection("Yapay zekâ masaüstü")]
public sealed class DesignConvenienceViewModelTests : IDisposable
{
    private readonly MainViewModel _main;

    public DesignConvenienceViewModelTests()
    {
        string folder = Path.Combine(TestDataRoot.Root, "tasarim-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        _main = new MainViewModel(new AppServices
        {
            Dialogs = new NullDialogs(),
            Clipboard = new NullClipboard(),
            Dispatcher = new ImmediateDispatcher(),
            ImageFactory = new FakeImageFactory(),
            SettingsStore = new JsonSettingsStore<AppSettings>(Path.Combine(folder, "ayarlar.json")),
            RecoveryFolder = Path.Combine(folder, "Kurtarma"),
        });
    }

    public void Dispose() => _main.Dispose();

    [Fact]
    public void InsertSymbol_AddsSharpSquareImage_WithEmbeddedSvg_AsOneUndoStep()
    {
        var session = _main.Session;
        int steps = session.History.UndoEntries.Count;
        _main.InsertSymbolCommand.Execute("sembol:kirilacak");

        var image = Assert.IsType<ImageElement>(Assert.Single(session.SelectedElements).Model);
        Assert.Equal(ImageConversionMethod.Logo, image.Method);
        Assert.Equal("Kırılacak eşya", image.Name);
        Assert.Equal(image.Width, image.Height);
        Assert.True(session.Assets.TryGet(image.AssetId, out var asset));
        Assert.Equal("kirilacak.svg", asset.FileName);
        Assert.Equal(steps + 1, session.History.UndoEntries.Count);
        Assert.Equal("Görsel eklendi", session.History.UndoDescription);

        // Aynı simge ikinci kez: görsel varlığı paylaşılır; bilinmeyen anahtar hiçbir şey eklemez.
        _main.InsertSymbolCommand.Execute("kirilacak");
        Assert.Equal(image.AssetId, Assert.IsType<ImageElement>(Assert.Single(session.SelectedElements).Model).AssetId);
        int count = session.Document.Elements.Count;
        _main.InsertSymbolCommand.Execute("yesil-nokta");
        Assert.Equal(count, session.Document.Elements.Count);
    }

    [Fact]
    public void SymbolPreviews_AreRenderedForThePanel()
    {
        var symbols = _main.Symbols;
        Assert.Equal(11, symbols.Count);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (symbols.Any(s => s.PreviewPng is null) && DateTime.UtcNow < deadline) Thread.Sleep(50);
        Assert.All(symbols, s => Assert.NotNull(s.PreviewPng));
        Assert.All(symbols, s => Assert.Equal((byte)0x89, s.PreviewPng![0]));
    }

    [Fact]
    public void NewText_StartsInlineEditing_AndCommitIsOneUndoStep()
    {
        ElementViewModel? requested = null;
        _main.InlineTextEditRequested += (_, vm) => requested = vm;
        _main.AddTextStyleCommand.Execute("Govde");

        Assert.NotNull(requested);
        Assert.Same(Assert.Single(_main.Session.SelectedElements), requested);
        Assert.True(_main.CommitInlineText(requested, "Melamin kase"));
        Assert.Equal("Melamin kase", ((TextElement)requested.Model).Text);
        Assert.Equal("Metin düzenlendi", _main.Session.History.UndoDescription);

        requested.Model.Locked = true;
        Assert.False(_main.RequestInlineTextEdit(requested));
    }

    [Fact]
    public void CopyAndPasteStyle_AppliesToSelectedSameKindElements_AsOneUndoStep()
    {
        var session = _main.Session;
        var source = session.AddElement(new TextElement { Text = "Kaynak", FontSize = 16, Bold = true, X = 5, Y = 5 });
        Assert.True(_main.CopyStyleCommand.CanExecute(null));
        _main.CopyStyleCommand.Execute(null);

        var first = session.AddElement(new TextElement { Text = "Bir", FontSize = 8, X = 5, Y = 20 }, select: false);
        var second = session.AddElement(new TextElement { Text = "İki", FontSize = 9, X = 5, Y = 30 }, select: false);
        var shape = session.AddElement(new ShapeElement { X = 50, Y = 30 }, select: false);
        session.SelectMany([first, second, shape]);
        int steps = session.History.UndoEntries.Count;
        Assert.True(_main.PasteStyleCommand.CanExecute(null));
        _main.PasteStyleCommand.Execute(null);

        Assert.All(new[] { first, second }, vm => Assert.Equal(16, ((TextElement)vm.Model).FontSize));
        Assert.All(new[] { first, second }, vm => Assert.True(((TextElement)vm.Model).Bold));
        Assert.Equal(steps + 1, session.History.UndoEntries.Count);
        Assert.Equal("Biçim yapıştırıldı", session.History.UndoDescription);
        Assert.Equal("Kaynak", ((TextElement)source.Model).Text);
    }

    [Fact]
    public void UndoTo_GoesBackToTheChosenHistoryEntry()
    {
        var session = _main.Session;
        var vm = session.AddElement(new TextElement { Text = "A", X = 10, Y = 10 });
        string id = vm.Model.Id;
        session.MoveSelection(3, 0);
        session.MoveSelection(0, 3);

        var history = _main.GetUndoHistory();
        _main.UndoToCommand.Execute(history[2]);

        Assert.Null(session.Document.FindElement(id));
        Assert.True(session.CanRedo);
    }

    private sealed class NullDialogs : IDialogService
    {
        public string? ShowOpenFileDialog(string title, string filter, string? initialDirectory = null) => null;
        public string? ShowSaveFileDialog(string title, string filter, string defaultFileName, string? initialDirectory = null) => null;
        public void ShowMessage(string title, string message, MessageKind kind = MessageKind.Info) { }
        public bool Confirm(string title, string message, string confirmText = "Evet", string cancelText = "Hayır") => true;
        public SaveChangesChoice AskSaveChanges(string documentName) => SaveChangesChoice.Discard;
        public string? Prompt(string title, string message, string defaultValue = "") => null;
        public bool ShowDialog(object viewModel) => false;
    }

    private sealed class NullClipboard : IClipboardService
    {
        public void SetElements(string payloadJson, string plainText) { }
        public string? GetElements() => null;
        public bool ContainsElements() => false;
        public byte[]? GetImagePng() => null;
        public string? GetText() => null;
    }
}
