using System.Text;

namespace SaveRescuer.Core;

/// <summary>
/// Creates and recognises placeholder plugins: valid but empty plugin files that occupy
/// the load-order slot a save expects, so the game stops reporting missing content.
/// </summary>
public static class StubPlugin
{
    /// <summary>Marker written into the stub's author field so the tool can recognise its own work.</summary>
    public const string AuthorTag = "FO4SaveRescuer";

    private const int EslFlag = 0x200;

    /// <summary>A stub is tiny; anything larger is real content and must never be treated as a stub.</summary>
    private const long MaxStubSize = 512;

    public static byte[] Build(bool light)
    {
        using var body = new MemoryStream();
        using var w = new BinaryWriter(body, Encoding.Latin1);

        // HEDR: version, record count (0), next available object id
        Sub(w, "HEDR", bw =>
        {
            bw.Write(1.0f);
            bw.Write(0);
            bw.Write(0x800);
        });
        Sub(w, "CNAM", bw => bw.Write(Zero(AuthorTag)));
        Sub(w, "SNAM", bw => bw.Write(Zero("Placeholder created to satisfy a save file. Contains no records.")));
        Sub(w, "MAST", bw => bw.Write(Zero("Fallout4.esm")));
        Sub(w, "DATA", bw => bw.Write(new byte[8]));

        var payload = body.ToArray();

        using var rec = new MemoryStream();
        using var rw = new BinaryWriter(rec, Encoding.Latin1);
        rw.Write(Encoding.ASCII.GetBytes("TES4"));
        rw.Write(payload.Length);
        rw.Write(light ? EslFlag : 0);
        rw.Write(new byte[12]);          // form id, revision, version fields
        rw.Write(payload);
        return rec.ToArray();
    }

    public static void Write(string path, bool light) => File.WriteAllBytes(path, Build(light));

    /// <summary>True when the file is an empty placeholder created by this tool.</summary>
    public static bool IsStub(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxStubSize) return false;

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 24) return false;
            if (Encoding.ASCII.GetString(bytes, 0, 4) != "TES4") return false;

            return Encoding.Latin1.GetString(bytes).Contains(AuthorTag, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private static void Sub(BinaryWriter w, string tag, Action<BinaryWriter> writeData)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.Latin1);
        writeData(bw);
        var data = ms.ToArray();
        w.Write(Encoding.ASCII.GetBytes(tag));
        w.Write((ushort)data.Length);
        w.Write(data);
    }

    private static byte[] Zero(string s) => [.. Encoding.Latin1.GetBytes(s), 0];
}
