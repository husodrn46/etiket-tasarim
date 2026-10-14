using AkelEtiket.Assistant.Chat;
using AkelEtiket.Design.Tools;

namespace AkelEtiket.Assistant;

/// <summary>
/// Uygulama içi asistanın araç döngüsü: kullanıcı isteğini modele gönderir, modelin istediği araçları sırayla
/// <see cref="IToolExecutor"/> üzerinden çalıştırır, sonuçları geri besler ve model bitirene, adım sınırına ya da
/// iptale kadar sürer. <see cref="ToolCallContext"/> her araç çağrısına değiştirilmeden geçirilir (taslak ve kapsam
/// kısıtlarını yürütücü uygular).
/// </summary>
/// <remarks>
/// Sohbet geçmişi yalnızca sona ekleme yapılarak büyür; sistem istemi ve araç listesi oturum boyunca sabittir.
/// Böylece sağlayıcının düşünme blokları geçerli kalır ve istem önbelleği çalışır. Aynı oturumda ikinci bir istek
/// (çok turlu sohbet) önceki geçmişin üzerine eklenir.
/// </remarks>
public sealed class DesignAssistantSession
{
    private readonly IChatModel _model;
    private readonly IToolExecutor _executor;
    private readonly ToolCallContext _context;
    private readonly AssistantSessionOptions _options;
    private readonly IReadOnlyList<ChatToolSpec> _toolSpecs;
    private readonly HashSet<string> _toolNames;
    private readonly List<ChatMessage> _history = [];
    private int _running;

    public DesignAssistantSession(IChatModel model, IToolExecutor executor, ToolCallContext context,
        AssistantSessionOptions? options = null, string? systemPrompt = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _options = options ?? new AssistantSessionOptions();
        // Araç listesi oturum başında sabitlenir (sohbet boyunca değişmemeli).
        _toolSpecs = executor.Tools.Select(ChatToolSpec.From).ToList();
        _toolNames = new HashSet<string>(_toolSpecs.Select(t => t.Name), StringComparer.Ordinal);
        SystemPrompt = systemPrompt ?? SystemPromptBuilder.Build(_toolNames, _options.DotsPerMm);
    }

    /// <summary>Oturum boyunca değişmeyen sistem istemi.</summary>
    public string SystemPrompt { get; }

    /// <summary>Modele sunulan araçlar.</summary>
    public IReadOnlyList<ChatToolSpec> Tools => _toolSpecs;

    /// <summary>Sohbet geçmişi (yalnızca okunur görünüm).</summary>
    public IReadOnlyList<ChatMessage> History => _history;

    /// <summary>Bu oturumun araç çağrılarına geçirdiği bağlam.</summary>
    public ToolCallContext Context => _context;

    public IChatModel Model => _model;

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    /// <summary>Geçmişi temizler (yeni sohbet). Çalışırken çağrılamaz.</summary>
    public void Reset()
    {
        if (IsRunning) throw new InvalidOperationException("Asistan çalışırken sohbet sıfırlanamaz.");
        _history.Clear();
    }

