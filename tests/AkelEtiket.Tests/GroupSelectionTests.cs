using AkelEtiket.Core.Geometry;
using AkelEtiket.Core.Model;

namespace AkelEtiket.Tests;

/// <summary>Gruplar editörde: birlikte seçim, birlikte taşıma/hizalama, tek geri alma adımı.</summary>
public sealed class GroupSelectionTests
{
    [Fact]
    public void ClickingGroupMember_SelectsWholeGroup_AndUndoRemovesGroup()
    {
        using var session = EditorTestHelpers.CreateSession();
        var a = session.AddElement(new TextElement { Text = "A", X = 10, Y = 10 });
        var b = session.AddElement(new TextElement { Text = "B", X = 10, Y = 30 });
        var c = session.AddElement(new TextElement { Text = "C", X = 60, Y = 30 });
        session.SelectMany([a, b]);
        Assert.True(session.GroupSelection());
        Assert.NotNull(a.Model.GroupId);
        Assert.Equal(a.Model.GroupId, b.Model.GroupId);
        Assert.Equal("Gruplandı", session.History.UndoDescription);

        session.Select(c);
        session.Select(b);
        Assert.Equal(2, session.SelectedElements.Count);
        Assert.Same(b, session.PrimarySelection);
        Assert.True(session.SelectionHasGroup);

        session.MoveSelection(5, 0);
        Assert.Equal(15, a.Model.X, 3);
        Assert.Equal(15, b.Model.X, 3);
        Assert.Equal(60, c.Model.X, 3);

        Assert.True(session.UngroupSelection());
        Assert.Null(a.Model.GroupId);
        session.Undo();
        // Geri almada öğeler yeniden oluşturulur: güncel nesneyi kimlikle al.
        Assert.NotNull(session.Document.FindElement(a.Model.Id)!.GroupId);
    }

    [Fact]
    public void AlignTreatsGroupAsUnit()
    {
        var label = new LabelSetup { WidthMm = 100, HeightMm = 100 };
        var a = new TextElement { X = 10, Y = 10, Width = 20, Height = 5, GroupId = "g1" };
        var b = new TextElement { X = 40, Y = 10, Width = 20, Height = 5, GroupId = "g1" };
        var c = new TextElement { X = 5, Y = 30, Width = 10, Height = 5 };
        TransformMath.Align([a, b, c], AlignMode.Right, label, AlignReference.Selection);
        // Grup sağ kenarı (60) referanstır; grubun içi bozulmaz, c sağa yaslanır.
        Assert.Equal(10, a.X, 3);
        Assert.Equal(40, b.X, 3);
        Assert.Equal(50, c.X, 3);
    }
}
