using System.Text;
using AkelEtiket.Core.Model;
using ZXing.Datamatrix.Encoder;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.BarcodeEngine;

/// <summary>
/// GS1 DataMatrix kodlayıcısı (ISO/IEC 16022, ECC 200). Veri ASCII kodlamasıyla yazılır: ilk kod sözcüğü FNC1 (232) —
/// okuyucu bunu "]d2" (GS1) olarak tanır —, değişken uzunluklu AI'lardan sonraki ayırıcılar da FNC1'dir; iki rakam tek
/// kod sözcüğüne sıkıştırılır. Hata düzeltme, sembol boyutu seçimi ve modül yerleşimi ZXing.Net'in standart
/// bileşenleriyle yapılır.
/// </summary>
/// <remarks>
/// ZXing.Net 0.16.11'in yazıcısı GS1'i yalnızca "compact" (MinimalEncoder) kipinde destekliyor ve bu kip bazı
/// içeriklerde (sonu büyük harfle biten veride) okununca sona fazladan karakter ("GR2u") çıkan semboller üretiyor.
/// Bu yüzden GS1 DataMatrix için kod sözcükleri burada, bilerek yalnızca ASCII kodlamasıyla oluşturulur.
/// </remarks>
internal static class Gs1DataMatrixEncoder
{
    /// <summary>Doğrulama metnindeki AI ayırıcısı (GS, 0x1D); sembolde FNC1 kod sözcüğü olur.</summary>
    public const char Separator = '\u001d';

    private const int Fnc1 = 232;
    private const int Pad = 129;

    /// <summary>
    /// GS ile ayrılmış GS1 öğe dizisini (ör. "0108691234567890" + "10ABC" + GS + "21X1") sembole çevirir.
    /// Sonuç, satır satır modül dizisidir (true = siyah); bulucu desenleri dahildir, sessiz bölge hariçtir.
    /// </summary>
    public static (int Width, int Height, bool[] Cells) Encode(string elementString, DataMatrixShape shape)
    {
        string codewords = BuildCodewords(elementString);
        var hint = shape switch
        {
            DataMatrixShape.Rectangle => SymbolShapeHint.FORCE_RECTANGLE,
            DataMatrixShape.Auto => SymbolShapeHint.FORCE_NONE,
            _ => SymbolShapeHint.FORCE_SQUARE,
        };
        var info = SymbolInfo.lookup(codewords.Length, hint, null, null, true)
                   ?? throw new ArgumentException(L10n.T("GS1 DataMatrix: içerik en büyük sembolün kapasitesini aşıyor."));
        string data = AddPadding(codewords, DataCapacity(info));
        string withEcc = ErrorCorrection.encodeECC200(data, info);
        var placement = new DefaultPlacement(withEcc, info.getSymbolDataWidth(), info.getSymbolDataHeight());
        placement.place();
        return Assemble(placement, info);
    }

    /// <summary>ASCII kodlama: FNC1 + veri (rakam çiftleri sıkıştırılmış), GS → FNC1.</summary>
    internal static string BuildCodewords(string elementString)
    {
        var sb = new StringBuilder(elementString.Length + 1);
        sb.Append((char)Fnc1);
        for (int i = 0; i < elementString.Length; i++)
        {
            char c = elementString[i];
            if (c == Separator)
            {
                sb.Append((char)Fnc1);
                continue;
            }
            if (char.IsAsciiDigit(c) && i + 1 < elementString.Length && char.IsAsciiDigit(elementString[i + 1]))
            {
                sb.Append((char)(130 + (c - '0') * 10 + (elementString[i + 1] - '0')));
                i++;
                continue;
            }
            if (c > 127)
                throw new ArgumentException(L10n.T("GS1 DataMatrix yalnızca GS1 karakter kümesindeki (ASCII) karakterleri taşır."));
            sb.Append((char)(c + 1));
        }
        return sb.ToString();
    }

    /// <summary>Kalan veri kapasitesini ISO/IEC 16022'deki gibi doldurur: önce 129, sonra 253-durumlu karıştırılmış dolgu.</summary>
    internal static string AddPadding(string codewords, int capacity)
    {
        var sb = new StringBuilder(codewords, capacity);
        if (sb.Length < capacity) sb.Append((char)Pad);
        while (sb.Length < capacity) sb.Append(Randomize253State(sb.Length + 1));
        return sb.ToString();
    }

    private static char Randomize253State(int codewordPosition)
    {
        int pseudoRandom = 149 * codewordPosition % 253 + 1;
        int value = Pad + pseudoRandom;
        return (char)(value <= 254 ? value : value - 254);
    }

    private static int DataCapacity(SymbolInfo info)
    {
        int total = 0;
        for (int block = 1; block <= info.getInterleavedBlockCount(); block++)
            total += info.getDataLengthForInterleavedBlock(block);
        return total;
    }

    /// <summary>Veri bölgelerini bulucu desenleriyle birleştirir (sol ve alt kenar dolu, üst ve sağ kenar kesikli).</summary>
    private static (int Width, int Height, bool[] Cells) Assemble(DefaultPlacement placement, SymbolInfo info)
    {
        int symbolWidth = info.getSymbolWidth(), symbolHeight = info.getSymbolHeight();
        int dataWidth = info.getSymbolDataWidth(), dataHeight = info.getSymbolDataHeight();
        var cells = new bool[symbolWidth * symbolHeight];
        void Set(int x, int y, bool black) => cells[y * symbolWidth + x] = black;

        int matrixY = 0;
        for (int y = 0; y < dataHeight; y++)
        {
            int matrixX;
            if (y % info.matrixHeight == 0)
            {
                // Üst kenar: dönüşümlü siyah/beyaz.
                for (matrixX = 0; matrixX < symbolWidth; matrixX++) Set(matrixX, matrixY, matrixX % 2 == 0);
                matrixY++;
            }
            matrixX = 0;
            for (int x = 0; x < dataWidth; x++)
            {
                // Sol kenar: dolu.
                if (x % info.matrixWidth == 0) Set(matrixX++, matrixY, true);
                Set(matrixX++, matrixY, placement.getBit(x, y));
                // Sağ kenar: dönüşümlü.
                if (x % info.matrixWidth == info.matrixWidth - 1) Set(matrixX++, matrixY, y % 2 == 0);
            }
            matrixY++;
            if (y % info.matrixHeight == info.matrixHeight - 1)
            {
                // Alt kenar: dolu.
                for (matrixX = 0; matrixX < symbolWidth; matrixX++) Set(matrixX, matrixY, true);
                matrixY++;
            }
        }
        return (symbolWidth, symbolHeight, cells);
    }
}
