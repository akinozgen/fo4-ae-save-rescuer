using SaveRescuer.Core;

namespace SaveRescuer.Tests;

public class SaveNamingTests
{
    [Theory]
    [InlineData("Save12_5A1D2E3F_566696F6C65747461_Sanctuary_003012_20260726_63_2.fos", 900,
                "Save900_5A1D2E3F_566696F6C65747461_Sanctuary_003012_20260726_63_2.fos")]
    [InlineData("Autosave2_5A1D2E3FM566696F6C65747461_Sanctuary_003012_20260726_63_2.fos", 901,
                "Save901_5A1D2E3F_566696F6C65747461_Sanctuary_003012_20260726_63_2.fos")]
    [InlineData("Save3_5A1D2E3FS566696F6C65747461_Sanctuary_003012_20260726_63_2.fos", 5,
                "Save5_5A1D2E3F_566696F6C65747461_Sanctuary_003012_20260726_63_2.fos")]
    public void Rewrites_an_old_name_into_the_current_shape_and_slot(string original, int slot, string expected)
    {
        Assert.Equal(expected, SaveNaming.ForSlot(original, slot));
    }

    [Fact]
    public void A_name_that_does_not_match_the_pattern_still_gets_a_slot()
    {
        Assert.Equal("Save900_something odd.fos", SaveNaming.ForSlot("something odd.fos", 900));
    }

    [Fact]
    public void Reads_the_survival_marker()
    {
        Assert.True(SaveNaming.IsSurvival("Save3_5A1D2E3FS5666_Sanctuary_1_2_3_2.fos"));
        Assert.False(SaveNaming.IsSurvival("Save3_5A1D2E3F_5666_Sanctuary_1_2_3_2.fos"));
        Assert.Equal("M", SaveNaming.Marker("Autosave1_5A1D2E3FM5666_Sanctuary_1_2_3_2.fos"));
    }

    [Fact]
    public void Next_free_slot_skips_the_numbers_already_on_disk()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rescuer-slots-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            foreach (var name in new[] { "Save900_a_b_c_1_2_3_2.fos", "Save901_a_b_c_1_2_3_2.fos", "Save12_a_b_c_1_2_3_2.fos" })
                File.WriteAllText(Path.Combine(folder, name), "");

