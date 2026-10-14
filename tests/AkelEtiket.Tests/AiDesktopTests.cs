using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using AkelEtiket.Assistant.Chat;
using AkelEtiket.Core;
using AkelEtiket.Core.Automation;
using AkelEtiket.Core.Model;
using AkelEtiket.Design.Tools;
using AkelEtiket.Design.Workspace;
using AkelEtiket.Editor.Ai;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.ProjectManager;

namespace AkelEtiket.Tests;

/// <summary>Bu test derlemesi bütün kullanıcı klasörlerini geçici bir köke yönlendirir (gerçek ayarlara dokunulmaz).</summary>
internal static class TestDataRoot
{
    public static readonly string Root = Path.Combine(Path.GetTempPath(), "akel-editor-tests", Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Initialize() => Environment.SetEnvironmentVariable(AppIdentity.DataRootVariable, Root);
}

/// <summary>
/// Masaüstü tümleşimi: dış ajanın açık belgeyi değiştirmesi (doğrudan mod, tek geri alma adımı), öneri kabulü
/// (onaylı mod), kullanıcının sonraki değişikliklerinin korunması, paylaşım kapalıyken erişim ve uygulama içi asistan.
/// </summary>
[Collection("Yapay zekâ masaüstü")]
public sealed class AiDesktopTests : IDisposable
{
    private readonly MainViewModel _main;
    private readonly AiSettings _settings;

    public AiDesktopTests()
    {
        _settings = AiSettingsStore.LoadWithToken();
        _settings.Enabled = false;
        _settings.ChangeMode = AiChangeMode.Approval;
        _settings.ShareOpenDocument = true;
        _settings.Assistant = new AssistantProviderSettings();
        AiSettingsStore.Save(_settings);
        string folder = Path.Combine(TestDataRoot.Root, "main-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        _main = new MainViewModel(new AppServices
        {
            Dialogs = new FakeDialogs(),
            Clipboard = new FakeClipboard(),
            Dispatcher = new ImmediateDispatcher(),
            ImageFactory = new FakeImageFactory(),
            SettingsStore = new JsonSettingsStore<AppSettings>(Path.Combine(folder, "ayarlar.json")),
            RecoveryFolder = Path.Combine(folder, "Kurtarma"),
        });
        _main.Ai.Initialize();
    }

    public void Dispose() => _main.Dispose();

    private void SetMode(AiChangeMode mode, bool share = true)
    {
        _settings.ChangeMode = mode;
        _settings.ShareOpenDocument = share;
        AiSettingsStore.Save(_settings);
        _main.AiSettings.Invalidate();
    }

    private string LiveId => DesignId.Live(_main.Session.SessionNumber).ToString();

    private async Task<ToolResult> Agent(string tool, object args)
    {
        using var doc = JsonDocument.Parse(args is JsonNode node ? node.ToJsonString() : JsonSerializer.Serialize(args));
        return await _main.Ai.Tools.Registry.ExecuteAsync(tool, doc.RootElement.Clone(), new ToolCallContext { ClientName = "Claude (test)" }, CancellationToken.None);
    }

    [Fact]
    public async Task DirectMode_AgentEditAppearsOnCanvas_AsSingleUndoStep()
    {
        SetMode(AiChangeMode.Direct);
        int undoBefore = _main.Session.History.UndoCount;
        var result = await Agent("add_text", new { design_id = LiveId, text = "Yapay zekâ metni", x = 10, y = 10 });
        Assert.False(result.IsError, result.Text);
        Assert.Contains(_main.Session.Document.Elements, e => e is TextElement { Text: "Yapay zekâ metni" });
        Assert.Contains(_main.Session.Elements, vm => vm.Model is TextElement { Text: "Yapay zekâ metni" });
        Assert.Equal(undoBefore + 1, _main.Session.History.UndoCount);
        Assert.Contains("Yapay zekâ", _main.Session.History.UndoDescription);
        Assert.True(_main.Session.IsDirty);

        _main.Session.Undo();
        Assert.DoesNotContain(_main.Session.Document.Elements, e => e is TextElement { Text: "Yapay zekâ metni" });
    }

    [Fact]
    public async Task DirectMode_FailedToolLeavesOpenDocumentUntouched()
    {
        SetMode(AiChangeMode.Direct);
        string before = Core.Serialization.ModelJson.Serialize(_main.Session.Document, indented: false);
        int undoBefore = _main.Session.History.UndoCount;
        var result = await Agent("add_barcode", new { design_id = LiveId, symbology = "ean13", content = "8691234567891", x = 5, y = 5 });
        Assert.True(result.IsError);
        Assert.Equal(before, Core.Serialization.ModelJson.Serialize(_main.Session.Document, indented: false));
        Assert.Equal(undoBefore, _main.Session.History.UndoCount);
    }

