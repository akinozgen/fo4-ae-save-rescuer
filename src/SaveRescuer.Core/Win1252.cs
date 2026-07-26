namespace SaveRescuer.Core;

/// <summary>
/// Windows-1252 codec. The game writes strings in this code page, and .NET does not ship it
/// outside the CodePages package - so it lives here to keep the tool dependency free.
/// Every byte value maps to a distinct character, which makes decode/encode a lossless round trip.
/// </summary>
public static class Win1252
{
    /// <summary>The 0x80-0x9F glyphs; the five values Windows-1252 leaves undefined are absent.</summary>
    private static readonly (byte Byte, char Char)[] HighRange =
    [
        (0x80, '€'), (0x82, '‚'), (0x83, 'ƒ'), (0x84, '„'), (0x85, '…'),
        (0x86, '†'), (0x87, '‡'), (0x88, 'ˆ'), (0x89, '‰'), (0x8A, 'Š'),
        (0x8B, '‹'), (0x8C, 'Œ'), (0x8E, 'Ž'), (0x91, '‘'), (0x92, '’'),
        (0x93, '“'), (0x94, '”'), (0x95, '•'), (0x96, '–'), (0x97, '—'),
        (0x98, '˜'), (0x99, '™'), (0x9A, 'š'), (0x9B, '›'), (0x9C, 'œ'),
        (0x9E, 'ž'), (0x9F, 'Ÿ')
    ];

    private static readonly char[] Table = BuildTable();
    private static readonly Dictionary<char, byte> Reverse = BuildReverse();

    private static char[] BuildTable()
    {
        // Latin-1 for everything except 0x80-0x9F. The undefined values keep their Latin-1
        // character so that no byte loses its identity on the way back.
        var table = new char[256];
        for (var i = 0; i < 256; i++) table[i] = (char)i;
        foreach (var (b, c) in HighRange) table[b] = c;
        return table;
    }

    private static Dictionary<char, byte> BuildReverse()
    {
        var map = new Dictionary<char, byte>(256);
        for (var i = 0; i < 256; i++) map[Table[i]] = (byte)i;
        return map;
    }

    public static string GetString(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++) chars[i] = Table[bytes[i]];
        return new string(chars);
    }

    /// <summary>Characters outside the code page become '?', mirroring the game's own behaviour.</summary>
    public static byte[] GetBytes(string text)
    {
        var bytes = new byte[text.Length];
        for (var i = 0; i < text.Length; i++)
            bytes[i] = Reverse.TryGetValue(text[i], out var b) ? b : (byte)'?';
        return bytes;
    }
}
