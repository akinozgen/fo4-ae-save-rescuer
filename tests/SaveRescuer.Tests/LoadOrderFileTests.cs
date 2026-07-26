using System.Text;
using SaveRescuer.Core;

namespace SaveRescuer.Tests;

public sealed class LoadOrderFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rescuer-order").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string WriteFile(params string[] lines)
    {
        var path = Path.Combine(_dir, "plugins.txt");
        File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public void EnableFlipsAnExistingDisabledEntryInPlace()
    {
        var path = WriteFile("# comment", "*First.esp", "Disabled.esp", "*Last.esp");
        var order = LoadOrderFile.Load(path);

        Assert.True(order.Enable("Disabled.esp"));
        order.Save();

        var lines = File.ReadAllText(path).Split("\r\n");
        Assert.Equal(["# comment", "*First.esp", "*Disabled.esp", "*Last.esp"], lines[..4]);
    }

    [Fact]
    public void EnableAppendsWhenTheEntryIsAbsent()
    {
        var path = WriteFile("*First.esp");
        var order = LoadOrderFile.Load(path);

        Assert.True(order.Enable("Brand New.esp"));
        order.Save();

        Assert.Contains("*Brand New.esp", File.ReadAllText(path));
    }

    [Fact]
    public void EnableIsIdempotent()
    {
        var path = WriteFile("*Already.esp");
        var order = LoadOrderFile.Load(path);

        Assert.False(order.Enable("Already.esp"));
        Assert.False(order.Enable("already.esp"));      // case-insensitive
        order.Save();

        Assert.Single(File.ReadAllLines(path));
    }

    [Fact]
    public void SavePreservesCommentsAndUsesCrLfWithoutBom()
    {
        var path = WriteFile("# keep me", "*A.esp", "B.esp");
        var order = LoadOrderFile.Load(path);
        order.Enable("B.esp");
        order.Save();

        var bytes = File.ReadAllBytes(path);
        Assert.NotEqual(0xEF, bytes[0]);                            // no UTF-8 BOM
        var text = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("# keep me", text);
        Assert.DoesNotContain("\r\r", text);                        // no doubled carriage returns
        Assert.Equal(2, text.Split("\r\n").Count(l => l.StartsWith('*')));
    }

    [Fact]
    public void RemoveDropsEntryEntirely()
    {
        var path = WriteFile("*A.esp", "*Stub.esp", "*B.esp");
        var order = LoadOrderFile.Load(path);

        Assert.True(order.Remove("Stub.esp"));
        order.Save();

        Assert.DoesNotContain("Stub.esp", File.ReadAllText(path));
        Assert.Equal(2, order.Enabled.Count());
    }

    [Fact]
    public void BackupCopiesTheOriginalFile()
    {
        var path = WriteFile("*A.esp");
        var order = LoadOrderFile.Load(path);

        var backup = order.Backup();

        Assert.True(File.Exists(backup));
        Assert.Equal(File.ReadAllText(path), File.ReadAllText(backup));
    }

    [Fact]
    public void HandlesMissingFileAsEmptyList()
    {
        var order = LoadOrderFile.Load(Path.Combine(_dir, "nope.txt"));
        Assert.Empty(order.Enabled);
        Assert.True(order.Enable("New.esp"));
    }
}