    [Fact]
    public async Task ApprovalMode_ProposalAccepted_AppliesAsOneUndoStep_KeepingUserChanges()
    {
        SetMode(AiChangeMode.Approval);
        // Kullanıcının belgesinde iki öğe var.
        var userText = new TextElement { Text = "Kullanıcı", X = 5, Y = 5 };
        var other = new TextElement { Text = "Diğer", X = 5, Y = 30 };
        _main.Session.AddElement(userText);
        _main.Session.AddElement(other);

        // Onaylı modda doğrudan değişiklik reddedilir.
        var denied = await Agent("move_element", new { design_id = LiveId, element_ids = new[] { other.Id }, dx = 3 });
        Assert.Equal(ToolErrorCodes.ApprovalRequired, denied.ErrorCode);

        var draft = (JsonObject)(await Agent("begin_draft", new { design_id = LiveId })).Structured!;
        string draftId = draft["draft_id"]!.GetValue<string>();
        Assert.False((await Agent("move_element", new { design_id = draftId, element_ids = new[] { other.Id }, dx = 10 })).IsError);
        Assert.False((await Agent("add_text", new { design_id = draftId, text = "Yeni", x = 5, y = 60 })).IsError);

        // Kullanıcı bu sırada kendi öğesini taşır.
        userText.X = 20;
        var submitted = await Agent("submit_draft", new { draft_id = draftId, summary = "Diğer metni sağa aldım, yeni metin ekledim." });
        Assert.False(submitted.IsError, submitted.Text);
        var proposal = Assert.Single(_main.Ai.Proposals);
        Assert.Equal(1, _main.Ai.PendingCount);

        int undoBefore = _main.Session.History.UndoCount;
        await _main.Ai.AcceptCommand.ExecuteAsync(proposal);
        Assert.Empty(_main.Ai.Proposals);
        Assert.Equal(15, _main.Session.Document.FindElement(other.Id)!.X, 3);
        Assert.Equal(20, _main.Session.Document.FindElement(userText.Id)!.X, 3);
        Assert.Contains(_main.Session.Document.Elements, e => e is TextElement { Text: "Yeni" });
        Assert.Equal(undoBefore + 1, _main.Session.History.UndoCount);
        Assert.StartsWith("Yapay zekâ önerisi", _main.Session.History.UndoDescription);

        _main.Session.Undo();
        Assert.Equal(5, _main.Session.Document.FindElement(other.Id)!.X, 3);
        Assert.DoesNotContain(_main.Session.Document.Elements, e => e is TextElement { Text: "Yeni" });
    }

    [Fact]
    public async Task RejectedProposal_LeavesDocumentUnchanged()
    {
        SetMode(AiChangeMode.Approval);
        string draftId = ((JsonObject)(await Agent("begin_draft", new { design_id = LiveId })).Structured!)["draft_id"]!.GetValue<string>();
        await Agent("add_text", new { design_id = draftId, text = "İstenmeyen", x = 5, y = 5 });
        await Agent("submit_draft", new { draft_id = draftId, summary = "Deneme" });
        var proposal = Assert.Single(_main.Ai.Proposals);
        _main.Ai.RejectCommand.Execute(proposal);
        Assert.Empty(_main.Ai.Proposals);
        Assert.DoesNotContain(_main.Session.Document.Elements, e => e is TextElement { Text: "İstenmeyen" });
    }

    [Fact]
    public async Task SharingOff_ExternalAgentCannotReadOpenDocument()
    {
        SetMode(AiChangeMode.Approval, share: false);
        var list = (JsonObject)(await Agent("list_designs", new { })).Structured!;
        Assert.DoesNotContain(list["designs"]!.AsArray(), d => d!["kind"]!.GetValue<string>() == "open_document");
        var read = await Agent("get_design", new { design_id = LiveId });
        Assert.Equal(ToolErrorCodes.PermissionDenied, read.ErrorCode);
    }

