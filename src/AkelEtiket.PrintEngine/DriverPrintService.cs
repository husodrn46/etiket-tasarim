using System.Globalization;
using System.Runtime.InteropServices;
using AkelEtiket.Core.Imaging;
using AkelEtiket.PrintEngine.Interop;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.PrintEngine;

/// <summary>Sürücü yolunda yazıcı DC'sinden okunan aygıt bilgisi (tanılama ve testler için).</summary>
/// <param name="DpiX">Yatay aygıt çözünürlüğü (LOGPIXELSX).</param>
/// <param name="DpiY">Dikey aygıt çözünürlüğü (LOGPIXELSY).</param>
/// <param name="PhysicalWidth">Sürücünün kurduğu fiziksel sayfa genişliği (aygıt noktası).</param>
/// <param name="PhysicalHeight">Fiziksel sayfa yüksekliği (aygıt noktası).</param>
/// <param name="OffsetX">Yazdırılamayan sol kenar (aygıt noktası).</param>
/// <param name="OffsetY">Yazdırılamayan üst kenar (aygıt noktası).</param>
public sealed record DriverPageInfo(int DpiX, int DpiY, int PhysicalWidth, int PhysicalHeight, int OffsetX, int OffsetY)
{
    public double PhysicalWidthMm => DpiX > 0 ? PhysicalWidth * 25.4 / DpiX : 0;
    public double PhysicalHeightMm => DpiY > 0 ? PhysicalHeight * 25.4 / DpiY : 0;

    /// <summary>Etiketin üretildiği yatay çözünürlük (nokta/mm).</summary>
    public double RenderDotsPerMmX { get; init; }

    /// <summary>Etiketin üretildiği dikey çözünürlük (nokta/mm).</summary>
    public double RenderDotsPerMmY { get; init; }

    /// <summary>Sürücünün kullandığı kağıt kimliği (256 = özel boyut).</summary>
    public int PaperId { get; init; }

    /// <summary>Üretilen etiket görüntüsünün boyutu (nokta).</summary>
    public int BitmapWidth { get; init; }

    public int BitmapHeight { get; init; }

    /// <summary>Sürücünün kurduğu sayfa istenen etiket ölçüsüyle (±1 mm) eşleşiyor mu?</summary>
    public bool PageMatches(LabelMedia media) =>
        Math.Abs(PhysicalWidthMm - media.WidthMm) <= 1.0 && Math.Abs(PhysicalHeightMm - media.HeightMm) <= 1.0;
}

/// <summary>
/// Windows sürücüsü (GDI) üzerinden baskı. Çift ölçekleme olmaması için:
/// <list type="number">
/// <item>Sürücü ayarlarına (DEVMODE) etiket boyutu yazılır: sürücünün form listesinde birebir eşleşen boyut varsa o form,
/// yoksa 0,1 mm hassasiyetli özel boyut (DMPAPER_USER); sürücü DocumentProperties ile doğrular.</item>
/// <item>Yazıcı DC'sinin gerçek çözünürlüğü okunur; etiket bu çözünürlükte 1-bit üretilir. Sürücünün sayfası istenen
/// ölçüyle eşleşiyorsa nokta/mm doğrudan fiziksel sayfadan hesaplanır (ör. 100 mm = 800 nokta → tam 8 nokta/mm).</item>
/// <item>Görüntü 1 kaynak pikseli = 1 aygıt noktası olarak (StretchDIBits, kaynak = hedef boyut) kağıdın fiziksel
/// sol üst köşesine çizilir. Hiçbir ölçekleme/yumuşatma yoktur.</item>
/// <item>Sürücünün kurduğu sayfa istenen ölçüden saparsa veya yazdırılamaz kenar bildirirse açıkça uyarılır.</item>
/// </list>
/// Bu yol hiçbir zaman TSPL göndermez; TSPL yolu da sürücü grafiklerini kullanmaz.
/// </summary>
public sealed class DriverPrintService
{
    private const int BandRows = 256;

