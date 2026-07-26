using SaveRescuer.Core;

namespace SaveRescuer.Tests;

public sealed class SaveReaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rescuer-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void ReadsHeaderFields()
    {
        var path = SaveFactory.Write(_dir, characterName: "Violetta", level: 71,
            location: "Far Harbor", playTime: "0d.4h.8m", gameVersion: "1.11.221.0");

        var info = SaveReader.Read(path);

        Assert.Equal("Violetta", info.CharacterName);
        Assert.Equal(71u, info.CharacterLevel);
        Assert.Equal("Far Harbor", info.Location);
        Assert.Equal("0d.4h.8m", info.PlayTime);
        Assert.Equal("1.11.221.0", info.GameVersion);
        Assert.Equal(69, info.FormVersion);
    }

    [Fact]
    public void ReadsRegularAndLightPluginLists()
    {
        var path = SaveFactory.Write(_dir,
            plugins: ["Fallout4.esm", "DLCCoast.esm", "MyMod.esp"],
            lightPlugins: ["ccSomething.esl", "Tiny.esl"]);

        var info = SaveReader.Read(path);

        Assert.Equal(["Fallout4.esm", "DLCCoast.esm", "MyMod.esp"], info.Plugins);
        Assert.Equal(["ccSomething.esl", "Tiny.esl"], info.LightPlugins);
        Assert.Equal(5, info.TotalPluginCount);
    }

    [Fact]
    public void HandlesSavesWithoutLightPlugins()
    {
        var path = SaveFactory.Write(_dir, plugins: ["Fallout4.esm"], lightPlugins: []);

        var info = SaveReader.Read(path);

        Assert.Single(info.Plugins);
        Assert.Empty(info.LightPlugins);
    }

    [Fact]
    public void ToleratesExtraHeaderFieldsFromNewerGameVersions()
    {
        // Newer builds append fields to the header; parsing must rely on headerSize, not field order.
        var path = SaveFactory.Write(_dir, headerPadding: 24,
            plugins: ["Fallout4.esm", "Late.esp"], lightPlugins: ["Late.esl"]);

        var info = SaveReader.Read(path);

        Assert.Equal(["Fallout4.esm", "Late.esp"], info.Plugins);
        Assert.Equal(["Late.esl"], info.LightPlugins);
    }

    [Fact]
    public void ReadsEmbeddedScreenshot()
    {
        var path = SaveFactory.Write(_dir, shotWidth: 4, shotHeight: 3);

        var info = SaveReader.Read(path);

        Assert.Equal(4, info.ScreenshotWidth);
        Assert.Equal(3, info.ScreenshotHeight);
        Assert.NotNull(info.ScreenshotRgba);
        Assert.Equal(4 * 3 * 4, info.ScreenshotRgba!.Length);
    }

    [Fact]
    public void RejectsNonSaveFiles()
    {
        var path = Path.Combine(_dir, "notasave.fos");
        File.WriteAllText(path, "hello wasteland");

        Assert.False(SaveReader.LooksLikeSave(path));
        Assert.Throws<InvalidDataException>(() => SaveReader.Read(path));
    }

    [Fact]
    public void RecognisesValidSaveSignature()
    {
        var path = SaveFactory.Write(_dir);
        Assert.True(SaveReader.LooksLikeSave(path));
    }
}
