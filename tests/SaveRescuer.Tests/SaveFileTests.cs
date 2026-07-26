using SaveRescuer.Core;

namespace SaveRescuer.Tests;

public class Win1252Tests
{
    [Fact]
    public void Every_byte_survives_a_decode_and_encode_round_trip()
    {
        var all = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        Assert.Equal(all, Win1252.GetBytes(Win1252.GetString(all)));
    }

    [Fact]
    public void Decodes_the_windows_1252_specific_range()
    {
        Assert.Equal("€", Win1252.GetString([0x80]));   // euro sign
        Assert.Equal("’", Win1252.GetString([0x92]));   // right single quote
    }

    [Fact]
    public void Characters_outside_the_code_page_become_a_question_mark()
    {
        Assert.Equal("Ba?kan"u8.ToArray().Length, Win1252.GetBytes("Başkan").Length);
        Assert.Equal((byte)'?', Win1252.GetBytes("ş")[0]);
    }
}

public class SaveFileTests
{
    [Fact]
    public void Reads_the_header_fields()
    {
        var save = SaveFile.FromBytes(new SaveBuilder { PlayerName = "Violetta", Level = 63 }.Build());

        Assert.Equal("Violetta", save.PlayerName);
        Assert.Equal(63u, save.PlayerLevel);
        Assert.Equal("Sanctuary Hills", save.Location);
        Assert.Equal("003.12.45", save.PlayTime);
        Assert.Equal("1.10.980", save.GameVersion);
        Assert.Equal(69, save.FormVersion);
    }

    [Fact]
    public void Rebuild_reproduces_an_untouched_save_byte_for_byte()
    {
        var bytes = SaveBuilder.WithMixedContent().Build();
        Assert.Equal(bytes, SaveFile.FromBytes(bytes).Rebuild());
    }

    [Fact]
    public void Rebuild_round_trips_saves_with_trailing_fields_the_parser_does_not_name()
    {
        var bytes = new SaveBuilder
        {
            HeaderTail = [1, 2, 3, 4, 5],
            PluginBlockTail = [9, 9],
            ShotWidth = 4,
            ShotHeight = 3
        }.Build();

        var save = SaveFile.FromBytes(bytes);
        Assert.Equal(5, save.HeaderTailLength);
        Assert.Equal(bytes, save.Rebuild());
    }

    [Fact]
    public void Keeps_the_light_plugin_count_field_even_when_the_list_is_empty()
    {
        var save = SaveFile.FromBytes(new SaveBuilder { LightPlugins = [] }.Build());
        Assert.True(save.HasLightSection);
        Assert.Empty(save.LightPlugins);

        // A rebuild that dropped the count would leave the following bytes misread.
        var reread = SaveFile.FromBytes(save.Rebuild());
        Assert.True(reread.HasLightSection);
        Assert.Empty(reread.LightPlugins);
    }

    [Fact]
    public void Notices_a_save_written_without_the_light_section()
    {
        var save = SaveFile.FromBytes(new SaveBuilder { IncludeLightSection = false }.Build());
        Assert.False(save.HasLightSection);
    }

    [Fact]
    public void Setting_the_location_changes_the_in_game_name_and_nothing_else()
    {
        var save = SaveFile.FromBytes(SaveBuilder.WithMixedContent().Build());
        var before = save.Raw.Length;

        save.SetLocation("[RESCUED] Sanctuary Hills");
        var rebuilt = SaveFile.FromBytes(save.Rebuild());

        Assert.Equal("[RESCUED] Sanctuary Hills", rebuilt.Location);
        Assert.Equal(before + "[RESCUED] ".Length, rebuilt.Raw.Length);
        Assert.Equal("Tester", rebuilt.PlayerName);
        Assert.Equal(save.FormIds, rebuilt.FormIds);
        Assert.Empty(rebuilt.LayoutIssues());
    }

