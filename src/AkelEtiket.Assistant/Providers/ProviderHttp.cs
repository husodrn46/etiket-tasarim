using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AkelEtiket.Assistant.Chat;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Assistant.Providers;

/// <summary>Başarısız HTTP yanıtının özü (hata eşlemesi ve yeniden deneme kararı için).</summary>
internal sealed record ProviderHttpFailure(int StatusCode, string Body, TimeSpan? RetryAfter, string? ErrorType, string? ErrorMessage);

/// <summary>
/// Sağlayıcı istekleri için ortak HTTP katmanı: zaman aşımı, geçici hatalarda geri çekilmeli yeniden deneme
/// (Retry-After başlığına uyulur), Türkçe hata iletileri ve anahtarın iletilerden temizlenmesi.
/// </summary>
internal static class ProviderHttp
{
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    /// <summary>Yeniden denenebilecek durum kodları.</summary>
    public static bool IsRetryableStatus(int status) =>
        status is 408 or 409 or 429 or 500 or 502 or 503 or 504 or 529;

    /// <summary>
    /// İsteği gönderir; başarılı yanıtın gövdesini döndürür. <paramref name="mapFailure"/> başarısız yanıtı
    /// istisnaya çevirir; dönen istisna geçiciyse ve hak kaldıysa beklenip yeniden denenir.
    /// <paramref name="onFailure"/> null olmayan bir değer döndürürse (ör. isteğe bağlı bir özelliği kapatıp
    /// yeniden denemek için) deneme hakkı harcanmadan tekrar gönderilir.
    /// </summary>
    public static async Task<string> SendAsync(
        HttpClient http,
        Func<HttpRequestMessage> buildRequest,
        ChatModelOptions options,
        Func<ProviderHttpFailure, ChatModelException> mapFailure,
        Func<ProviderHttpFailure, bool>? retryAdjusted,
        Func<HttpRequestException, ChatModelException> mapNetworkError,
        string? secret,
        CancellationToken cancellationToken)
    {
        int attempt = 0;
        int adjustments = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.RequestTimeout);
            HttpResponseMessage? response = null;
            string body;
            try
            {
                using var request = buildRequest();
                response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
                body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                response?.Dispose();
                throw;
            }
            catch (OperationCanceledException ex)
            {
                response?.Dispose();
                // Kendi zaman aşımımız veya HttpClient.Timeout.
                throw new ChatModelException(ChatErrorKind.Timeout,
                    L10n.T($"Yanıt {FormatSeconds(options.RequestTimeout)} içinde gelmedi (zaman aşımı). Tekrar deneyin veya daha kısa bir istek gönderin."),
                    inner: ex);
            }
            catch (HttpRequestException ex)
            {
                response?.Dispose();
                var mapped = mapNetworkError(ex);
                if (attempt < options.MaxRetries)
                {
                    await options.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }
                throw Scrub(mapped, secret);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode) return body;

                var failure = new ProviderHttpFailure((int)response.StatusCode, body, GetRetryAfter(response), null, null);
                failure = WithParsedError(failure);

                if (retryAdjusted is not null && adjustments < 2 && retryAdjusted(failure))
                {
                    adjustments++;
                    continue;
                }

