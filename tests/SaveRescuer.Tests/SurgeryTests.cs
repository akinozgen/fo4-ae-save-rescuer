using SaveRescuer.Core;

namespace SaveRescuer.Tests;

/// <summary>A Data folder holding only the plugin files the test wants to be installed.</summary>
public sealed class FakeDataFolder : IDisposable
{
    public string Path { get; }

    public FakeDataFolder(params string[] installedPlugins)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "rescuer-data-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path);
        foreach (var name in installedPlugins)
            File.WriteAllText(System.IO.Path.Combine(Path, name), "");
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}

public class PluginInventoryTests
{
    [Fact]
    public void A_plugin_whose_file_is_gone_counts_as_missing()
    {
        using var data = new FakeDataFolder("KeeperMod.esp", "KeeperLight.esl");
        var save = SaveFile.FromBytes(SaveBuilder.WithMixedContent().Build());

        var inventory = PluginInventory.Build(save, data.Path);

        Assert.Equal(PluginState.BaseGame, inventory.Entries[0].State);   // Fallout4.esm
        Assert.Equal(PluginState.Missing, inventory.Entries[1].State);    // GhostMod.esp
        Assert.Equal(PluginState.Installed, inventory.Entries[2].State);  // KeeperMod.esp
        Assert.Equal([1], inventory.MissingRegular);
        Assert.Equal([0], inventory.MissingLight);                       // GhostLight.esl
        Assert.Equal(2, inventory.MissingCount);
    }

    [Fact]
    public void Vanilla_mode_treats_every_mod_as_missing_even_when_installed()
    {
        using var data = new FakeDataFolder("GhostMod.esp", "KeeperMod.esp", "GhostLight.esl", "KeeperLight.esl");
        var save = SaveFile.FromBytes(SaveBuilder.WithMixedContent().Build());

        var inventory = PluginInventory.Build(save, data.Path, vanillaOnly: true);

        Assert.Equal(4, inventory.MissingCount);
        Assert.Equal(PluginState.BaseGame, inventory.Entries[0].State);
    }

    [Fact]
    public void An_unreachable_data_folder_leaves_every_mod_missing()
    {
        var save = SaveFile.FromBytes(SaveBuilder.WithMixedContent().Build());
        Assert.Equal(4, PluginInventory.Build(save, @"Z:\no\such\folder").MissingCount);
    }

    [Fact]
    public void Base_game_names_are_matched_regardless_of_case()
    {
        var save = SaveFile.FromBytes(new SaveBuilder { Plugins = ["FALLOUT4.ESM", "dlccoast.esm"] }.Build());
        using var data = new FakeDataFolder();
        Assert.Equal(0, PluginInventory.Build(save, data.Path).MissingCount);
    }
}

public class ChangeFormTests
{
    [Fact]
    public void Walks_every_record_regardless_of_length_field_width()
    {
        var builder = new SaveBuilder();
        builder.ChangeForms.Add(SaveBuilder.ChangeForm(0, 5, [1, 2, 3], lengthKind: 0));
        builder.ChangeForms.Add(SaveBuilder.ChangeForm(1, 6, new byte[400], lengthKind: 1));
        builder.ChangeForms.Add(SaveBuilder.ChangeForm(2, 7, new byte[70_000], lengthKind: 2));

        var save = SaveFile.FromBytes(builder.Build());
        var walked = ChangeForms.Walk(save.ChangeForms, save.ChangeFormCount).ToList();

        Assert.Equal(3, walked.Count);
        Assert.Equal([0, 1, 2], walked.Select(w => w.RefType));
        Assert.Equal([5, 6, 7], walked.Select(w => w.RefValue));
        Assert.Equal(save.ChangeForms.Length, walked[^1].End);   // the walk covers the section exactly
    }

    [Fact]
    public void Stops_instead_of_reading_past_a_truncated_section()
    {
        var builder = new SaveBuilder();
        builder.ChangeForms.Add(SaveBuilder.ChangeForm(0, 1, [1, 2, 3]));
        var save = SaveFile.FromBytes(builder.Build());

        Assert.Empty(ChangeForms.Walk(save.ChangeForms[..5], save.ChangeFormCount));
    }

    [Theory]
    [InlineData(0x00001234u, 3, 2, FormOwnerKind.Regular, 0)]
    [InlineData(0x02001234u, 3, 2, FormOwnerKind.Regular, 2)]
    [InlineData(0x07001234u, 3, 2, FormOwnerKind.OutOfRange, 7)]
    [InlineData(0xFE000123u, 3, 2, FormOwnerKind.Light, 0)]
    [InlineData(0xFE001123u, 3, 2, FormOwnerKind.Light, 1)]
    [InlineData(0xFE00F123u, 3, 2, FormOwnerKind.OutOfRange, 0xF)]
    [InlineData(0xFF001234u, 3, 2, FormOwnerKind.Created, -1)]
    public void Reads_the_owning_plugin_out_of_a_form_id(
        uint formId, int regular, int light, FormOwnerKind kind, int index)
    {
        var owner = ChangeForms.OwnerOf(formId, regular, light);
        Assert.Equal(kind, owner.Kind);
        Assert.Equal(index, owner.Index);
    }
}

