using System.Text;
using SaveRescuer.Core;

namespace SaveRescuer.Tests;

public sealed class StubPluginTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rescuer-stub").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void StubStartsWithValidPluginHeader()
    {
        var bytes = StubPlugin.Build(light: false);

        Assert.Equal("TES4", Encoding.ASCII.GetString(bytes, 0, 4));
        int declaredSize = BitConverter.ToInt32(bytes, 4);
        Assert.Equal(bytes.Length - 24, declaredSize);        // header size matches payload
        Assert.Contains("HEDR", Encoding.Latin1.GetString(bytes));
        Assert.Contains("Fallout4.esm", Encoding.Latin1.GetString(bytes));
    }

    [Fact]
    public void LightStubCarriesEslFlag()
    {
        int regularFlags = BitConverter.ToInt32(StubPlugin.Build(light: false), 8);
        int lightFlags = BitConverter.ToInt32(StubPlugin.Build(light: true), 8);

        Assert.Equal(0, regularFlags & 0x200);
        Assert.Equal(0x200, lightFlags & 0x200);
    }

    [Fact]
    public void WrittenStubIsRecognisedAsStub()
    {
        var path = Path.Combine(_dir, "Placeholder.esp");
        StubPlugin.Write(path, light: false);

        Assert.True(StubPlugin.IsStub(path));
    }

    [Fact]
    public void RealPluginsAreNotMistakenForStubs()
    {
        // A plugin with actual records is far larger and carries no tool marker.
        var path = Path.Combine(_dir, "RealMod.esp");
        var content = new List<byte>(Encoding.ASCII.GetBytes("TES4"));
        content.AddRange(new byte[20]);
        content.AddRange(Enumerable.Repeat((byte)0x41, 4096));
        File.WriteAllBytes(path, content.ToArray());

        Assert.False(StubPlugin.IsStub(path));
    }

    [Fact]
    public void SmallNonStubFilesAreNotMistakenForStubs()
    {
        var path = Path.Combine(_dir, "Small.esp");
        File.WriteAllBytes(path, [.. Encoding.ASCII.GetBytes("TES4"), .. new byte[40]]);

        Assert.False(StubPlugin.IsStub(path));      // valid header but no tool marker
    }

    [Fact]
    public void MissingFileIsNotAStub()
    {
        Assert.False(StubPlugin.IsStub(Path.Combine(_dir, "ghost.esp")));
    }
}
