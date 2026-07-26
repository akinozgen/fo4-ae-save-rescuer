namespace SaveRescuer.Core;

public sealed record RescueRequest
{
    public required string SourcePath { get; init; }
    /// <summary>Where the rescued file goes; the save folder unless the caller says otherwise.</summary>
    public required string OutputFolder { get; init; }
    public SurgeryOptions Options { get; init; } = new();
    /// <summary>Copy the original into the vault before writing anything.</summary>
    public bool BackupFirst { get; init; } = true;
    /// <summary>Manual slot to write into; the next free one when left unset.</summary>
    public int? Slot { get; init; }
    /// <summary>Data folder used to decide what is still installed.</summary>
    public string? DataFolder { get; init; }
}

public sealed record RescueReport
{
    public required string SourcePath { get; init; }
    public required bool Succeeded { get; init; }
    public string? OutputPath { get; init; }
    public SurgeryOutcome? Outcome { get; init; }
    public VaultEntry? Backup { get; init; }
    public SaveAnalysis? Analysis { get; init; }
    public string? Error { get; init; }

    public string SourceName => Path.GetFileName(SourcePath);
    public string? OutputName => OutputPath is null ? null : Path.GetFileName(OutputPath);
}

/// <summary>
/// One rescue from start to finish: read, diagnose, back up, operate, write into a manual slot.
/// The original file is never written to.
/// </summary>
public static class RescueWorkflow
{
    public static SaveAnalysis Diagnose(string savePath, string? dataFolder, bool vanillaOnly = false)
    {
        var save = SaveFile.Load(savePath);
        return SaveAnalysis.Run(save, PluginInventory.Build(save, dataFolder, vanillaOnly));
    }

    public static RescueReport Rescue(RescueRequest request, SaveVault? vault = null)
    {
        VaultEntry? backup = null;
        try
        {
            var save = SaveFile.Load(request.SourcePath);
            var plugins = PluginInventory.Build(save, request.DataFolder, request.Options.VanillaOnly);
            var analysis = SaveAnalysis.Run(save, plugins);

            if (!analysis.IsIntact)
                return Failure(request, backup, analysis,
                    "The save does not read cleanly - its sections are inconsistent, so nothing was written.");

            if (request.BackupFirst)
                backup = (vault ?? new SaveVault()).Backup(request.SourcePath, "before rescue");

            var outcome = SaveSurgeon.Operate(save, plugins, request.Options);
            if (!outcome.Passed)
                return Failure(request, backup, analysis,
                    "The result failed its own checks: " +
                    string.Join("; ", outcome.Checks.Where(c => !c.Passed).Select(c => $"{c.Name} - {c.Detail}")));

            Directory.CreateDirectory(request.OutputFolder);
            var slot = request.Slot ?? SaveNaming.NextFreeSlot(request.OutputFolder);
            var outputPath = SaveNaming.Unique(
                request.OutputFolder, SaveNaming.ForSlot(Path.GetFileName(request.SourcePath), slot));
            File.WriteAllBytes(outputPath, outcome.Bytes);

            return new RescueReport
            {
                SourcePath = request.SourcePath,
                Succeeded = true,
                OutputPath = outputPath,
                Outcome = outcome,
                Backup = backup,
                Analysis = analysis
            };
        }
        catch (Exception ex)
        {
            return Failure(request, backup, null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Writes a copy whose in-game name carries a tag, with the contents left alone.</summary>
    public static RescueReport Relabel(string savePath, string outputFolder, string prefix, int? slot = null)
    {
        try
        {
            var save = SaveFile.Load(savePath);
            var bytes = SaveSurgeon.Relabel(save, prefix);

            Directory.CreateDirectory(outputFolder);
            var target = SaveNaming.Unique(outputFolder, SaveNaming.ForSlot(
                Path.GetFileName(savePath), slot ?? SaveNaming.NextFreeSlot(outputFolder)));
            File.WriteAllBytes(target, bytes);

            return new RescueReport { SourcePath = savePath, Succeeded = true, OutputPath = target };
        }
        catch (Exception ex)
        {
            return new RescueReport
            {
                SourcePath = savePath,
                Succeeded = false,
                Error = $"{ex.GetType().Name}: {ex.Message}"
            };
        }
    }

    private static RescueReport Failure(RescueRequest request, VaultEntry? backup, SaveAnalysis? analysis, string error) =>
        new()
        {
            SourcePath = request.SourcePath,
            Succeeded = false,
            Backup = backup,
            Analysis = analysis,
            Error = error
        };
}