    [Fact]
    public async Task StaleSessionNumber_IsReportedAfterNewDocument()
    {
        SetMode(AiChangeMode.Direct);
        string oldId = LiveId;
        _main.NewCommand.Execute(null);
        Assert.NotEqual(oldId, LiveId);
        var result = await Agent("add_text", new { design_id = oldId, text = "x", x = 1, y = 1 });
        Assert.Equal(ToolErrorCodes.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task InAppAssistant_WorksInDraft_AndProducesProposal()
    {
        SetMode(AiChangeMode.Approval, share: false);
        _settings.Assistant = new AssistantProviderSettings
        {
            Kind = AssistantProviderKind.Anthropic,
            ProtectedApiKey = SecretProtector.Protect("test-anahtari"),
            DataSharingConsent = true,
        };
        _settings.Assistant.ConsentScope = _settings.Assistant.CurrentScope;
        AiSettingsStore.Save(_settings);
        _main.AiSettings.Invalidate();

        var model = new ScriptedModel(LiveId);
        _main.Ai.ModelFactoryOverride = (_, key) =>
        {
            Assert.Equal("test-anahtari", key);
            return model;
        };
        _main.Ai.Prompt = "Ortaya 'Melamin Kase' yaz.";
        await _main.Ai.SendCommand.ExecuteAsync(null);

        Assert.Equal(2, model.Calls);
        // Asistan açık belgeye dokunmadı; öneri bekliyor.
        Assert.DoesNotContain(_main.Session.Document.Elements, e => e is TextElement { Text: "Melamin Kase" });
        var proposal = Assert.Single(_main.Ai.Proposals);
        Assert.Equal(AiPanelViewModel.AssistantClientName, proposal.Info.ClientName);
        await _main.Ai.AcceptCommand.ExecuteAsync(proposal);
        Assert.Contains(_main.Session.Document.Elements, e => e is TextElement { Text: "Melamin Kase" });
    }

    [Fact]
    public async Task InAppAssistant_WithoutConsent_SendsNothing()
    {
        _settings.Assistant = new AssistantProviderSettings { Kind = AssistantProviderKind.Anthropic, ProtectedApiKey = SecretProtector.Protect("k") };
        AiSettingsStore.Save(_settings);
        _main.AiSettings.Invalidate();
        bool created = false;
        _main.Ai.ModelFactoryOverride = (_, _) =>
        {
            created = true;
            return new ScriptedModel(LiveId);
        };
        _main.Ai.Prompt = "Bir şey yap";
        await _main.Ai.SendCommand.ExecuteAsync(null);
        Assert.False(created);
        Assert.Contains(_main.Ai.Messages, m => m.Text.Contains("izin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ShowInApp_OffersNewDesign_AndOpensItAsNormalDocument()
    {
        var created = (JsonObject)(await Agent("create_design", new { name = "Ajanın Etiketi", width_mm = 80, height_mm = 50 })).Structured!;
        string id = created["design_id"]!.GetValue<string>();
        Assert.False((await Agent("add_text", new { design_id = id, text = "Ajan yazdı", placement = "center" })).IsError);
        var shown = await Agent("show_in_app", new { design_id = id });
        Assert.False(shown.IsError, shown.Text);
        var request = Assert.Single(_main.Ai.OpenRequests);
        Assert.True(_main.IsAiPanelOpen);

        _main.Ai.OpenRequestedCommand.Execute(request);
        Assert.Empty(_main.Ai.OpenRequests);
        Assert.Equal(80, _main.Session.Document.Label.WidthMm);
        Assert.Contains(_main.Session.Elements, vm => vm.Model is TextElement { Text: "Ajan yazdı" });
        Assert.Null(_main.Session.Project.FilePath);
    }

    [Fact]
    public void SecretProtector_RoundTripsAndRejectsGarbage()
    {
        string protectedValue = SecretProtector.Protect("sk-test-123");
        Assert.DoesNotContain("sk-test-123", protectedValue);
        Assert.Equal("sk-test-123", SecretProtector.Unprotect(protectedValue));
        Assert.Null(SecretProtector.Unprotect("bozuk-deger"));
    }

    /// <summary>Önce add_text aracını çağıran, sonra özet yazan senaryolu model.</summary>
    private sealed class ScriptedModel(string liveId) : IChatModel
    {
        public int Calls { get; private set; }
        public string ProviderName => "Test";
        public string Model => "senaryo";

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == 1)
            {
                var args = JsonSerializer.SerializeToElement(new { design_id = liveId, text = "Melamin Kase", placement = "center", font_size_pt = 18 });
                return Task.FromResult(new ChatResponse
                {
                    StopReason = ChatStopReason.ToolUse,
                    Message = new ChatMessage(ChatRole.Assistant, [new ChatToolCallBlock("t1", "add_text", args)]),
                });
            }
            return Task.FromResult(new ChatResponse
            {
                StopReason = ChatStopReason.EndTurn,
                Message = new ChatMessage(ChatRole.Assistant, [new ChatTextBlock("Ortaya 'Melamin Kase' yazısını ekledim.")]),
            });
        }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public string? ShowOpenFileDialog(string title, string filter, string? initialDirectory = null) => null;
        public string? ShowSaveFileDialog(string title, string filter, string defaultFileName, string? initialDirectory = null) => null;
        public void ShowMessage(string title, string message, MessageKind kind = MessageKind.Info) { }
        public bool Confirm(string title, string message, string confirmText = "Evet", string cancelText = "Hayır") => true;
        public SaveChangesChoice AskSaveChanges(string documentName) => SaveChangesChoice.Discard;
        public string? Prompt(string title, string message, string defaultValue = "") => null;
        public bool ShowDialog(object viewModel) => viewModel is LabelSetupViewModel;
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public void SetElements(string payloadJson, string plainText) { }
        public string? GetElements() => null;
        public bool ContainsElements() => false;
        public byte[]? GetImagePng() => null;
        public string? GetText() => null;
    }
}