    /// <summary>Dosyaya yazdır (ör. "Microsoft Print to PDF" için PDF yolu). Boşsa yazıcıya gider.</summary>
    public string? OutputFilePath { get; init; }

    public TimeSpan QueueWaitTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Yazıcı hazır görünmese de gönder.</summary>
    public bool AllowPrintWhenOffline { get; init; }

    public string DocumentName { get; init; } = AkelEtiket.Core.AppIdentity.ProductName;

    /// <summary>Son baskıda ölçülen aygıt bilgisi.</summary>
    public DriverPageInfo? LastPageInfo { get; private set; }

    /// <summary>
    /// Yazdırma işi OLUŞTURMADAN sürücünün bu etiket ölçüsü için kuracağı sayfayı ve çözünürlüğü sorgular
    /// (yalnızca aygıt bağlamı açılıp kapatılır; yazıcıya hiçbir veri gitmez).
    /// </summary>
    public static DriverPageInfo? Probe(string printerName, LabelMedia media, out string? error)
    {
        ArgumentNullException.ThrowIfNull(media);
        var report = new PrintReport { Method = PrintMethod.WindowsDriver, PrinterName = printerName ?? "" };
        if (string.IsNullOrWhiteSpace(printerName))
        {
            error = L10n.T("Yazıcı seçilmedi.");
            return null;
        }
        if (!TryCreateDc(report, printerName, media, out IntPtr hdc, out var caps))
        {
            error = report.Errors.FirstOrDefault() ?? L10n.T("Yazıcı aygıt bağlamı oluşturulamadı.");
            return null;
        }
        GdiNative.DeleteDC(hdc);
        error = null;
        return caps;
    }

    /// <summary>
    /// Etiketleri sürücü üzerinden yazdırır. <paramref name="renderPage"/>(sayfa, noktaX/mm, noktaY/mm) aygıtın
    /// gerçek çözünürlüğünde 1-bit görüntü üretmelidir. <paramref name="copiesForPage"/> her sayfanın adedidir.
    /// </summary>
    public Task<PrintReport> PrintAsync(string printerName, LabelMedia media, int pageCount,
        Func<int, double, double, MonoBitmap> renderPage, Func<int, int> copiesForPage,
        IProgress<PrintProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(renderPage);
        ArgumentNullException.ThrowIfNull(copiesForPage);
        return Task.Run(() => PrintCoreAsync(printerName ?? "", media, pageCount, renderPage, copiesForPage, progress, cancellationToken),
            CancellationToken.None);
    }