    [Fact]
    public void Layout_check_passes_on_a_well_formed_save()
    {
        Assert.Empty(SaveFile.FromBytes(SaveBuilder.WithMixedContent().Build()).LayoutIssues());
    }

    [Fact]
    public void Rejects_a_file_that_is_not_a_save()
    {
        var bytes = new byte[64];
        "NOT_A_SAVEGAME"u8.CopyTo(bytes);
        var ex = Assert.Throws<SaveFormatException>(() => SaveFile.FromBytes(bytes));
        Assert.Contains("FO4_SAVEGAME", ex.Message);
    }

    [Fact]
    public void Rejects_a_truncated_save()
    {
        var bytes = SaveBuilder.WithMixedContent().Build();
        Assert.ThrowsAny<Exception>(() => SaveFile.FromBytes(bytes[..(bytes.Length / 2)]));
    }
}

public class SaveSummaryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rescuer-summary-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Reads_the_same_facts_as_the_full_parser()
    {
        var builder = SaveBuilder.WithMixedContent();
        builder.PlayerName = "Wagner";
        builder.Level = 51;
        var path = builder.WriteTo(_folder, "Save900_ABCDEF01_Wagner_Sanctuary_010203_20260726_51_2.fos");

        var summary = SaveSummary.Read(path);
        var full = SaveFile.Load(path);

        Assert.True(summary.IsReadable);
        Assert.Equal(full.PlayerName, summary.PlayerName);
        Assert.Equal(full.PlayerLevel, summary.Level);
        Assert.Equal(full.Location, summary.Location);
        Assert.Equal(full.GameVersion, summary.GameVersion);
        Assert.Equal(full.FormVersion, summary.FormVersion);
        Assert.Equal(full.Plugins, summary.Plugins);
        Assert.Equal(full.LightPlugins, summary.LightPlugins);
        Assert.Equal(full.ChangeFormCount, summary.ChangeFormCount);
        Assert.Equal(full.FormIds.Count, summary.FormIdCount);
    }

    [Fact]
    public void Reads_the_screenshot_bytes()
    {
        var path = new SaveBuilder { ShotWidth = 4, ShotHeight = 3 }.WriteTo(_folder, "shot.fos");
        var summary = SaveSummary.Read(path);
        Assert.Equal(4 * 3 * 4, summary.ReadScreenshot()!.Length);
    }

    [Fact]
    public void Reports_a_broken_file_instead_of_throwing()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "broken.fos");
        File.WriteAllBytes(path, new byte[128]);

        var summary = SaveSummary.Read(path);
        Assert.False(summary.IsReadable);
        Assert.NotNull(summary.Error);
    }

    [Fact]
    public void Flags_slots_the_game_rotates()
    {
        Directory.CreateDirectory(_folder);
        var autosave = new SaveBuilder().WriteTo(_folder, "Autosave2_ABCDEF01_Nate_Vault111_1_2_3_2.fos");
        var manual = new SaveBuilder().WriteTo(_folder, "Save12_ABCDEF01_Nate_Vault111_1_2_3_2.fos");

        Assert.True(SaveSummary.Read(autosave).IsRotatingSlot);
        Assert.False(SaveSummary.Read(manual).IsRotatingSlot);
    }

    [Fact]
    public void Library_scan_lists_every_save_in_the_folder()
    {
        new SaveBuilder { PlayerName = "Alex" }.WriteTo(_folder, "Save1_AAAAAAAA_Alex_a_1_2_3_2.fos");
        new SaveBuilder { PlayerName = "Magda" }.WriteTo(_folder, "Save2_BBBBBBBB_Magda_b_1_2_3_2.fos");
        new SaveBuilder { PlayerName = "Alex" }.WriteTo(_folder, "Save3_AAAAAAAA_Alex_c_1_2_3_2.fos");

        var saves = SaveLibrary.Scan(_folder);
        Assert.Equal(3, saves.Count);

        var groups = SaveLibrary.ByCharacter(saves);
        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups.First(g => g.Key == "Alex").Count());
    }
}