public class SaveAnalysisTests
{
    [Fact]
    public void Counts_the_form_ids_and_change_forms_the_missing_plugins_own()
    {
        using var data = new FakeDataFolder("KeeperMod.esp", "KeeperLight.esl");
        var save = SaveFile.FromBytes(SaveBuilder.WithMixedContent().Build());

        var analysis = SaveAnalysis.Run(save, PluginInventory.Build(save, data.Path));

        Assert.Equal([1, 3], analysis.DeadFormIdSlots.OrderBy(s => s));   // GhostMod form, GhostLight form
        Assert.Equal(2, analysis.DeadChangeForms);
        Assert.True(analysis.IsIntact);
    }

    [Fact]
    public void Names_the_missing_plugin_that_costs_the_most()
    {
        using var data = new FakeDataFolder();
        var builder = SaveBuilder.WithMixedContent();
        for (var i = 0; i < 5; i++) builder.FormIds.Add(SaveBuilder.RegularForm(1, (uint)(0x3000 + i)));
        var save = SaveFile.FromBytes(builder.Build());

        var heaviest = SaveAnalysis.Run(save, PluginInventory.Build(save, data.Path)).HeaviestMissing();

        Assert.Equal("GhostMod.esp", heaviest[0].Plugin.Name);
        Assert.Equal(6, heaviest[0].FormCount);
    }

    [Fact]
    public void Nothing_to_do_when_every_plugin_is_installed()
    {
        using var data = new FakeDataFolder("GhostMod.esp", "KeeperMod.esp", "GhostLight.esl", "KeeperLight.esl");
        var save = SaveFile.FromBytes(SaveBuilder.WithMixedContent().Build());

        var analysis = SaveAnalysis.Run(save, PluginInventory.Build(save, data.Path));
        Assert.Equal(RescueOutlook.NothingToDo, analysis.Outlook);
        Assert.Empty(analysis.DeadFormIdSlots);
    }

    [Theory]
    [InlineData(50_000, RescueOutlook.Good)]
    [InlineData(120_000, RescueOutlook.Uncertain)]
    [InlineData(200_000, RescueOutlook.Risky)]
    public void Outlook_follows_the_measured_change_form_thresholds(uint changeFormCount, RescueOutlook expected)
    {
        using var data = new FakeDataFolder();
        var save = SaveFile.FromBytes(SaveBuilder.WithMixedContent().Build());
        save.ChangeFormCount = changeFormCount;   // the count the game reports, not the records present

        var analysis = SaveAnalysis.Run(save, PluginInventory.Build(save, data.Path));
        Assert.Equal(expected, analysis.Outlook);
        Assert.False(analysis.IsIntact);          // the walk cannot reach that many records
    }
}

public class SaveSurgeonTests
{
    private static (SaveFile Save, PluginInventory Plugins, FakeDataFolder Data) Case()
    {
        var data = new FakeDataFolder("KeeperMod.esp", "KeeperLight.esl");
        var save = SaveFile.FromBytes(SaveBuilder.WithMixedContent().Build());
        return (save, PluginInventory.Build(save, data.Path), data);
    }

    [Fact]
    public void Removes_the_missing_plugins_from_the_lists()
    {
        var (save, plugins, data) = Case();
        using var _ = data;

        var outcome = SaveSurgeon.Operate(save, plugins, new SurgeryOptions());
        var result = SaveFile.FromBytes(outcome.Bytes);

        Assert.Equal(["Fallout4.esm", "KeeperMod.esp"], result.Plugins);
        Assert.Equal(["KeeperLight.esl"], result.LightPlugins);
        Assert.Equal(1, outcome.RemovedRegular);
        Assert.Equal(1, outcome.RemovedLight);
        Assert.True(outcome.Passed);
    }

    [Fact]
    public void Keeps_form_id_slots_in_place_and_zeroes_the_dead_ones()
    {
        var (save, plugins, data) = Case();
        using var _ = data;
        var slotCount = save.FormIds.Count;

        var result = SaveFile.FromBytes(SaveSurgeon.Operate(save, plugins, new SurgeryOptions()).Bytes);

        // Other sections index this array by position, so its length must never change.
        Assert.Equal(slotCount, result.FormIds.Count);
        Assert.Equal(0u, result.FormIds[1]);                              // GhostMod form cleared
        Assert.Equal(0u, result.FormIds[3]);                              // GhostLight form cleared
        Assert.Equal(SaveBuilder.RegularForm(0, 0x0F99EC), result.FormIds[0]);
        Assert.Equal(SaveBuilder.RegularForm(1, 0x002222), result.FormIds[2]);   // KeeperMod moved 2 -> 1
        Assert.Equal(SaveBuilder.LightForm(0, 0x222), result.FormIds[4]);        // KeeperLight moved 1 -> 0
        Assert.Equal(SaveBuilder.CreatedForm(0x004321), result.FormIds[5]);      // created form untouched
    }

