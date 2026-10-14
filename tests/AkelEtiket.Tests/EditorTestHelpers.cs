using AkelEtiket.Editor.Documents;
using AkelEtiket.Editor.Services;
using AkelEtiket.ProjectManager;

namespace AkelEtiket.Tests;

/// <summary>Testlerde WPF yerine kullanılan basit görüntü fabrikası.</summary>
internal sealed class FakeImageFactory : ILayerImageFactory
{
    public object Create(byte[] bgra, int width, int height) => (width, height, bgra.Length);
}

internal static class EditorTestHelpers
{
    public static DocumentSession CreateSession(LabelProject? project = null) =>
        new(project ?? LabelProject.CreateBlank(), new EditorContext(new ImmediateDispatcher(), new FakeImageFactory()), 8.0);

    /// <summary>Arka plan render'larının bitmesini bekler (ImmediateDispatcher sonucu hemen uygular).</summary>
    public static void WaitForRenders(DocumentSession session, int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (session.Elements.All(e => !e.IsRendering && e.RenderResult is not null)) return;
            Thread.Sleep(10);
        }
        throw new TimeoutException("Render tamamlanmadı.");
    }

    public static string FindRepoFile(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine([dir.FullName, .. relative]);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(string.Join("/", relative));
    }
}
