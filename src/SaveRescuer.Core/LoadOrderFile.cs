using System.Text;

namespace SaveRescuer.Core;

/// <summary>
/// Reads and writes plugins.txt while preserving its existing content: comments stay,
/// disabled entries keep their place, and only the requested lines are enabled or appended.
/// </summary>
public sealed class LoadOrderFile
{
    private readonly List<string> _lines;

    public string Path { get; }

    private LoadOrderFile(string path, List<string> lines)
    {
        Path = path;
        _lines = lines;
    }

    public static LoadOrderFile Load(string path) =>
        new(path, File.Exists(path)
            ? [.. File.ReadAllText(path, Encoding.UTF8).Split('\n').Select(l => l.TrimEnd('\r'))]
            : []);

    public IReadOnlyList<string> Lines => _lines;

    public IEnumerable<string> Enabled => _lines
        .Where(l => l.StartsWith('*'))
        .Select(l => l[1..].Trim())
        .Where(l => l.Length > 0);

    public bool IsEnabled(string plugin) =>
        Enabled.Any(e => e.Equals(plugin, StringComparison.OrdinalIgnoreCase));

    public bool IsListed(string plugin) => _lines
        .Select(l => l.TrimStart('*').Trim())
        .Any(l => l.Equals(plugin, StringComparison.OrdinalIgnoreCase));

    /// <summary>Enables a plugin: flips an existing disabled line, or appends a new one.</summary>
    /// <returns>true if the file changed.</returns>
    public bool Enable(string plugin)
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            var raw = _lines[i];
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            var name = trimmed.TrimStart('*').Trim();
            if (!name.Equals(plugin, StringComparison.OrdinalIgnoreCase)) continue;
            if (trimmed.StartsWith('*')) return false;      // already enabled

            _lines[i] = "*" + name;
            return true;
        }

        _lines.Add("*" + plugin);
        return true;
    }

    public bool Disable(string plugin)
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            var trimmed = _lines[i].Trim();
            if (!trimmed.StartsWith('*')) continue;
            if (trimmed[1..].Trim().Equals(plugin, StringComparison.OrdinalIgnoreCase))
            {
                _lines[i] = trimmed[1..].Trim();
                return true;
            }
        }
        return false;
    }

    public bool Remove(string plugin)
    {
        int removed = _lines.RemoveAll(l =>
            l.Trim().TrimStart('*').Trim().Equals(plugin, StringComparison.OrdinalIgnoreCase));
        return removed > 0;
    }

    /// <summary>Writes the file back with Windows line endings and no UTF-8 BOM.</summary>
    public void Save()
    {
        var text = string.Join("\r\n", _lines.Where((l, i) => l.Length > 0 || i < _lines.Count - 1));
        if (!text.EndsWith("\r\n")) text += "\r\n";
        File.WriteAllText(Path, text, new UTF8Encoding(false));
    }

    /// <summary>Copies the file next to itself with a timestamp, returning the backup path.</summary>
    public string Backup()
    {
        var backup = $"{Path}.rescuer-backup-{DateTime.Now:yyyyMMdd-HHmmss}";
        if (File.Exists(Path)) File.Copy(Path, backup, overwrite: true);
        return backup;
    }
}