    [Fact]
    public void Drops_only_the_change_forms_that_point_at_a_cleared_slot()
    {
        var (save, plugins, data) = Case();
        using var _ = data;
        var before = save.ChangeFormCount;

        var outcome = SaveSurgeon.Operate(save, plugins, new SurgeryOptions());
        var result = SaveFile.FromBytes(outcome.Bytes);

        Assert.Equal(2, outcome.DroppedChangeForms);
        Assert.Equal(before - 2u, result.ChangeFormCount);
        Assert.Equal((int)result.ChangeFormCount,
            ChangeForms.Walk(result.ChangeForms, result.ChangeFormCount).Count());
        Assert.DoesNotContain(ChangeForms.Walk(result.ChangeForms, result.ChangeFormCount),
            e => e.PointsAtFormIdArray && result.FormIds[e.RefValue] == 0);
    }

    [Fact]
    public void The_result_no_longer_depends_on_anything_that_is_missing()
    {
        var (save, plugins, data) = Case();
        using var _ = data;

        var result = SaveFile.FromBytes(SaveSurgeon.Operate(save, plugins, new SurgeryOptions()).Bytes);
        Assert.Equal(0, PluginInventory.Build(result, data.Path).MissingCount);
    }

    [Fact]
    public void Marks_the_in_game_name_with_the_prefix()
    {
        var (save, plugins, data) = Case();
        using var _ = data;

        var outcome = SaveSurgeon.Operate(save, plugins, new SurgeryOptions { Prefix = "[RESCUED] " });
        Assert.Equal("[RESCUED] Sanctuary Hills", SaveFile.FromBytes(outcome.Bytes).Location);
        Assert.Equal("[RESCUED] Sanctuary Hills", outcome.InGameName);
    }

    [Theory]
    [InlineData("Sanctuary", "[RESCUED] ", true, "[RESCUED] Sanctuary")]
    [InlineData("[OG] Sanctuary", "[RESCUED] ", true, "[RESCUED] Sanctuary")]
    [InlineData("[OG] Sanctuary", "[RESCUED] ", false, "[RESCUED] [OG] Sanctuary")]
    [InlineData("[RESCUED] Sanctuary", "[RESCUED] ", true, "[RESCUED] Sanctuary")]
    [InlineData("Sanctuary", "", true, "Sanctuary")]
    public void Prefixing_replaces_an_existing_tag_rather_than_stacking(
        string location, string prefix, bool replace, string expected)
    {
        var options = new SurgeryOptions { Prefix = prefix, ReplaceExistingTag = replace };
        Assert.Equal(expected, SaveSurgeon.ApplyPrefix(location, options));
    }

    [Fact]
    public void Vanilla_mode_leaves_only_the_base_game()
    {
        using var data = new FakeDataFolder("GhostMod.esp", "KeeperMod.esp", "GhostLight.esl", "KeeperLight.esl");
        var save = SaveFile.FromBytes(SaveBuilder.WithMixedContent().Build());
        var plugins = PluginInventory.Build(save, data.Path, vanillaOnly: true);

        var outcome = SaveSurgeon.Operate(save, plugins, new SurgeryOptions { VanillaOnly = true });
        var result = SaveFile.FromBytes(outcome.Bytes);

        Assert.Equal(["Fallout4.esm"], result.Plugins);
        Assert.Empty(result.LightPlugins);
        Assert.True(result.HasLightSection);      // the count field stays even with an empty list
        Assert.True(outcome.Passed);
    }

    [Fact]
    public void An_untouched_save_comes_out_the_same_size_apart_from_the_prefix()
    {
        using var data = new FakeDataFolder("GhostMod.esp", "KeeperMod.esp", "GhostLight.esl", "KeeperLight.esl");
        var save = SaveFile.FromBytes(SaveBuilder.WithMixedContent().Build());
        var plugins = PluginInventory.Build(save, data.Path);

        var outcome = SaveSurgeon.Operate(save, plugins, new SurgeryOptions { Prefix = "[OG] " });

        Assert.Equal(0, outcome.DroppedChangeForms);
        Assert.Equal(0, outcome.ZeroedFormIds);
        Assert.Equal(outcome.OriginalBytes + "[OG] ".Length, outcome.NewBytes);
    }

    [Fact]
    public void Refuses_to_write_when_the_change_form_section_does_not_read_cleanly()
    {
        var (save, plugins, data) = Case();
        using var _ = data;
        save.ChangeForms = save.ChangeForms[..^4];   // clip the last record

        var ex = Assert.Throws<SaveFormatException>(() => SaveSurgeon.Operate(save, plugins, new SurgeryOptions()));
        Assert.Contains("Refusing to write", ex.Message);
    }

    [Fact]
    public void Relabel_changes_the_name_and_leaves_the_plugins_alone()
    {
        var save = SaveFile.FromBytes(SaveBuilder.WithMixedContent().Build());
        var result = SaveFile.FromBytes(SaveSurgeon.Relabel(save, "[OG] "));

        Assert.Equal("[OG] Sanctuary Hills", result.Location);
        Assert.Equal(3, result.Plugins.Count);
        Assert.Equal(2, result.LightPlugins.Count);
    }
}
