using System.Runtime.InteropServices;
using AkelEtiket.PrintEngine.Interop;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.PrintEngine;

/// <summary>Yazdırma kuyruğuna ham veri yazan iş (test edilebilirlik için soyutlama).</summary>
internal interface IRawJobWriter : IDisposable
{
    int JobId { get; }
    void Write(ReadOnlySpan<byte> data);
    void Complete();
    void Abort();
}

/// <summary>
/// Windows yazdırma biriktiricisine "RAW" veri türünde iş. Veri sürücü tarafından işlenmeden
/// (yazdırma işlemcisi winprint, grafik DLL'i çağrılmaz) doğrudan yazıcı portuna iletilir.
/// Veri türü tam olarak "RAW" olmalıdır; "RAW [FF appended]" gibi türler sona form-feed ekler.
/// </summary>
public sealed unsafe class RawPrintJob : IRawJobWriter
{
    /// <summary>Tek WritePrinter çağrısıyla yazılan en büyük parça.</summary>
    private const int ChunkSize = 64 * 1024;

    private readonly SafePrinterHandle _handle;
    private bool _pageOpen;
    private bool _docOpen;
    private bool _finished;

    private RawPrintJob(SafePrinterHandle handle, string printerName, string documentName, int jobId)
    {
        _handle = handle;
        PrinterName = printerName;
        DocumentName = documentName;
        JobId = jobId;
        _docOpen = true;
        _pageOpen = true;
    }

    public string PrinterName { get; }
    public string DocumentName { get; }

    /// <summary>Windows kuyruğundaki iş numarası.</summary>
    public int JobId { get; }

    public long BytesWritten { get; private set; }

    /// <summary>Yazıcıyı açar, RAW belge ve sayfa başlatır. Hata durumunda Türkçe <see cref="PrintEngineException"/>.</summary>
    public static RawPrintJob Start(string printerName, string documentName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            throw new PrintEngineException(L10n.T("Yazıcı seçilmedi. Ayarlar'dan etiket yazıcısını seçin."));
        if (!NativeMethods.TryOpen(printerName, out var handle, out int openError))
            throw PrintEngineException.FromWin32(L10n.T("Yazıcı açılamadı"), openError, printerName);

        var docInfo = new NativeMethods.DOC_INFO_1
        {
            pDocName = string.IsNullOrWhiteSpace(documentName) ? AkelEtiket.Core.AppIdentity.ProductName : documentName,
            pOutputFile = null,
            pDatatype = "RAW",
        };
        int jobId = NativeMethods.StartDocPrinter(handle, 1, ref docInfo);
        if (jobId == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw PrintEngineException.FromWin32(L10n.T("Yazdırma işi başlatılamadı"), error, printerName);
        }
        if (!NativeMethods.StartPagePrinter(handle))
        {
            int error = Marshal.GetLastPInvokeError();
            NativeMethods.AbortPrinter(handle);
            handle.Dispose();
            throw PrintEngineException.FromWin32(L10n.T("Yazdırma sayfası başlatılamadı"), error, printerName);
        }
        return new RawPrintJob(handle, printerName, docInfo.pDocName, jobId);
    }

    /// <summary>Verinin tamamını yazar (kısmi yazmalar döngüyle tamamlanır).</summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        if (_finished) throw new InvalidOperationException(L10n.T("İş tamamlandıktan sonra veri yazılamaz."));
        int offset = 0;
        while (offset < data.Length)
        {
            int count = Math.Min(ChunkSize, data.Length - offset);
            bool ok;
            int written;
            fixed (byte* p = data.Slice(offset, count))
            {
                ok = NativeMethods.WritePrinter(_handle, p, count, out written);
            }
            if (!ok)
                throw PrintEngineException.FromWin32(L10n.T("Yazıcıya veri gönderilemedi"), Marshal.GetLastPInvokeError(), PrinterName);
            if (written <= 0)
                throw new PrintEngineException(L10n.T($"'{PrinterName}' yazıcısı veri kabul etmiyor (0 bayt yazıldı)."));
            offset += written;
            BytesWritten += written;
        }
    }

    /// <summary>Sayfayı ve belgeyi kapatır; iş kuyrukta yazıcıya gönderilmeye başlar.</summary>
    public void Complete()
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        if (_finished) return;
        if (_pageOpen)
        {
            _pageOpen = false;
            if (!NativeMethods.EndPagePrinter(_handle))
                throw PrintEngineException.FromWin32(L10n.T("Yazdırma sayfası kapatılamadı"), Marshal.GetLastPInvokeError(), PrinterName);
        }
        if (_docOpen)
        {
            _docOpen = false;
            if (!NativeMethods.EndDocPrinter(_handle))
                throw PrintEngineException.FromWin32(L10n.T("Yazdırma işi tamamlanamadı"), Marshal.GetLastPInvokeError(), PrinterName);
        }
        _finished = true;
    }

    /// <summary>İşi iptal eder; biriktirme dosyası silinir.</summary>
    public void Abort()
    {
        if (_finished || _handle.IsClosed) return;
        NativeMethods.AbortPrinter(_handle);
        _pageOpen = false;
        _docOpen = false;
        _finished = true;
    }

    public void Dispose()
    {
        if (!_finished && !_handle.IsClosed) Abort();
        _handle.Dispose();
    }
}
