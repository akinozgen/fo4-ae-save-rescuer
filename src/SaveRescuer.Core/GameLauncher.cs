using System.Diagnostics;

namespace SaveRescuer.Core;

/// <summary>One way of starting the game, as found in the game folder.</summary>
public sealed record LaunchOption(string DisplayName, string ExecutablePath, bool IsScriptExtender)
{
    public string FileName => Path.GetFileName(ExecutablePath);
}

/// <summary>
/// Finds the executables that can start Fallout 4 and launches them with the game folder as
/// working directory. The script extender is preferred when it is installed.
/// </summary>
public static class GameLauncher
{
    /// <summary>The game folder itself, derived from the Data folder.</summary>
    public static string? GameRoot(string? dataFolder) =>
        dataFolder is null ? null : Path.GetDirectoryName(dataFolder.TrimEnd(Path.DirectorySeparatorChar));

    /// <summary>Launch options present in the game folder, script extender first.</summary>
    public static List<LaunchOption> Discover(string? dataFolder)
    {
        var options = new List<LaunchOption>();
        var root = GameRoot(dataFolder);
        if (root is null || !Directory.Exists(root)) return options;

        void Add(string file, string label, bool se)
        {
            var full = Path.Combine(root, file);
            if (File.Exists(full)) options.Add(new LaunchOption(label, full, se));
        }

        Add("f4se_loader.exe", "Fallout 4 Script Extender", true);
        Add("Fallout4Launcher.exe", "Fallout4Launcher", false);
        Add("Fallout4.exe", "Fallout4 (direct)", false);
        return options;
    }

    /// <summary>Script extender when available, otherwise the standard launcher.</summary>
    public static LaunchOption? Preferred(string? dataFolder)
    {
        var options = Discover(dataFolder);
        return options.FirstOrDefault(o => o.IsScriptExtender) ?? options.FirstOrDefault();
    }

    /// <summary>The executable whose icon represents the game (the launcher artwork).</summary>
    public static string? IconSource(string? dataFolder)
    {
        var root = GameRoot(dataFolder);
        if (root is null) return null;
        foreach (var file in new[] { "Fallout4Launcher.exe", "Fallout4.exe" })
        {
            var full = Path.Combine(root, file);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    public static Process Launch(LaunchOption option)
    {
        var root = Path.GetDirectoryName(option.ExecutablePath)!;
        return Process.Start(new ProcessStartInfo
        {
            FileName = option.ExecutablePath,
            WorkingDirectory = root,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException($"Could not start {option.FileName}.");
    }
}
