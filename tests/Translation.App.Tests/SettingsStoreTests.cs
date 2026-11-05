using System.IO;
using Translation.App;
using Xunit;

namespace Translation.App.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory;

    public SettingsStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "translation-settings-tests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Load_文件不存在时返回默认值()
    {
        var store = new SettingsStore(_directory);
        var settings = store.Load();

        Assert.Equal("Ctrl+Alt+F9", settings.Hotkey);
        Assert.Equal(3, settings.IdleReleaseMinutes);
    }

    [Fact]
    public void SaveAndLoad_往返保留字段()
    {
        var store = new SettingsStore(_directory);
        var settings = new AppSettings
        {
            Hotkey = "Ctrl+Shift+F8",
            AutoStart = true,
            IdleReleaseMinutes = 42,
            WindowWidth = 800,
            WindowHeight = 600,
            WindowLeft = 100,
            WindowTop = 200,
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal("Ctrl+Shift+F8", loaded.Hotkey);
        Assert.True(loaded.AutoStart);
        Assert.Equal(42, loaded.IdleReleaseMinutes);
        Assert.Equal(800, loaded.WindowWidth);
        Assert.Equal(600, loaded.WindowHeight);
        Assert.Equal(100, loaded.WindowLeft);
        Assert.Equal(200, loaded.WindowTop);
    }

    [Fact]
    public void Load_损坏JSON回退默认值()
    {
        var store = new SettingsStore(_directory);
        File.WriteAllText(store.FilePath, "{ not valid json");

        var settings = store.Load();

        Assert.Equal("Ctrl+Alt+F9", settings.Hotkey);
    }

    [Fact]
    public void Load_越界值被修正()
    {
        var store = new SettingsStore(_directory);
        File.WriteAllText(
            store.FilePath,
            """{"Hotkey":null,"IdleReleaseMinutes":999999,"WindowWidth":-1,"WindowHeight":0}""");

        var settings = store.Load();

        Assert.Equal("Ctrl+Alt+F9", settings.Hotkey);
        Assert.Equal(AppSettings.MaxIdleReleaseMinutes, settings.IdleReleaseMinutes);
        Assert.Equal(680, settings.WindowWidth);
        Assert.Equal(480, settings.WindowHeight);
    }

    [Fact]
    public void Save_会规范化越界值()
    {
        var store = new SettingsStore(_directory);
        store.Save(new AppSettings { IdleReleaseMinutes = -5, WindowWidth = double.NaN });

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(store.FilePath));
        var root = document.RootElement;
        Assert.Equal(0, root.GetProperty("IdleReleaseMinutes").GetInt32());
        Assert.Equal(680, root.GetProperty("WindowWidth").GetDouble());
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }
}
