namespace Translation.App;

public sealed class AppSettings
{
    public const int MaxIdleReleaseMinutes = 1440;

    public string Hotkey { get; set; } = "Ctrl+Alt+F9";

    public bool AutoStart { get; set; }

    /// <summary>窗口隐藏多少分钟后释放翻译/OCR 模型与词典；0 为不释放。</summary>
    public int IdleReleaseMinutes { get; set; } = 3;

    public double WindowWidth { get; set; } = 680;

    public double WindowHeight { get; set; } = 480;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    /// <summary>修正手工编辑/旧版本写入的越界值，避免启动时抛异常。</summary>
    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Hotkey))
        {
            Hotkey = "Ctrl+Alt+F9";
        }

        IdleReleaseMinutes = Math.Clamp(IdleReleaseMinutes, 0, MaxIdleReleaseMinutes);
        WindowWidth = NormalizeSize(WindowWidth, 680);
        WindowHeight = NormalizeSize(WindowHeight, 480);
    }

    private static double NormalizeSize(double value, double fallback)
        => double.IsFinite(value) && value > 0 ? value : fallback;
}