    private async Task<PrintReport> PrintCoreAsync(string printerName, LabelMedia media, int pageCount,
        Func<int, double, double, MonoBitmap> renderPage, Func<int, int> copiesForPage,
        IProgress<PrintProgress>? progress, CancellationToken cancellationToken)
    {
        var report = new PrintReport { Method = PrintMethod.WindowsDriver, PrinterName = printerName };
        bool toFile = !string.IsNullOrWhiteSpace(OutputFilePath);
        if (string.IsNullOrWhiteSpace(printerName))
        {
            report.Errors.Add(L10n.T("Yazıcı seçilmedi. Ayarlar'dan yazıcıyı seçin."));
            return report;
        }
        report.Errors.AddRange(media.Validate());
        if (pageCount <= 0) report.Errors.Add(L10n.T("Yazdırılacak etiket yok."));
        if (report.Errors.Count > 0) return report;

        var status = PrinterDiscovery.GetStatus(printerName);
        if (!status.Exists)
        {
            report.Errors.Add(status.ErrorMessage ?? Win32ErrorText.Explain(Win32ErrorText.ErrorInvalidPrinterName, printerName));
            return report;
        }
        var printer = status.Printer!;
        if (!toFile && !printer.IsReady)
        {
            if (!AllowPrintWhenOffline)
            {
                report.Errors.Add(L10n.T($"Yazıcı hazır değil: {printer.StatusText}. {TsplPrintService.Advice(printer)}"));
                return report;
            }
            report.Warnings.Add(L10n.T($"Yazıcı hazır görünmüyor ({printer.StatusText}); ayar gereği yine de gönderildi."));
        }

        var sequence = new List<int>();
        for (int i = 0; i < pageCount; i++)
        {
            int copies = Math.Clamp(copiesForPage(i), 1, 9999);
            report.CopiesTotal += copies;
            for (int k = 0; k < copies; k++) sequence.Add(i);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            report.Canceled = true;
            return report;
        }

        // GDI çağrıları tek iş parçacığında, bekleme (await) olmadan yapılır.
        int jobId = PrintPages(report, printerName, media, sequence, renderPage, progress, cancellationToken);
        if (jobId <= 0) return report;

        report.JobIds.Add(jobId);
        if (toFile) report.OutputFile = OutputFilePath;
        progress?.Report(new PrintProgress { Done = sequence.Count, Total = sequence.Count, Stage = L10n.T("Kuyruk durumu bekleniyor") });
        try
        {
            var result = await SpoolerMonitor.WaitForJobAsync(printerName, jobId, QueueWaitTimeout, null, cancellationToken)
                .ConfigureAwait(false);
            report.QueueOutcome = result.Outcome;
            report.QueueMessage = result.Message;
            if (!result.Delivered)
            {
                if (result.Outcome == SpoolOutcome.TimedOut) report.Warnings.Add(result.Message);
                else report.Errors.Add(result.Message);
            }
        }
        catch (OperationCanceledException)
        {
            report.QueueOutcome = SpoolOutcome.TimedOut;
            report.QueueMessage = L10n.T("Kuyruk takibi iptal edildi; işin durumunu Windows yazıcı kuyruğundan kontrol edin.");
        }

        if (toFile && report.QueueOutcome is SpoolOutcome.LeftQueue or SpoolOutcome.Printed &&
            !await WaitForFileAsync(OutputFilePath!, TimeSpan.FromSeconds(15)).ConfigureAwait(false))
        {
            report.Errors.Add(L10n.T($"Çıktı dosyası oluşturulmadı: {OutputFilePath}"));
        }
        return report;
    }

