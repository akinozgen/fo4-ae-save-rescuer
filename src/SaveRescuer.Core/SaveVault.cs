using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaveRescuer.Core;

/// <summary>One kept copy of a save file.</summary>
public sealed record VaultEntry
{
    public required string Id { get; init; }
    /// <summary>File name inside the vault folder.</summary>
    public required string StoredName { get; init; }
    /// <summary>Where the file was when it was taken in, so it can go back there.</summary>
    public required string OriginalPath { get; init; }
    public required string OriginalName { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required long Bytes { get; init; }
    public required string Sha256 { get; init; }
    public string PlayerName { get; init; } = "";
    public uint Level { get; init; }
    public string Location { get; init; } = "";
    /// <summary>Why the copy was made, shown in the backup list.</summary>
    public string Reason { get; init; } = "";

    [JsonIgnore] public double MegaBytes => Bytes / 1024.0 / 1024.0;
}

/// <summary>
/// Keeps original saves outside the game's own folder before anything is written.
///
/// The default location is under LocalAppData on purpose: the game's save folder lives in
/// Documents, which Steam Cloud syncs, and copies there would be pushed to the cloud and can be
/// overwritten by the game's own slot rotation.
/// </summary>
public sealed class SaveVault
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Root { get; }
    public string IndexPath => Path.Combine(Root, "index.json");

    public SaveVault(string? root = null)
    {
        Root = root ?? DefaultRoot;
        Directory.CreateDirectory(Root);
    }

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FO4SaveRescuer", "Backups");

    public List<VaultEntry> Entries()
    {
        if (!File.Exists(IndexPath)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<VaultEntry>>(File.ReadAllText(IndexPath)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public long TotalBytes() => Entries().Sum(e => e.Bytes);

    /// <summary>Copies a save into the vault. Identical content already stored is not duplicated.</summary>
    public VaultEntry Backup(string savePath, string reason = "")
    {
        if (!File.Exists(savePath)) throw new FileNotFoundException("Save file not found.", savePath);

        var hash = HashOf(savePath);
        var existing = Entries().FirstOrDefault(
            e => e.Sha256 == hash && File.Exists(Path.Combine(Root, e.StoredName)));
        if (existing is not null) return existing;

        var summary = SaveSummary.Read(savePath);
        var id = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var storedName = $"{id}_{Path.GetFileName(savePath)}";
        File.Copy(savePath, Path.Combine(Root, storedName), overwrite: false);

        var entry = new VaultEntry
        {
            Id = id,
            StoredName = storedName,
            OriginalPath = savePath,
            OriginalName = Path.GetFileName(savePath),
            CreatedAt = DateTime.Now,
            Bytes = new FileInfo(savePath).Length,
            Sha256 = hash,
            PlayerName = summary.PlayerName,
            Level = summary.Level,
            Location = summary.Location,
            Reason = reason
        };

        var all = Entries();
        all.Insert(0, entry);
        Save(all);
        return entry;
    }

    /// <summary>
    /// Puts a stored copy back. Without <paramref name="overwrite"/> an occupied target is left
    /// alone and the copy lands next to it under a free name.
    /// </summary>
    public string Restore(VaultEntry entry, string? targetFolder = null, bool overwrite = false)
    {
        var stored = Path.Combine(Root, entry.StoredName);
        if (!File.Exists(stored))
            throw new FileNotFoundException($"The stored copy of {entry.OriginalName} is gone.", stored);

        var folder = targetFolder
                     ?? Path.GetDirectoryName(entry.OriginalPath)
                     ?? throw new InvalidOperationException("No folder to restore into.");
        Directory.CreateDirectory(folder);

        var target = Path.Combine(folder, entry.OriginalName);
        if (File.Exists(target) && !overwrite) target = SaveNaming.Unique(folder, entry.OriginalName);
        File.Copy(stored, target, overwrite);
        return target;
    }

    public void Forget(VaultEntry entry, bool deleteFile = true)
    {
        if (deleteFile)
        {
            var stored = Path.Combine(Root, entry.StoredName);
            if (File.Exists(stored)) File.Delete(stored);
        }
        Save(Entries().Where(e => e.Id != entry.Id).ToList());
    }

    /// <summary>Drops index entries whose file has disappeared from the vault folder.</summary>
    public int Prune()
    {
        var all = Entries();
        var alive = all.Where(e => File.Exists(Path.Combine(Root, e.StoredName))).ToList();
        if (alive.Count != all.Count) Save(alive);
        return all.Count - alive.Count;
    }

    private void Save(List<VaultEntry> entries)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(entries, Json));
    }

    public static string HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
