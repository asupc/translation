using System.IO;
using System.Text.Json;

namespace Translation.App;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public SettingsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TranslationApp");
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, "settings.json");
    }

    public string FilePath { get; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception error)
        {
            AppLog.Write($"settings load failed: {error.Message}");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, FilePath, true);
    }
}
