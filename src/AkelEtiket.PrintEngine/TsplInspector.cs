using System.Globalization;
using System.Text;

namespace AkelEtiket.PrintEngine;

/// <summary>Ayrıştırılmış tek TSPL komutu.</summary>
/// <param name="Name">Komut adı (büyük harf), ör. "SIZE", "BITMAP".</param>
/// <param name="Arguments">Komut parametreleri (BITMAP için ikili veri hariç "X,Y,genişlik,yükseklik,mod").</param>
/// <param name="PayloadLength">BITMAP ikili veri uzunluğu (bayt); diğer komutlarda 0.</param>
/// <param name="Offset">Komutun veri akışındaki başlangıç konumu.</param>
public sealed record TsplCommand(string Name, string Arguments, int PayloadLength, int Offset)
{
    /// <summary>İstenirse BITMAP verisinin kopyası.</summary>
    public byte[]? Payload { get; init; }

    public override string ToString() =>
        PayloadLength > 0 ? $"{Name} {Arguments},<{PayloadLength.ToString("N0", CultureInfo.GetCultureInfo("tr-TR"))} bayt görüntü verisi>"
        : Arguments.Length > 0 ? $"{Name} {Arguments}" : Name;
}

/// <summary>
/// TSPL veri akışını komutlara ayırır (tanılama ve testler için). BITMAP ikili verisi
/// genişlik×yükseklik bayt olarak atlanır; veri içinde CR/LF baytı bulunsa bile ayrıştırma bozulmaz.
/// </summary>
public static class TsplInspector
{
    private static readonly byte[] BitmapPrefix = "BITMAP "u8.ToArray();

    public static IReadOnlyList<TsplCommand> Parse(ReadOnlySpan<byte> data, bool includePayload = false)
    {
        var commands = new List<TsplCommand>();
        int pos = 0;
        while (pos < data.Length)
        {
            byte b = data[pos];
            if (b is (byte)'\r' or (byte)'\n' or (byte)' ' or (byte)'\t')
            {
                pos++;
                continue;
            }

            if (data[pos..].StartsWith(BitmapPrefix))
            {
                pos = ParseBitmap(data, pos, includePayload, commands);
                continue;
            }

            int end = pos;
            while (end < data.Length && data[end] != (byte)'\r' && data[end] != (byte)'\n') end++;
            string line = Encoding.ASCII.GetString(data[pos..end]).Trim();
            int space = line.IndexOf(' ');
            string name = (space < 0 ? line : line[..space]).ToUpperInvariant();
            string args = space < 0 ? "" : line[(space + 1)..].Trim();
            commands.Add(new TsplCommand(name, args, 0, pos));
            pos = end;
        }
        return commands;
    }

    /// <summary>Komutları okunabilir metin olarak listeler (görüntü verisi özetlenir).</summary>
    public static string Describe(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder();
        foreach (var command in Parse(data)) sb.AppendLine(command.ToString());
        return sb.ToString();
    }

    private static int ParseBitmap(ReadOnlySpan<byte> data, int start, bool includePayload, List<TsplCommand> commands)
    {
        int p = start + BitmapPrefix.Length;
        int headerStart = p;
        int commas = 0;
        while (p < data.Length && commas < 5)
        {
            byte c = data[p];
            if (c == (byte)',') commas++;
            else if (c is (byte)'\r' or (byte)'\n')
                throw new FormatException("BITMAP başlığı eksik: 5 parametre bekleniyordu.");
            p++;
        }
        if (commas < 5) throw new FormatException("BITMAP başlığı eksik: 5 parametre bekleniyordu.");

        string header = Encoding.ASCII.GetString(data[headerStart..(p - 1)]);
        string[] parts = header.Split(',');
        if (parts.Length != 5 ||
            !int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int widthBytes) ||
            !int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int heightDots) ||
            widthBytes <= 0 || heightDots <= 0)
        {
            throw new FormatException($"BITMAP başlığı geçersiz: \"{header}\".");
        }

        long length = (long)widthBytes * heightDots;
        if (p + length > data.Length)
            throw new FormatException($"BITMAP verisi eksik: {length} bayt bekleniyordu, {data.Length - p} bayt var.");

        byte[]? payload = includePayload ? data.Slice(p, (int)length).ToArray() : null;
        commands.Add(new TsplCommand("BITMAP", header, (int)length, start) { Payload = payload });
        p += (int)length;
        if (p < data.Length && data[p] == (byte)'\r') p++;
        if (p < data.Length && data[p] == (byte)'\n') p++;
        return p;
    }
}
