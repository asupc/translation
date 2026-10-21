using Microsoft.Win32;

namespace Translation.App;

internal static class AutoStartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TranslationApp";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) is string value
            && !string.IsNullOrWhiteSpace(value);
    }

    public static void SetEnabled(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{executablePath}\" --tray");
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }
}
