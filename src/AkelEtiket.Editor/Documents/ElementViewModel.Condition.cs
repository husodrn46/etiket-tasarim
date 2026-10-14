using AkelEtiket.Core.Model;
using AkelEtiket.Rendering;

namespace AkelEtiket.Editor.Documents;

/// <summary>Özellikler panelindeki "Koşullu gösterme" bölümü (model: <see cref="LabelElement.VisibleWhen"/>).</summary>
public sealed partial class ElementViewModel
{
    // Alan seçilmeden önce kutu işaretlenebilsin diye taslak değerler tutulur; geçerli koşul oluşunca modele yazılır.
    private bool _conditionDraftEnabled;
    private string _draftField = "";
    private ConditionOperator _draftOperator = ConditionOperator.NotEmpty;
    private string _draftValue = "";
    private bool _applyingCondition;

    /// <summary>"Yalnızca şu koşulda bas" işaretli mi?</summary>
    public bool ConditionEnabled
    {
        get => Model.VisibleWhen is not null || _conditionDraftEnabled;
        set
        {
            if (value == ConditionEnabled) return;
            SyncDraftFromModel();
            _conditionDraftEnabled = value;
            ApplyCondition();
        }
    }

    /// <summary>Koşul alanı (veri sütunu ya da yerleşik alan).</summary>
    public string ConditionField
    {
        get => Model.VisibleWhen?.Field ?? _draftField;
        set
        {
            SyncDraftFromModel();
            _draftField = VisibilityCondition.CleanField(value);
            ApplyCondition();
        }
    }

    public ConditionOperator ConditionOperator
    {
        get => Model.VisibleWhen?.Operator ?? _draftOperator;
        set
        {
            SyncDraftFromModel();
            _draftOperator = value;
            ApplyCondition();
        }
    }

    public string ConditionValue
    {
        get => Model.VisibleWhen?.Value ?? _draftValue;
        set
        {
            SyncDraftFromModel();
            _draftValue = value ?? "";
            ApplyCondition();
        }
    }

    /// <summary>Eşittir/eşit değildir/içerir için değer kutusu gösterilir.</summary>
    public bool ConditionNeedsValue => ConditionOperator is ConditionOperator.IsEqual or ConditionOperator.NotEqual or ConditionOperator.Contains;

    /// <summary>Koşulun durumu (önizleme satırına göre).</summary>
    public string? ConditionStatusText => Model.VisibleWhen is not { } condition
        ? ConditionEnabled ? "Koşul için bir alan seçin." : null
        : RenderResult?.HiddenByCondition == true
            ? $"Bu önizleme satırında koşul sağlanmıyor: öğe basılmaz (yalnızca {condition.Describe()} basılır)."
            : $"Yalnızca {condition.Describe()} basılır.";

    /// <summary>Modeldeki koşulu taslağa alır (dosyadan açılan ya da geri alınan koşul düzenlenirken).</summary>
    private void SyncDraftFromModel()
    {
        if (Model.VisibleWhen is not { } current) return;
        _conditionDraftEnabled = true;
        _draftField = current.Field;
        _draftOperator = current.Operator;
        _draftValue = current.Value ?? "";
    }

    private void ApplyCondition()
    {
        _applyingCondition = true;
        try
        {
            bool needsValue = _draftOperator is ConditionOperator.IsEqual or ConditionOperator.NotEqual or ConditionOperator.Contains;
            Model.VisibleWhen = !_conditionDraftEnabled || _draftField.Length == 0
                ? null
                : new VisibilityCondition(_draftField, _draftOperator, needsValue ? _draftValue : null);
        }
        finally
        {
            _applyingCondition = false;
        }
        RaiseCondition();
    }

    /// <summary>Modelin koşulu değişti (panelden, geri al/ileri al ya da yapay zekâ ile).</summary>
    internal void OnConditionChangedInModel()
    {
        if (!_applyingCondition)
        {
            // Dışarıdan gelen değişiklik: kutu modeli izler.
            _conditionDraftEnabled = Model.VisibleWhen is not null;
            SyncDraftFromModel();
        }
        RaiseCondition();
    }

    private void RaiseCondition()
    {
        OnPropertyChanged(nameof(ConditionEnabled));
        OnPropertyChanged(nameof(ConditionField));
        OnPropertyChanged(nameof(ConditionOperator));
        OnPropertyChanged(nameof(ConditionValue));
        OnPropertyChanged(nameof(ConditionNeedsValue));
        OnPropertyChanged(nameof(ConditionStatusText));
    }

    partial void OnRenderResultChanged(ElementRenderResult? value) => OnPropertyChanged(nameof(ConditionStatusText));
}