    /// <summary>Sayfaları GDI ile basar; başarılıysa iş numarasını, değilse 0 döndürür (hata rapora yazılır).</summary>
    private int PrintPages(PrintReport report, string printerName, LabelMedia media, List<int> sequence,
        Func<int, double, double, MonoBitmap> renderPage, IProgress<PrintProgress>? progress, CancellationToken cancellationToken)
    {
        if (!TryCreateDc(report, printerName, media, out IntPtr hdc, out var caps)) return 0;
        try
        {
            LastPageInfo = caps;
            if ((GdiNative.GetDeviceCaps(hdc, GdiNative.RASTERCAPS) & GdiNative.RC_STRETCHDIB) == 0)
            {
                report.Errors.Add(L10n.T("Yazıcı sürücüsü bit eşlem çizimini desteklemiyor; TSPL baskı yöntemini kullanın."));
                return 0;
            }
            AddPageWarnings(report, media, caps);
            GdiNative.SetStretchBltMode(hdc, GdiNative.COLORONCOLOR);

            var docInfo = new GdiNative.DOCINFOW
            {
                cbSize = Marshal.SizeOf<GdiNative.DOCINFOW>(),
                lpszDocName = DocumentName,
                lpszOutput = string.IsNullOrWhiteSpace(OutputFilePath) ? null : OutputFilePath,
                lpszDatatype = null,
                fwType = 0,
            };
            int jobId = GdiNative.StartDoc(hdc, ref docInfo);
            if (jobId <= 0)
            {
                report.Errors.Add(PrintEngineException.FromWin32(L10n.T("Yazdırma işi başlatılamadı"), Marshal.GetLastPInvokeError(), printerName).Message);
                return 0;
            }

            bool docOpen = true;
            try
            {
                int cachedIndex = -1;
                MonoBitmap? cached = null;
                for (int position = 0; position < sequence.Count; position++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        report.Canceled = true;
                        report.Warnings.Add(L10n.T("İptal edildi: yazdırma işi silindi, sayfalar gönderilmedi."));
                        return 0;
                    }

                    int pageIndex = sequence[position];
                    if (cached is null || cachedIndex != pageIndex)
                    {
                        try
                        {
                            cached = renderPage(pageIndex, caps.RenderDotsPerMmX, caps.RenderDotsPerMmY)
                                     ?? throw new InvalidOperationException(L10n.T("Etiket görüntüsü üretilemedi."));
                        }
                        catch (OperationCanceledException)
                        {
                            report.Canceled = true;
                            return 0;
                        }
                        catch (Exception ex)
                        {
                            report.Errors.Add(L10n.T($"{pageIndex + 1}. etiket oluşturulamadı: {ex.Message}"));
                            return 0;
                        }
                        cachedIndex = pageIndex;
                        report.DataGenerated = true;
                        report.LabelsGenerated++;
                        report.BytesGenerated += cached.Data.Length;
                        LastPageInfo = caps with { BitmapWidth = cached.Width, BitmapHeight = cached.Height };
                    }

                    if (GdiNative.StartPage(hdc) <= 0)
                    {
                        report.Errors.Add(PrintEngineException.FromWin32(L10n.T("Sayfa başlatılamadı"), Marshal.GetLastPInvokeError(), printerName).Message);
                        return 0;
                    }
                    if (!DrawMono(hdc, cached, caps, out int drawError))
                    {
                        report.Errors.Add(PrintEngineException.FromWin32(L10n.T("Etiket görüntüsü sürücüye çizilemedi"), drawError, printerName).Message);
                        return 0;
                    }
                    if (GdiNative.EndPage(hdc) <= 0)
                    {
                        report.Errors.Add(PrintEngineException.FromWin32(L10n.T("Sayfa tamamlanamadı"), Marshal.GetLastPInvokeError(), printerName).Message);
                        return 0;
                    }
                    progress?.Report(new PrintProgress { Done = position + 1, Total = sequence.Count, Stage = L10n.T("Sürücüye gönderiliyor") });
                }

                docOpen = false;
                if (GdiNative.EndDoc(hdc) <= 0)
                {
                    report.Errors.Add(PrintEngineException.FromWin32(L10n.T("Yazdırma işi tamamlanamadı"), Marshal.GetLastPInvokeError(), printerName).Message);
                    return 0;
                }
                report.Spooled = true;
                report.LabelsSpooled = report.LabelsGenerated;
                return jobId;
            }
            finally
            {
                if (docOpen) GdiNative.AbortDoc(hdc);
            }
        }
        finally
        {
            GdiNative.DeleteDC(hdc);
        }
    }

    /// <summary>DEVMODE hazırlar, aygıt bağlamını açar ve aygıt bilgisini okur.</summary>
    private static bool TryCreateDc(PrintReport report, string printerName, LabelMedia media, out IntPtr hdc, out DriverPageInfo caps)
    {
        hdc = IntPtr.Zero;
        caps = new DriverPageInfo(0, 0, 0, 0, 0, 0);
        byte[]? devMode = BuildDevMode(report, printerName, media, out int paperId);
        if (devMode is null) return false;

        unsafe
        {
            fixed (byte* pDevMode = devMode)
                hdc = GdiNative.CreateDC(null, printerName, null, (IntPtr)pDevMode);
        }
        if (hdc == IntPtr.Zero)
        {
            report.Errors.Add(PrintEngineException.FromWin32(L10n.T("Yazıcı aygıt bağlamı oluşturulamadı"), Marshal.GetLastPInvokeError(), printerName).Message);
            return false;
        }

        caps = new DriverPageInfo(
            GdiNative.GetDeviceCaps(hdc, GdiNative.LOGPIXELSX),
            GdiNative.GetDeviceCaps(hdc, GdiNative.LOGPIXELSY),
            GdiNative.GetDeviceCaps(hdc, GdiNative.PHYSICALWIDTH),
            GdiNative.GetDeviceCaps(hdc, GdiNative.PHYSICALHEIGHT),
            GdiNative.GetDeviceCaps(hdc, GdiNative.PHYSICALOFFSETX),
            GdiNative.GetDeviceCaps(hdc, GdiNative.PHYSICALOFFSETY))
        {
            PaperId = paperId,
        };
        if (caps.DpiX <= 0 || caps.DpiY <= 0)
        {
            report.Errors.Add(L10n.T("Yazıcı sürücüsü çözünürlük bilgisi vermedi."));
            GdiNative.DeleteDC(hdc);
            hdc = IntPtr.Zero;
            return false;
        }

        // Sürücü sayfayı istenen ölçüde kurduysa nokta/mm fiziksel sayfadan alınır (203 DPI yazıcılarda 100 mm = 800 nokta).
        double dotsPerMmX = caps.DpiX / 25.4, dotsPerMmY = caps.DpiY / 25.4;
        if (caps.PageMatches(media))
        {
            double fx = caps.PhysicalWidth / media.WidthMm, fy = caps.PhysicalHeight / media.HeightMm;
            if (Math.Abs(fx / dotsPerMmX - 1) < 0.01) dotsPerMmX = fx;
            if (Math.Abs(fy / dotsPerMmY - 1) < 0.01) dotsPerMmY = fy;
        }
        caps = caps with { RenderDotsPerMmX = dotsPerMmX, RenderDotsPerMmY = dotsPerMmY };
        return true;
    }

    /// <summary>
    /// Sürücünün varsayılan DEVMODE'unu alır, kağıt boyutunu ve dikey yönü yazar, sonra sürücünün kendi özel bölümünü
    /// güncellemesi için DocumentProperties ile birleştirir. Ayarlar kalıcı olarak DEĞİŞTİRİLMEZ.
    /// </summary>
    private static byte[]? BuildDevMode(PrintReport report, string printerName, LabelMedia media, out int paperId)
    {
        paperId = FindMatchingPaper(printerName, media) ?? GdiNative.DMPAPER_USER;
        if (!NativeMethods.TryOpen(printerName, out var handle, out int openError))
        {
            report.Errors.Add(PrintEngineException.FromWin32(L10n.T("Yazıcı açılamadı"), openError, printerName).Message);
            return null;
        }
        using (handle)
        {
            int size = GdiNative.DocumentProperties(IntPtr.Zero, handle, printerName, IntPtr.Zero, IntPtr.Zero, 0);
            if (size < GdiNative.MinimumDevModeSize)
            {
                report.Errors.Add(PrintEngineException.FromWin32(L10n.T("Yazıcı sürücüsü ayarları okunamadı"), Marshal.GetLastPInvokeError(), printerName).Message);
                return null;
            }
            IntPtr input = Marshal.AllocHGlobal(size);
            IntPtr output = Marshal.AllocHGlobal(size);
            try
            {
                if (GdiNative.DocumentProperties(IntPtr.Zero, handle, printerName, input, IntPtr.Zero, GdiNative.DM_OUT_BUFFER) < 0)
                {
                    report.Errors.Add(PrintEngineException.FromWin32(L10n.T("Yazıcı sürücüsü ayarları okunamadı"), Marshal.GetLastPInvokeError(), printerName).Message);
                    return null;
                }
                ApplyPaper(input, media, (short)paperId);
                IntPtr result = input;
                if (GdiNative.DocumentProperties(IntPtr.Zero, handle, printerName, output, input,
                        GdiNative.DM_IN_BUFFER | GdiNative.DM_OUT_BUFFER) >= 0)
                {
                    result = output;
                }
                var bytes = new byte[size];
                Marshal.Copy(result, bytes, 0, size);
                return bytes;
            }
            finally
            {
                Marshal.FreeHGlobal(input);
                Marshal.FreeHGlobal(output);
            }
        }
    }

    /// <summary>Sürücünün kağıt listesinde ölçüsü etiketle ±0,5 mm eşleşen standart form (varsa kimliği).</summary>
    internal static int? FindMatchingPaper(string printerName, LabelMedia media)
    {
        int count = GdiNative.DeviceCapabilities(printerName, null, GdiNative.DC_PAPERS, IntPtr.Zero, IntPtr.Zero);
        if (count <= 0 || count > 4096) return null;
        IntPtr ids = Marshal.AllocHGlobal(count * sizeof(short));
        IntPtr sizes = Marshal.AllocHGlobal(count * 2 * sizeof(int));
        try
        {
            if (GdiNative.DeviceCapabilities(printerName, null, GdiNative.DC_PAPERS, ids, IntPtr.Zero) != count) return null;
            if (GdiNative.DeviceCapabilities(printerName, null, GdiNative.DC_PAPERSIZE, sizes, IntPtr.Zero) != count) return null;
            for (int i = 0; i < count; i++)
            {
                short id = Marshal.ReadInt16(ids, i * sizeof(short));
                if (id == GdiNative.DMPAPER_USER) continue; // genel "özel boyut" girişi
                double widthMm = Marshal.ReadInt32(sizes, i * 8) / 10.0;
                double heightMm = Marshal.ReadInt32(sizes, i * 8 + 4) / 10.0;
                if (Math.Abs(widthMm - media.WidthMm) <= 0.5 && Math.Abs(heightMm - media.HeightMm) <= 0.5) return id;
            }
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(ids);
            Marshal.FreeHGlobal(sizes);
        }
    }

    private static void ApplyPaper(IntPtr devMode, LabelMedia media, short paperId)
    {
        uint fields = (uint)Marshal.ReadInt32(devMode, GdiNative.OffsetDmFields);
        fields |= GdiNative.DM_ORIENTATION | GdiNative.DM_PAPERSIZE | GdiNative.DM_PAPERLENGTH | GdiNative.DM_PAPERWIDTH |
                  GdiNative.DM_SCALE | GdiNative.DM_COPIES;
        fields &= ~GdiNative.DM_FORMNAME; // form adı istenen boyutun önüne geçmesin
        Marshal.WriteInt32(devMode, GdiNative.OffsetDmFields, unchecked((int)fields));
        Marshal.WriteInt16(devMode, GdiNative.OffsetDmOrientation, GdiNative.DMORIENT_PORTRAIT);
        Marshal.WriteInt16(devMode, GdiNative.OffsetDmPaperSize, paperId);
        Marshal.WriteInt16(devMode, GdiNative.OffsetDmPaperLength, TenthsOfMm(media.HeightMm));
        Marshal.WriteInt16(devMode, GdiNative.OffsetDmPaperWidth, TenthsOfMm(media.WidthMm));
        Marshal.WriteInt16(devMode, GdiNative.OffsetDmScale, 100);
        Marshal.WriteInt16(devMode, GdiNative.OffsetDmCopies, 1);
    }

    internal static short TenthsOfMm(double mm) => (short)Math.Clamp(Math.Round(mm * 10.0, MidpointRounding.AwayFromZero), 1, short.MaxValue);

    /// <summary>
    /// 1-bit görüntüyü aygıta ölçeklemeden çizer: kaynak ve hedef dikdörtgen aynı boyuttadır.
    /// Bellek ve sürücü sınırları için 256 satırlık şeritler halinde, alttan üste DIB olarak gönderilir.
    /// Palet: 0 = beyaz, 1 = siyah (MonoBitmap kutupluluğu).
    /// </summary>
    private static unsafe bool DrawMono(IntPtr hdc, MonoBitmap mono, DriverPageInfo caps, out int error)
    {
        error = 0;
        int width = mono.Width;
        int dibStride = (width + 31) / 32 * 4;
        var band = new byte[dibStride * Math.Min(BandRows, mono.Height)];
        var info = new GdiNative.BITMAPINFO1
        {
            Header = new GdiNative.BITMAPINFOHEADER
            {
                biSize = sizeof(GdiNative.BITMAPINFOHEADER),
                biWidth = width,
                biPlanes = 1,
                biBitCount = 1,
                biCompression = GdiNative.BI_RGB,
                biXPelsPerMeter = (int)Math.Round(caps.RenderDotsPerMmX * 1000),
                biYPelsPerMeter = (int)Math.Round(caps.RenderDotsPerMmY * 1000),
                biClrUsed = 2,
                biClrImportant = 2,
            },
            Color0 = 0x00FFFFFF,
            Color1 = 0x00000000,
        };

        for (int y0 = 0; y0 < mono.Height; y0 += BandRows)
        {
            int rows = Math.Min(BandRows, mono.Height - y0);
            for (int r = 0; r < rows; r++)
            {
                int sourceRow = y0 + rows - 1 - r; // alttan üste DIB
                int target = r * dibStride;
                Buffer.BlockCopy(mono.Data, sourceRow * mono.Stride, band, target, mono.Stride);
                if (dibStride > mono.Stride) Array.Clear(band, target + mono.Stride, dibStride - mono.Stride);
            }
            info.Header.biHeight = rows;
            info.Header.biSizeImage = dibStride * rows;
            int copied;
            fixed (byte* bits = band)
            {
                copied = GdiNative.StretchDIBits(hdc, -caps.OffsetX, -caps.OffsetY + y0, width, rows,
                    0, 0, width, rows, bits, &info, GdiNative.DIB_RGB_COLORS, GdiNative.SRCCOPY);
            }
            if (copied == 0 || copied == -1)
            {
                error = Marshal.GetLastPInvokeError();
                return false;
            }
        }
        return true;
    }

    /// <summary>Sürücünün kurduğu sayfa istenenden saparsa veya yazdırılamaz kenar bildirirse uyarır.</summary>
    private static void AddPageWarnings(PrintReport report, LabelMedia media, DriverPageInfo caps)
    {
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        if (!caps.PageMatches(media))
        {
            report.Warnings.Add(
                L10n.T($"Sürücü sayfayı {caps.PhysicalWidthMm.ToString("0.#", tr)} × {caps.PhysicalHeightMm.ToString("0.#", tr)} mm olarak kurdu ") +
                L10n.T($"(istenen {media.WidthMm.ToString("0.#", tr)} × {media.HeightMm.ToString("0.#", tr)} mm). Sürücü bu özel kağıt boyutunu desteklemiyor; ") +
                L10n.T("yazıcı tercihlerinden etiket boyutunu ayarlayın veya TSPL baskı yöntemini kullanın. Görüntü yine de ölçeklenmeden basılır."));
        }
        if (caps.OffsetX > 0 || caps.OffsetY > 0)
        {
            double leftMm = caps.OffsetX * 25.4 / caps.DpiX, topMm = caps.OffsetY * 25.4 / caps.DpiY;
            report.Warnings.Add(
                L10n.T($"Sürücü kağıdın solundan {leftMm.ToString("0.#", tr)} mm ve üstünden {topMm.ToString("0.#", tr)} mm'yi yazdırılamaz alan olarak bildiriyor; ") +
                L10n.T("bu bölgedeki içerik basılmaz (TSPL yönteminde bu sınır yoktur)."));
        }
    }

    private static async Task<bool> WaitForFileAsync(string path, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        long lastSize = -1;
        while (DateTime.UtcNow < until)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 0)
                {
                    if (info.Length == lastSize) return true;
                    lastSize = info.Length;
                }
            }
            catch (IOException)
            {
            }
            await Task.Delay(250).ConfigureAwait(false);
        }
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }
}
