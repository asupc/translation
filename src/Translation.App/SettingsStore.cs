using System.IO;
using System.Text.Json;

namespace Translation.App;

public sealed class SettingsStore
{
    private const string DefaultDirectoryName = "TranslationApp";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public SettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            DefaultDirectoryName))
    {
    }

    /// <summary>测试用：指定设置目录，不写入用户的 %APPDATA%。</summary>
    public SettingsStore(string directory)
    {
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
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception error)
        {
            AppLog.Write($"settings load failed: {error.Message}");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temporary = FilePath + ".tmp";
        try
        {
            File.WriteAllText(temporary, json);
            File.Move(temporary, FilePath, true);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch
            {
                // 清理临时文件失败不掩盖原始异常。
            }

            throw;
        }
    }
}
