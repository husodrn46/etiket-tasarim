using AkelEtiket.Core.Geometry;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Documents;
using AkelEtiket.Editor.Services;
using AkelEtiket.ProjectManager;

namespace AkelEtiket.Tests;

public class DocumentSessionTests
{
    [Fact]
    public void AddMoveUndoRedo_RestoresExactState()
    {
        using var session = EditorTestHelpers.CreateSession();
        var text = new TextElement { X = 10, Y = 10, Text = "Melamin Tepsi" };
        session.AddElement(text);
        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);

        session.MoveSelection(5, 2.5);
        var moved = session.Document.Elements.Single();
        Assert.Equal((15.0, 12.5), (moved.X, moved.Y));

        session.Undo();
        var afterUndo = session.Document.Elements.Single();
        Assert.Equal((10.0, 10.0), (afterUndo.X, afterUndo.Y));
        Assert.Equal(text.Id, afterUndo.Id);
        Assert.True(session.SelectedElements.Single().Model == afterUndo); // seçim korunur

        session.Redo();
        Assert.Equal(15, session.Document.Elements.Single().X);

        session.Undo();
        session.Undo();
        Assert.Empty(session.Document.Elements);
        Assert.False(session.IsDirty); // ilk kayıtlı duruma dönüldü
    }

    [Fact]
    public void Typing_IsCoalescedIntoSingleUndoStep()
    {
        using var session = EditorTestHelpers.CreateSession();
        var text = new TextElement { Text = "" };
        session.AddElement(text);
        int before = session.History.UndoCount;
        foreach (var partial in new[] { "A", "AK", "AKE", "AKEL" }) text.Text = partial;
        Assert.Equal(before + 1, session.History.UndoCount);
        session.Undo();
        Assert.Equal("", ((TextElement)session.Document.Elements[0]).Text);
    }

    [Fact]
    public void Transaction_GroupsManyChanges()
    {
        using var session = EditorTestHelpers.CreateSession();
        session.AddElement(new ShapeElement { X = 0 });
        session.AddElement(new ShapeElement { X = 50 }, select: false);
        session.SelectAll();
        int before = session.History.UndoCount;
        using (session.BeginTransaction())
        {
            foreach (var e in session.Document.Elements) e.Y += 10;
            foreach (var e in session.Document.Elements) e.Width += 5;
        }
        Assert.Equal(before + 1, session.History.UndoCount);
        session.Undo();
        Assert.All(session.Document.Elements, e => Assert.Equal(0, e.Y));
    }

    [Fact]
    public void ZOrder_Duplicate_Delete_LockedProtection()
    {
        using var session = EditorTestHelpers.CreateSession();
        var a = new ShapeElement { Name = "A" };
        var b = new ShapeElement { Name = "B" };
        var c = new ShapeElement { Name = "C" };
        session.AddElement(a);
        session.AddElement(b);
        session.AddElement(c);
        session.Select(session.GetViewModel(a));
        session.ChangeZOrder(ZOrderChange.BringToFront);
        Assert.Equal(["B", "C", "A"], session.Document.Elements.Select(e => e.Name));
        session.ChangeZOrder(ZOrderChange.SendBackward);
        Assert.Equal(["B", "A", "C"], session.Document.Elements.Select(e => e.Name));
        Assert.Equal(["B", "A", "C"], session.Elements.Select(e => e.Model.Name)); // görünüm modelleri senkron

        session.DuplicateSelection();
        Assert.Equal(4, session.Document.Elements.Count);
        Assert.NotEqual(a.Id, session.PrimarySelection!.Model.Id);

        session.Select(session.GetViewModel(c));
        c.Locked = true;
        session.DeleteSelection();
        Assert.Contains(c, session.Document.Elements); // kilitli öğe silinmez
    }

    [Fact]
    public void ClipboardPayload_CarriesImageAssetsToAnotherDocument()
    {
        using var source = EditorTestHelpers.CreateSession();
        var asset = source.Assets.Add([1, 2, 3, 4], "logo.png");
        var image = new ImageElement { AssetId = asset.Id, X = 5, Y = 5 };
        source.AddElement(image);
        string payload = source.BuildClipboardPayload()!;

        using var target = EditorTestHelpers.CreateSession();
        Assert.True(target.PastePayload(payload, 3));
        var pasted = Assert.IsType<ImageElement>(target.Document.Elements.Single());
        Assert.NotEqual(image.Id, pasted.Id);
        Assert.Equal(8, pasted.X);
        Assert.True(target.Assets.Contains(asset.Id));
        Assert.False(target.PastePayload("geçersiz json", 0));
    }

    [Fact]
    public void Render_ProducesLayersAndMovingDoesNotRerender()
    {
        using var session = EditorTestHelpers.CreateSession();
        var barcode = new BarcodeElement { X = 10, Y = 10, Width = 50, Height = 25, Symbology = BarcodeType.Ean13, Content = "8691234567890" };
        session.AddElement(barcode);
        EditorTestHelpers.WaitForRenders(session);
        var vm = session.Elements.Single();
        Assert.NotNull(vm.LayerImage);
        Assert.False(vm.HasErrors);
        var firstResult = vm.RenderResult;

        barcode.X = 30;
        EditorTestHelpers.WaitForRenders(session);
        Assert.Same(firstResult, vm.RenderResult); // taşıma yeniden çizim gerektirmez
        Assert.Equal(30, vm.LayerLeftMm, 3);

        session.VerifyBarcode(vm);
        Assert.True(vm.VerificationSuccess, vm.VerificationMessage);
    }

    [Fact]
    public void InvalidBarcode_ShowsErrorOnViewModel()
    {
        using var session = EditorTestHelpers.CreateSession();
        session.AddElement(new BarcodeElement { Width = 50, Height = 25, Symbology = BarcodeType.Ean13, Content = "8691234567891" });
        EditorTestHelpers.WaitForRenders(session);
        var vm = session.Elements.Single();
        Assert.True(vm.HasErrors);
        Assert.Contains("kontrol basamağı", vm.StatusText);
    }

    [Fact]
    public void HitTest_ReturnsTopmostVisible()
    {
        using var session = EditorTestHelpers.CreateSession();
        var bottom = new ShapeElement { X = 0, Y = 0, Width = 50, Height = 50 };
        var top = new ShapeElement { X = 10, Y = 10, Width = 20, Height = 20 };
        session.AddElement(bottom);
        session.AddElement(top);
        Assert.Same(top, session.HitTest(15, 15)!.Model);
        top.Visible = false;
        Assert.Same(bottom, session.HitTest(15, 15)!.Model);
        Assert.Null(session.HitTest(80, 80));
    }

    [Fact]
    public void LabelSetupChange_IsUndoable()
    {
        using var session = EditorTestHelpers.CreateSession();
        using (session.BeginTransaction())
        {
            session.Document.Label.WidthMm = 58;
            session.Document.Label.HeightMm = 40;
        }
        session.Undo();
        Assert.Equal(100, session.Document.Label.WidthMm);
        Assert.Equal(100, session.Document.Label.HeightMm);
    }

    [Fact]
    public void MarkSaved_ClearsDirty()
    {
        using var session = EditorTestHelpers.CreateSession();
        session.AddElement(new TextElement());
        Assert.True(session.IsDirty);
        session.MarkSaved();
        Assert.False(session.IsDirty);
        session.Undo();
        Assert.True(session.IsDirty);
    }
}

