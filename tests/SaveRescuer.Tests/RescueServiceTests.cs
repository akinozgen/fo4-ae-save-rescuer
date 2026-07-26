using System.Text;
using SaveRescuer.Core;
using SaveRescuer.Core.Models;

namespace SaveRescuer.Tests;

public sealed class RescueServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("rescuer-service").FullName;
    private readonly string _data;
    private readonly string _saves;
    private readonly string _pluginsTxt;

    public RescueServiceTests()
    {
        _data = Path.Combine(_root, "Data");
        _saves = Path.Combine(_root, "Saves");
        Directory.CreateDirectory(_data);
        Directory.CreateDirectory(_saves);
        _pluginsTxt = Path.Combine(_root, "plugins.txt");
        File.WriteAllText(_pluginsTxt, "# header\r\n", new UTF8Encoding(false));
    }

    public void Dispose()
    {
        // Remove the manifest this test run may have written before deleting the sandbox.
        try { new RescueService(Env()).Revert(); } catch { /* best effort */ }
        Directory.Delete(_root, true);
    }

    private GameEnvironment Env() => new()
    {
        DataFolder = _data,
        PluginsTxt = _pluginsTxt,
        SavesFolder = _saves
    };

    private void RealPlugin(string name, bool enabled)
    {
        File.WriteAllBytes(Path.Combine(_data, name),
            [.. Encoding.ASCII.GetBytes("TES4"), .. new byte[20], .. Enumerable.Repeat((byte)7, 2048)]);
        if (!enabled) return;
        var order = LoadOrderFile.Load(_pluginsTxt);
        order.Enable(name);
        order.Save();
    }

    [Fact]
    public void DiagnosisClassifiesEveryPluginState()
    {
        RealPlugin("Enabled.esp", enabled: true);
        RealPlugin("Present.esp", enabled: false);
        StubPlugin.Write(Path.Combine(_data, "Ghost.esp"), light: false);

        var path = SaveFactory.Write(_saves,
            plugins: ["Fallout4.esm", "Enabled.esp", "Present.esp", "Ghost.esp", "Gone.esp"],
            lightPlugins: ["GoneLight.esl"]);
        var save = SaveReader.Read(path);

        var d = new RescueService(Env()).Diagnose(save);

        Assert.Equal(PluginState.BaseGame, d.Plugins.Single(p => p.Name == "Fallout4.esm").State);
        Assert.Equal(PluginState.Ok, d.Plugins.Single(p => p.Name == "Enabled.esp").State);
        Assert.Equal(PluginState.NotEnabled, d.Plugins.Single(p => p.Name == "Present.esp").State);
        Assert.Equal(PluginState.Stub, d.Plugins.Single(p => p.Name == "Ghost.esp").State);
        Assert.Equal(PluginState.Missing, d.Plugins.Single(p => p.Name == "Gone.esp").State);
        Assert.Equal(2, d.MissingCount);                    // Gone.esp + GoneLight.esl
        Assert.False(d.CanLoadCleanly);
    }

    [Fact]
    public void RescueCreatesStubsEnablesEntriesAndLeavesSaveLoadable()
    {
        var path = SaveFactory.Write(_saves,
            plugins: ["Fallout4.esm", "Gone.esp"],
            lightPlugins: ["GoneLight.esl"]);
        var save = SaveReader.Read(path);
        var service = new RescueService(Env());

        var result = service.Rescue(save);

        Assert.Equal(["Gone.esp", "GoneLight.esl"], result.CreatedStubs);
        Assert.True(File.Exists(Path.Combine(_data, "Gone.esp")));
        Assert.True(StubPlugin.IsStub(Path.Combine(_data, "GoneLight.esl")));
        Assert.Equal(0x200, BitConverter.ToInt32(File.ReadAllBytes(Path.Combine(_data, "GoneLight.esl")), 8) & 0x200);

        var order = LoadOrderFile.Load(_pluginsTxt);
        Assert.True(order.IsEnabled("Gone.esp"));
        Assert.True(order.IsEnabled("GoneLight.esl"));

        Assert.True(service.Diagnose(SaveReader.Read(path)).CanLoadCleanly);
    }

    [Fact]
    public void RescueEnablesPluginsThatExistButAreDisabled()
    {
        RealPlugin("Present.esp", enabled: false);
        var save = SaveReader.Read(SaveFactory.Write(_saves, plugins: ["Fallout4.esm", "Present.esp"]));

        var result = new RescueService(Env()).Rescue(save);

        Assert.Empty(result.CreatedStubs);
        Assert.Contains("Present.esp", result.EnabledPlugins);
        Assert.True(LoadOrderFile.Load(_pluginsTxt).IsEnabled("Present.esp"));
    }

    [Fact]
    public void RescueNeverOverwritesRealPlugins()
    {
        RealPlugin("Precious.esp", enabled: false);
        var before = File.ReadAllBytes(Path.Combine(_data, "Precious.esp"));
        var save = SaveReader.Read(SaveFactory.Write(_saves, plugins: ["Precious.esp"]));

        new RescueService(Env()).Rescue(save);

        Assert.Equal(before, File.ReadAllBytes(Path.Combine(_data, "Precious.esp")));
    }

    [Fact]
    public void DryRunWritesNothing()
    {
        var save = SaveReader.Read(SaveFactory.Write(_saves, plugins: ["Fallout4.esm", "Gone.esp"]));
        var orderBefore = File.ReadAllText(_pluginsTxt);

        var result = new RescueService(Env()).Rescue(save, dryRun: true);

        Assert.Contains("Gone.esp", result.CreatedStubs);
        Assert.False(File.Exists(Path.Combine(_data, "Gone.esp")));
        Assert.Equal(orderBefore, File.ReadAllText(_pluginsTxt));
    }

    [Fact]
    public void RescueReportsNothingToDoOnAHealthySave()
    {
        RealPlugin("Fine.esp", enabled: true);
        var save = SaveReader.Read(SaveFactory.Write(_saves, plugins: ["Fallout4.esm", "Fine.esp"]));

        var result = new RescueService(Env()).Rescue(save);

        Assert.False(result.AnyChange);
        Assert.Contains(result.Messages, m => m.Contains("no changes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RevertRemovesStubsAndRestoresLoadOrder()
    {
        RealPlugin("Keep.esp", enabled: true);
        var orderBefore = File.ReadAllText(_pluginsTxt);
        var save = SaveReader.Read(SaveFactory.Write(_saves,
            plugins: ["Fallout4.esm", "Keep.esp", "Gone.esp"], lightPlugins: ["GoneLight.esl"]));
        var service = new RescueService(Env());
        service.Rescue(save);

        var revert = service.Revert();

        Assert.Equal(["Gone.esp", "GoneLight.esl"], revert.RemovedStubs);
        Assert.False(File.Exists(Path.Combine(_data, "Gone.esp")));
        Assert.False(File.Exists(Path.Combine(_data, "GoneLight.esl")));
        Assert.True(File.Exists(Path.Combine(_data, "Keep.esp")));          // real content untouched
        Assert.Equal(orderBefore, File.ReadAllText(_pluginsTxt));
    }

    [Fact]
    public void RevertLeavesFilesThatAreNoLongerStubs()
    {
        var save = SaveReader.Read(SaveFactory.Write(_saves, plugins: ["Fallout4.esm", "Gone.esp"]));
        var service = new RescueService(Env());
        service.Rescue(save);

        // The user later installed the real mod over the placeholder.
        RealPlugin("Gone.esp", enabled: true);

        var revert = service.Revert();

        Assert.Empty(revert.RemovedStubs);
        Assert.True(File.Exists(Path.Combine(_data, "Gone.esp")));
        Assert.Contains(revert.Messages, m => m.Contains("Gone.esp"));
    }

    [Fact]
    public void EnumerateSavesReturnsNewestFirst()
    {
        var older = SaveFactory.Write(_saves, characterName: "Older");
        Thread.Sleep(30);
        var newer = SaveFactory.Write(_saves, characterName: "Newer");
        File.SetLastWriteTime(older, DateTime.Now.AddHours(-2));
        File.SetLastWriteTime(newer, DateTime.Now);

        var names = RescueService.EnumerateSaves(_saves).Select(s => s.CharacterName).ToList();

        Assert.Equal(["Newer", "Older"], names);
    }
}