    /// <summary>
    /// Bir isteği sonuna kadar çalıştırır. İstisna fırlatmaz: iptal, sağlayıcı hatası ve izin eksikliği sonuçta
    /// <see cref="AssistantRunResult.Status"/> olarak döner.
    /// </summary>
    public async Task<AssistantRunResult> RunAsync(AssistantRequest request, IProgress<AssistantEvent>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.UserText)) throw new ArgumentException("İstek metni boş olamaz.", nameof(request));
        if (Interlocked.Exchange(ref _running, 1) == 1)
            throw new InvalidOperationException("Asistan zaten çalışıyor; önceki istek bitmeden yenisi başlatılamaz.");
        try
        {
            return await RunCoreAsync(request, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private async Task<AssistantRunResult> RunCoreAsync(AssistantRequest request, IProgress<AssistantEvent>? progress, CancellationToken cancellationToken)
    {
        var records = new List<AssistantToolCallRecord>();
        var usage = ChatUsage.Zero;
        int iterations = 0;
        int historyCountBefore = _history.Count;
        ChatMessage? mergedInto = null;
        bool assistantReplied = false;
        List<ChatToolResultBlock>? inFlight = null;

        // Kullanıcı mesajı. Geçmiş bir kullanıcı mesajıyla bitiyorsa (ör. iptal sonrası araç sonuçları) ona eklenir:
        // ardışık iki kullanıcı mesajı gönderilmez.
        var userBlocks = AssistantContextBuilder.Build(request, _context, _options.MaxContextChars);
        if (_history.Count > 0 && _history[^1].Role == ChatRole.User)
        {
            mergedInto = _history[^1];
            _history[^1] = new ChatMessage(ChatRole.User, mergedInto.Blocks.Concat(userBlocks).ToList());
        }
        else
        {
            _history.Add(new ChatMessage(ChatRole.User, userBlocks));
        }

        try
        {
            string lastText = "";
            while (true)
            {
                if (iterations >= _options.MaxIterations)
                {
                    string text = $"Adım sınırına ({_options.MaxIterations}) ulaşıldı; işlem tamamlanmadan durdu. Şu ana kadarki değişiklikler taslakta duruyor ve onayınızı bekliyor.";
                    return Finish(AssistantRunStatus.MaxIterationsReached, Combine(lastText, text), null);
                }

                iterations++;
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new AssistantEvent(AssistantEventKind.ModelCallStarted, iterations));
                var response = await _model.CompleteAsync(new ChatRequest
                {
                    SystemPrompt = SystemPrompt,
                    Messages = _history.ToList(),
                    Tools = _toolSpecs,
                    MaxOutputTokens = _options.MaxOutputTokens,
                }, cancellationToken).ConfigureAwait(false);

                _history.Add(response.Message);
                assistantReplied = true;
                usage = usage.Add(response.Usage);
                if (response.Text.Length > 0)
                {
                    lastText = response.Text;
                    progress?.Report(new AssistantEvent(AssistantEventKind.AssistantText, iterations, Text: response.Text));
                }

                var calls = response.ToolCalls;
                switch (response.StopReason)
                {
                    case ChatStopReason.Refusal:
                    {
                        AnswerToolCalls(calls, null, "Sağlayıcı isteği reddettiği için araç çalıştırılmadı.");
                        string category = string.IsNullOrWhiteSpace(response.RefusalCategory) ? "" : $" (kategori: {response.RefusalCategory})";
                        string text = $"Sağlayıcı bu isteği güvenlik denetimi nedeniyle yanıtlamadı{category}. İsteği farklı ifade ederek tekrar deneyin.";
                        return Finish(AssistantRunStatus.Refused, text, null);
                    }
                    case ChatStopReason.MaxTokens when calls.Count > 0:
                        // Kesilmiş araç çağrılarına güvenilmez; hatayla yanıtlanır, model daha kısa adımlarla devam eder.
                        _history.Add(new ChatMessage(ChatRole.User, calls.Select(c => (ChatContentBlock)new ChatToolResultBlock(
                            c.Id, "Yanıt uzunluk sınırına ulaştığı için bu araç çalıştırılmadı. Daha kısa adımlarla tekrar dene.", [], true)).ToList()));
                        continue;
                    case ChatStopReason.MaxTokens:
                        return Finish(AssistantRunStatus.MaxTokensReached,
                            Combine(lastText, "Yanıt uzunluk sınırına ulaştı; özet yarım kalmış olabilir."), null);
                    default:
                        if (calls.Count == 0)
                            return Finish(AssistantRunStatus.Completed,
                                lastText.Length > 0 ? lastText : "İşlem tamamlandı.", null);
                        break;
                }

                inFlight = [];
                foreach (var call in calls)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var block = await ExecuteToolAsync(call, iterations, records, progress, cancellationToken).ConfigureAwait(false);
                    inFlight.Add(block);
                }
                _history.Add(new ChatMessage(ChatRole.User, inFlight.Cast<ChatContentBlock>().ToList()));
                inFlight = null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!assistantReplied) RollBack(historyCountBefore, mergedInto);
            else AnswerToolCalls(PendingCalls(), inFlight, "İşlem kullanıcı tarafından durduruldu; bu araç çalıştırılmadı.");
            const string text = "İşlem durduruldu. Şu ana kadarki değişiklikler taslakta duruyor ve onayınızı bekliyor.";
            progress?.Report(new AssistantEvent(AssistantEventKind.Cancelled, iterations, Text: text, Status: AssistantRunStatus.Cancelled));
            return new AssistantRunResult
            {
                Status = AssistantRunStatus.Cancelled,
                FinalText = text,
                ToolCalls = records,
                Iterations = iterations,
                Usage = usage,
            };
        }
        catch (Exception ex) when (ex is ChatModelException or AssistantConsentException or AssistantConfigurationException)
        {
            if (!assistantReplied) RollBack(historyCountBefore, mergedInto);
            else AnswerToolCalls(PendingCalls(), inFlight, "Bir hata nedeniyle bu araç çalıştırılmadı.");
            progress?.Report(new AssistantEvent(AssistantEventKind.Failed, iterations, Text: ex.Message, IsError: true, Status: AssistantRunStatus.Failed));
            return new AssistantRunResult
            {
                Status = AssistantRunStatus.Failed,
                FinalText = ex.Message,
                ErrorMessage = ex.Message,
                ToolCalls = records,
                Iterations = iterations,
                Usage = usage,
            };
        }

        AssistantRunResult Finish(AssistantRunStatus status, string text, string? error)
        {
            progress?.Report(new AssistantEvent(AssistantEventKind.Completed, iterations, Text: text, Status: status));
            return new AssistantRunResult
            {
                Status = status,
                FinalText = text,
                ErrorMessage = error,
                ToolCalls = records,
                Iterations = iterations,
                Usage = usage,
            };
        }
    }

    private async Task<ChatToolResultBlock> ExecuteToolAsync(ChatToolCallBlock call, int iteration,
        List<AssistantToolCallRecord> records, IProgress<AssistantEvent>? progress, CancellationToken cancellationToken)
    {
        string argsSummary = Summarize(call.Arguments.GetRawText(), 160);
        progress?.Report(new AssistantEvent(AssistantEventKind.ToolCallStarted, iteration, call.Name, argsSummary));

        ToolResult result;
        if (!_toolNames.Contains(call.Name))
        {
            result = new ToolResult { IsError = true, ErrorCode = "UNKNOWN_TOOL", Text = $"Bilinmeyen araç: \"{call.Name}\". Yalnızca listelenen araçları kullan." };
        }
        else
        {
            try
            {
                result = await _executor.ExecuteAsync(call.Name, call.Arguments, _context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = new ToolResult { IsError = true, ErrorCode = "TOOL_FAILED", Text = $"Araç çalıştırılamadı: {ex.Message}" };
            }
        }

        string text = Truncate(result.Text ?? "", _options.MaxToolResultChars);
        var images = result.Images.Select(i => new ChatImageBlock(i.MimeType, i.Data)).ToList();
        string resultSummary = Summarize(result.Text ?? "", 200);
        records.Add(new AssistantToolCallRecord(iteration, call.Name, argsSummary, result.IsError, resultSummary));
        progress?.Report(new AssistantEvent(AssistantEventKind.ToolCallFinished, iteration, call.Name, resultSummary, result.IsError));
        return new ChatToolResultBlock(call.Id, text, images, result.IsError);
    }

    /// <summary>Son asistan mesajındaki yanıtsız araç çağrıları (geçmiş tutarlı kalsın diye yanıtlanır).</summary>
    private IReadOnlyList<ChatToolCallBlock> PendingCalls() =>
        _history.Count > 0 && _history[^1].Role == ChatRole.Assistant ? _history[^1].ToolCalls : [];

    /// <summary>
    /// Yanıtsız araç çağrılarını, tamamlananların gerçek sonuçları ve kalanlar için <paramref name="note"/> ile yanıtlar.
    /// Sağlayıcılar yanıtsız araç çağrısı içeren geçmişi reddettiği için gereklidir.
    /// </summary>
    private void AnswerToolCalls(IReadOnlyList<ChatToolCallBlock> calls, List<ChatToolResultBlock>? completed, string note)
    {
        if (calls.Count == 0) return;
        var done = (completed ?? []).ToDictionary(r => r.ToolCallId, StringComparer.Ordinal);
        var blocks = calls.Select(c => (ChatContentBlock)(done.TryGetValue(c.Id, out var r)
            ? r
            : new ChatToolResultBlock(c.Id, note, [], true))).ToList();
        _history.Add(new ChatMessage(ChatRole.User, blocks));
    }

    /// <summary>Model hiç yanıt vermeden biten isteği geçmişten geri alır.</summary>
    private void RollBack(int countBefore, ChatMessage? mergedInto)
    {
        if (mergedInto is not null && _history.Count == countBefore)
        {
            _history[^1] = mergedInto;
            return;
        }
        while (_history.Count > countBefore) _history.RemoveAt(_history.Count - 1);
    }

    internal static string Truncate(string text, int max)
    {
        if (max <= 0 || text.Length <= max) return text;
        return text[..max] + $"\n\n[Çıktı kısaltıldı: toplam {text.Length} karakterin ilk {max} karakteri gönderildi.]";
    }

    private static string Summarize(string text, int max)
    {
        string single = string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return single.Length <= max ? single : single[..max] + "…";
    }

    private static string Combine(string first, string second) =>
        string.IsNullOrWhiteSpace(first) ? second : first.TrimEnd() + "\n\n" + second;
}