public class TransformMathTests
{
    [Fact]
    public void Resize_BottomRight_Unrotated_KeepsTopLeft()
    {
        var r = TransformMath.Resize((10, 10, 20, 10, 0), ResizeHandle.BottomRight, 5, 3, keepAspect: false);
        Assert.Equal((10.0, 10.0, 25.0, 13.0), (Math.Round(r.X, 6), Math.Round(r.Y, 6), Math.Round(r.Width, 6), Math.Round(r.Height, 6)));
    }

    [Fact]
    public void Resize_TopLeft_KeepsBottomRight()
    {
        var r = TransformMath.Resize((10, 10, 20, 10, 0), ResizeHandle.TopLeft, -4, -2, keepAspect: false);
        Assert.Equal(6, r.X, 6);
        Assert.Equal(8, r.Y, 6);
        Assert.Equal(24, r.Width, 6);
        Assert.Equal(12, r.Height, 6);
    }

    [Fact]
    public void Resize_Rotated90_AnchorCornerStaysFixed()
    {
        var start = (X: 0.0, Y: 0.0, Width: 40.0, Height: 10.0, Rotation: 90.0);
        var anchorBefore = AkelEtiket.Core.Geometry.ElementGeometry.GetCorners(start.X, start.Y, start.Width, start.Height, start.Rotation)[0]; // sol üst (yerel)
        // Sağ alt tutamacı ekranda aşağı çek: 90° döndürülmüşte yerel genişlik artar.
        var r = TransformMath.Resize(start, ResizeHandle.BottomRight, 0, 10, keepAspect: false);
        Assert.Equal(50, r.Width, 6);
        Assert.Equal(10, r.Height, 6);
        var anchorAfter = AkelEtiket.Core.Geometry.ElementGeometry.GetCorners(r.X, r.Y, r.Width, r.Height, 90)[0];
        Assert.Equal(anchorBefore.X, anchorAfter.X, 6);
        Assert.Equal(anchorBefore.Y, anchorAfter.Y, 6);
    }

