using System.IO;
using System.Text;

namespace Translation.App;

internal static class AppLog
{
    public static void Write(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TranslationApp",
                "logs");
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, "app.log");
            File.AppendAllText(
                path,
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
            // 日志失败不影响主流程。
        }
    }
}
