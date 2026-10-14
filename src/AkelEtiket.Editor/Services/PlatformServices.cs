namespace AkelEtiket.Editor.Services;

public enum MessageKind
{
    Info,
    Warning,
    Error,
}

public enum SaveChangesChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>Arayüz katmanının sağladığı diyaloglar (WPF'te uygulanır; testlerde sahte uygulanır).</summary>
public interface IDialogService
{
    string? ShowOpenFileDialog(string title, string filter, string? initialDirectory = null);
    string? ShowSaveFileDialog(string title, string filter, string defaultFileName, string? initialDirectory = null);
    void ShowMessage(string title, string message, MessageKind kind = MessageKind.Info);
    bool Confirm(string title, string message, string confirmText = "Evet", string cancelText = "Hayır");
    SaveChangesChoice AskSaveChanges(string documentName);
    string? Prompt(string title, string message, string defaultValue = "");

    /// <summary>Görünüm modeline karşılık gelen pencereyi kalıcı (modal) açar. true = onaylandı.</summary>
    bool ShowDialog(object viewModel);
}

/// <summary>Pano erişimi.</summary>
public interface IClipboardService
{
    void SetElements(string payloadJson, string plainText);
    string? GetElements();
    bool ContainsElements();
    byte[]? GetImagePng();
    string? GetText();
}

/// <summary>Arayüz iş parçacığına iş gönderme.</summary>
public interface IUiDispatcher
{
    bool IsUiThread { get; }
    void Post(Action action);
}

/// <summary>Render edilmiş BGRA piksellerinden platform görüntüsü (WPF BitmapSource) üretir; her iş parçacığından çağrılabilir.</summary>
public interface ILayerImageFactory
{
    object Create(byte[] bgra, int width, int height);
}

/// <summary>Testler için eşzamanlı dağıtıcı.</summary>
public sealed class ImmediateDispatcher : IUiDispatcher
{
    public bool IsUiThread => true;
    public void Post(Action action) => action();
}