    [Fact]
    public void Resize_KeepAspect_AndMinimum()
    {
        var r = TransformMath.Resize((0, 0, 20, 10, 0), ResizeHandle.BottomRight, 20, 0, keepAspect: true);
        Assert.Equal(40, r.Width, 6);
        Assert.Equal(20, r.Height, 6);
        var tiny = TransformMath.Resize((0, 0, 20, 10, 0), ResizeHandle.Right, -100, 0, keepAspect: false);
        Assert.Equal(TransformMath.MinSizeMm, tiny.Width);
    }

    [Fact]
    public void Rotation_SnapsTo15Degrees()
    {
        Assert.Equal(90, TransformMath.RotationFromPointer(0, 0, 10, 0.5, snap: true));
        Assert.Equal(0, TransformMath.RotationFromPointer(0, 0, 0, -10, snap: false));
    }

    [Fact]
    public void Align_SingleElementToLabel_AndDistribute()
    {
        var label = new LabelSetup { WidthMm = 100, HeightMm = 100 };
        var e = new ShapeElement { X = 7, Y = 3, Width = 20, Height = 10 };
        TransformMath.Align([e], AlignMode.CenterHorizontal, label);
        Assert.Equal(40, e.X, 6);
        TransformMath.Align([e], AlignMode.Bottom, label);
        Assert.Equal(90, e.Y, 6);

        var a = new ShapeElement { X = 0, Width = 10 };
        var b = new ShapeElement { X = 15, Width = 10 };
        var c = new ShapeElement { X = 90, Width = 10 };
        TransformMath.Align([a, b, c], AlignMode.DistributeHorizontal, label);
        Assert.Equal(45, b.X, 6); // eşit aralık: 0–10, 45–55, 90–100
    }
}

public class SnapEngineTests
{
    [Fact]
    public void SnapsToLabelCenterAndGuides_ElseGrid()
    {
        var label = new LabelSetup { WidthMm = 100, HeightMm = 100 };
        var original = new AkelEtiket.Core.Geometry.RectMm(10, 10, 20, 10);
        var options = new SnapEngine.Options { ThresholdMm = 1, GridSizeMm = 1 };
        // Merkez 40,3 + 9,4 = 49,7 → etiket merkezine (50) yakalanmalı
        var result = SnapEngine.SnapMove(original, 29.7, 0.4, label, [], [], options);
        Assert.Equal(30, result.DeltaX, 6);
        Assert.Contains(result.Lines, l => l.Orientation == GuideOrientation.Vertical && l.Position == 50);
        Assert.Equal(0, result.DeltaY, 6); // ızgaraya (10) yuvarlandı

        var guide = new GuideLine { Orientation = GuideOrientation.Horizontal, Position = 33 };
        var withGuide = SnapEngine.SnapMove(original, 0, 22.6, label, [guide], [], options);
        Assert.Equal(23, withGuide.DeltaY, 6); // üst kenar 33'e
    }
}

public class UndoManagerTests
{
    [Fact]
    public void Capacity_IsBounded_AndRedoClearedOnNewChange()
    {
        var undo = new UndoManager(capacity: 10);
        for (int i = 0; i < 25; i++) undo.Record("s" + i);
        Assert.Equal(10, undo.UndoCount);
        string? previous = undo.Undo("current");
        Assert.Equal("s24", previous);
        Assert.True(undo.CanRedo);
        undo.Record("new");
        Assert.False(undo.CanRedo);
    }
}