            Assert.Equal(902, SaveNaming.NextFreeSlot(folder));
            Assert.Equal(13, SaveNaming.NextFreeSlot(folder, startAt: 12));
            Assert.Equal([901, 900, 12], SaveNaming.UsedSlots(folder).OrderByDescending(s => s));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Unique_path_steps_aside_when_the_name_is_taken()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rescuer-unique-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "Save900_x.fos"), "");
            Assert.Equal(Path.Combine(folder, "Save900_x (2).fos"), SaveNaming.Unique(folder, "Save900_x.fos"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

public class SaveVaultTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rescuer-vault-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _saves;
    private readonly SaveVault _vault;

    public SaveVaultTests()
    {
        _saves = Path.Combine(_root, "Saves");
        Directory.CreateDirectory(_saves);
        _vault = new SaveVault(Path.Combine(_root, "Vault"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string MakeSave(string name = "Save12_ABCDEF01_Nate_Sanctuary_1_2_3_2.fos", string character = "Nate") =>
        new SaveBuilder { PlayerName = character, Level = 30 }.WriteTo(_saves, name);

    [Fact]
    public void Backup_copies_the_file_and_records_who_it_belongs_to()
    {
        var entry = _vault.Backup(MakeSave(), "before rescue");

        Assert.True(File.Exists(Path.Combine(_vault.Root, entry.StoredName)));
        Assert.Equal("Nate", entry.PlayerName);
        Assert.Equal(30u, entry.Level);
        Assert.Equal("Sanctuary Hills", entry.Location);
        Assert.Equal("before rescue", entry.Reason);
        Assert.Single(_vault.Entries());
    }

    [Fact]
    public void Backing_the_same_content_up_twice_stores_it_once()
    {
        var path = MakeSave();
        var first = _vault.Backup(path);
        var second = _vault.Backup(path);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(_vault.Entries());
    }

    [Fact]
    public void Restore_puts_the_file_back_where_it_came_from()
    {
        var path = MakeSave();
        var entry = _vault.Backup(path);
        File.Delete(path);

        var restored = _vault.Restore(entry);

        Assert.Equal(path, restored);
        Assert.Equal(entry.Sha256, SaveVault.HashOf(restored));
    }

    [Fact]
    public void Restore_does_not_overwrite_an_occupied_target_unless_told_to()
    {
        var path = MakeSave();
        var entry = _vault.Backup(path);
        File.WriteAllText(path, "something else");

        var restored = _vault.Restore(entry);
        Assert.NotEqual(path, restored);
        Assert.Equal("something else", File.ReadAllText(path));

        var overwritten = _vault.Restore(entry, overwrite: true);
        Assert.Equal(path, overwritten);
        Assert.Equal(entry.Sha256, SaveVault.HashOf(path));
    }

    [Fact]
    public void Restore_can_send_the_copy_to_another_folder()
    {
        var entry = _vault.Backup(MakeSave());
        var elsewhere = Path.Combine(_root, "Elsewhere");

        var restored = _vault.Restore(entry, elsewhere);

        Assert.Equal(Path.Combine(elsewhere, entry.OriginalName), restored);
    }

    [Fact]
    public void Forget_removes_the_entry_and_its_stored_copy()
    {
        var entry = _vault.Backup(MakeSave());
        _vault.Forget(entry);

        Assert.Empty(_vault.Entries());
        Assert.False(File.Exists(Path.Combine(_vault.Root, entry.StoredName)));
    }

    [Fact]
    public void Prune_drops_entries_whose_file_disappeared()
    {
        var entry = _vault.Backup(MakeSave());
        File.Delete(Path.Combine(_vault.Root, entry.StoredName));

        Assert.Equal(1, _vault.Prune());
        Assert.Empty(_vault.Entries());
    }

    [Fact]
    public void A_damaged_index_reads_as_empty_rather_than_throwing()
    {
        _vault.Backup(MakeSave());
        File.WriteAllText(_vault.IndexPath, "{ not json");
        Assert.Empty(_vault.Entries());
    }
}

public class RescueWorkflowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rescuer-flow-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _saves;
    private readonly SaveVault _vault;
    private readonly FakeDataFolder _data = new("KeeperMod.esp", "KeeperLight.esl");

    public RescueWorkflowTests()
    {
        _saves = Path.Combine(_root, "Saves");
        Directory.CreateDirectory(_saves);
        _vault = new SaveVault(Path.Combine(_root, "Vault"));
    }

    public void Dispose()
    {
        _data.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private string MakeSave(string name = "Save12_ABCDEF01_5661_Sanctuary_003012_20260726_63_2.fos")
    {
        var builder = SaveBuilder.WithMixedContent();
        builder.PlayerName = "Violetta";
        return builder.WriteTo(_saves, name);
    }

    private RescueRequest Request(string source, int? slot = 900) => new()
    {
        SourcePath = source,
        OutputFolder = _saves,
        DataFolder = _data.Path,
        Slot = slot,
        Options = new SurgeryOptions { Prefix = "[RESCUED] " }
    };

    [Fact]
    public void Diagnose_reports_what_the_save_is_missing()
    {
        var analysis = RescueWorkflow.Diagnose(MakeSave(), _data.Path);

        Assert.Equal(2, analysis.Plugins.MissingCount);
        Assert.Equal(RescueOutlook.Good, analysis.Outlook);
        Assert.Contains("change forms", analysis.OutlookNote);
    }

    [Fact]
    public void Rescue_writes_an_independent_save_into_a_manual_slot_and_keeps_the_original()
    {
        var source = MakeSave();
        var before = SaveVault.HashOf(source);

        var report = RescueWorkflow.Rescue(Request(source), _vault);

        Assert.True(report.Succeeded, report.Error);
        Assert.StartsWith("Save900_", report.OutputName);
        Assert.True(File.Exists(report.OutputPath));

        var rescued = SaveFile.Load(report.OutputPath!);
        Assert.Equal("[RESCUED] Sanctuary Hills", rescued.Location);
        Assert.Equal("Violetta", rescued.PlayerName);
        Assert.Equal(0, PluginInventory.Build(rescued, _data.Path).MissingCount);
        Assert.Equal(before, SaveVault.HashOf(source));   // the original is untouched
    }

    [Fact]
    public void Rescue_backs_the_original_up_first()
    {
        var report = RescueWorkflow.Rescue(Request(MakeSave()), _vault);

        Assert.NotNull(report.Backup);
        Assert.Equal("before rescue", report.Backup!.Reason);
        Assert.True(File.Exists(Path.Combine(_vault.Root, report.Backup.StoredName)));
    }

    [Fact]
    public void Rescue_can_skip_the_backup_when_asked()
    {
        var request = Request(MakeSave()) with { BackupFirst = false };
        var report = RescueWorkflow.Rescue(request, _vault);

        Assert.True(report.Succeeded, report.Error);
        Assert.Null(report.Backup);
        Assert.Empty(_vault.Entries());
    }

    [Fact]
    public void Rescue_picks_a_free_slot_when_none_is_given()
    {
        var first = RescueWorkflow.Rescue(Request(MakeSave("Save1_ABCDEF01_5661_a_1_2_3_2.fos"), slot: null), _vault);
        var second = RescueWorkflow.Rescue(Request(MakeSave("Save2_ABCDEF01_5661_b_1_2_3_2.fos"), slot: null), _vault);

        Assert.True(first.Succeeded, first.Error);
        Assert.True(second.Succeeded, second.Error);
        Assert.NotEqual(SaveNaming.SlotOf(first.OutputName!), SaveNaming.SlotOf(second.OutputName!));
    }

    [Fact]
    public void Rescue_reports_a_damaged_save_without_writing_anything()
    {
        var path = Path.Combine(_saves, "broken.fos");
        File.WriteAllBytes(path, new byte[256]);

        var report = RescueWorkflow.Rescue(Request(path), _vault);

        Assert.False(report.Succeeded);
        Assert.NotNull(report.Error);
        Assert.Single(Directory.GetFiles(_saves));   // only the broken input
    }

    [Fact]
    public void Rescue_leaves_the_folder_alone_when_the_change_forms_do_not_read_cleanly()
    {
        var source = MakeSave();
        var bytes = File.ReadAllBytes(source);
        var save = SaveFile.FromBytes(bytes);
        save.ChangeFormCount += 5;                  // claim records that are not there
        File.WriteAllBytes(source, save.Rebuild());

        var report = RescueWorkflow.Rescue(Request(source), _vault);

        Assert.False(report.Succeeded);
        Assert.Single(Directory.GetFiles(_saves));
    }

    [Fact]
    public void Relabel_writes_a_tagged_copy_with_its_contents_intact()
    {
        var report = RescueWorkflow.Relabel(MakeSave(), _saves, "[OG] ", slot: 800);

        Assert.True(report.Succeeded, report.Error);
        var copy = SaveFile.Load(report.OutputPath!);
        Assert.Equal("[OG] Sanctuary Hills", copy.Location);
        Assert.Equal(3, copy.Plugins.Count);        // nothing removed
    }
}