                var error = Scrub(mapFailure(failure), secret);
                if (IsRetryableStatus(failure.StatusCode) && attempt < options.MaxRetries)
                {
                    var wait = failure.RetryAfter is { } ra ? (ra > MaxRetryAfter ? MaxRetryAfter : ra) : Backoff(attempt);
                    await options.Delay(wait, cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }
                throw error;
            }
        }
    }

    /// <summary>Üstel geri çekilme (1 sn, 2 sn, 4 sn …; en çok 30 sn) + küçük rastgele sapma.</summary>
    public static TimeSpan Backoff(int attempt)
    {
        double seconds = Math.Pow(2, attempt);
        double jitter = 1 + (Random.Shared.NextDouble() - 0.5) * 0.4;
        var value = TimeSpan.FromSeconds(seconds * jitter);
        return value > MaxBackoff ? MaxBackoff : value;
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? retry = response.Headers.RetryAfter;
        if (retry?.Delta is { } delta) return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (retry?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }
        return null;
    }

    /// <summary>Gövdedeki hata türünü ve iletisini çıkarır (Anthropic, OpenAI ve Ollama biçimleri).</summary>
    private static ProviderHttpFailure WithParsedError(ProviderHttpFailure failure)
    {
        if (string.IsNullOrWhiteSpace(failure.Body)) return failure;
        try
        {
            var node = JsonNode.Parse(failure.Body);
            if (node is not JsonObject obj) return failure;
            string? type = null;
            string? message = null;
            switch (obj["error"])
            {
                case JsonObject error:
                    type = AsString(error["type"]) ?? AsString(error["code"]);
                    message = AsString(error["message"]);
                    break;
                case JsonValue value:
                    message = AsString(value);
                    break;
            }
            message ??= AsString(obj["message"]);
            return failure with { ErrorType = type, ErrorMessage = message };
        }
        catch (JsonException)
        {
            return failure;
        }
    }

    internal static string? AsString(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue(out string? text)) return text;
        return value.ToJsonString();
    }

    /// <summary>Durum koduna göre Türkçe ileti ve hata türü.</summary>
    public static ChatModelException MapStatus(ProviderHttpFailure failure, string providerName, string model)
    {
        string detail = string.IsNullOrWhiteSpace(failure.ErrorMessage) ? "" : L10n.T(" Sağlayıcının iletisi: ") + Shorten(failure.ErrorMessage!, 400);
        int status = failure.StatusCode;
        (ChatErrorKind kind, string text) = status switch
        {
            400 => (ChatErrorKind.BadRequest, L10n.T($"{providerName} isteği geçersiz buldu (400).")),
            401 => (ChatErrorKind.Authentication, L10n.T($"{providerName} API anahtarı geçersiz veya eksik (401). Ayarlar → Yapay Zekâ bölümünden anahtarı kontrol edin.")),
            402 => (ChatErrorKind.Billing, L10n.T($"{providerName} hesabında ödeme veya bakiye sorunu var (402).")),
            403 => (ChatErrorKind.Permission, L10n.T($"Bu anahtarın {providerName} üzerinde bu işlem veya model için izni yok (403).")),
            404 => (ChatErrorKind.NotFound, L10n.T($"\"{model}\" modeli ya da uç nokta bulunamadı veya hesabınızda kullanılamıyor (404).")),
            413 => (ChatErrorKind.TooLarge, L10n.T("İstek çok büyük (413). Daha küçük bir tasarımla veya önizleme görseli göndermeden deneyin.")),
            429 => (ChatErrorKind.RateLimited, L10n.T($"{providerName} istek sınırı aşıldı (429). Biraz bekleyip tekrar deneyin.")),
            529 => (ChatErrorKind.Overloaded, L10n.T($"{providerName} şu anda aşırı yoğun (529). Biraz sonra tekrar deneyin.")),
            503 => (ChatErrorKind.Overloaded, L10n.T($"{providerName} şu anda hizmet veremiyor (503). Biraz sonra tekrar deneyin.")),
            >= 500 => (ChatErrorKind.Server, L10n.T($"{providerName} tarafında geçici bir sunucu hatası oluştu ({status}). Biraz sonra tekrar deneyin.")),
            _ => (ChatErrorKind.Other, L10n.T($"{providerName} beklenmeyen bir hata döndürdü ({status}).")),
        };
        return new ChatModelException(kind, text + detail, status, failure.ErrorType);
    }

    /// <summary>İletide anahtar geçiyorsa gizler.</summary>
    public static ChatModelException Scrub(ChatModelException error, string? secret)
    {
        if (string.IsNullOrEmpty(secret) || !error.Message.Contains(secret, StringComparison.Ordinal)) return error;
        return new ChatModelException(error.Kind, error.Message.Replace(secret, "***", StringComparison.Ordinal),
            error.StatusCode, error.ProviderErrorType, error.InnerException is null ? null : new ChatModelInnerException());
    }

    public static string ScrubText(string text, string? secret) =>
        string.IsNullOrEmpty(secret) ? text : text.Replace(secret, "***", StringComparison.Ordinal);

    public static string Shorten(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    private static string FormatSeconds(TimeSpan value) =>
        value.TotalMinutes >= 1 ? $"{value.TotalMinutes:0.#} dakika" : $"{value.TotalSeconds:0} saniye";

    public static StringContent JsonContent(JsonNode body) =>
        new(body.ToJsonString(), Encoding.UTF8, "application/json");

    public static bool IsConnectionRefused(HttpRequestException ex) =>
        ex.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError ||
        ex.StatusCode is HttpStatusCode.BadGateway;

    /// <summary>Anahtar sızmasını önlemek için iç istisnanın yerine konan boş istisna.</summary>
    private sealed class ChatModelInnerException() : Exception(L10n.T("(ayrıntı gizlendi)"));
}
