namespace AkelEtiket.Editor.Services;

/// <summary>Local appearance and new-document preferences. Never applied to existing label contents.</summary>
public sealed class PersonalizationSettings
{
    public bool OpenDocumentsInTabs { get; set; } = true;
    public bool EnableAnimations { get; set; } = true;
    public string Accent { get; set; } = "red";
    public double UiScale { get; set; } = 1;
    public string DefaultFontFamily { get; set; } = "Arial";
    public string? StartupTemplatePath { get; set; }
    public bool RememberPanelLayout { get; set; } = true;
    public string ActivePanel { get; set; } = "Text";
    public bool IsPanelOpen { get; set; } = true;
    public bool IsAiPanelOpen { get; set; }
    public void Normalize()
    {
        if (Accent is not ("red" or "blue" or "teal" or "purple")) Accent = "red";
        if (!double.IsFinite(UiScale) || UiScale is < 0.9 or > 1.25) UiScale = 1;
        DefaultFontFamily = string.IsNullOrWhiteSpace(DefaultFontFamily) ? "Arial" : DefaultFontFamily.Trim()[..Math.Min(DefaultFontFamily.Trim().Length, 100)];
        if (!Enum.TryParse<ViewModels.LeftPanel>(ActivePanel, out var panel) || !Enum.IsDefined(panel)) ActivePanel = "Text";
        if (string.IsNullOrWhiteSpace(StartupTemplatePath)) StartupTemplatePath = null;
    }
}
