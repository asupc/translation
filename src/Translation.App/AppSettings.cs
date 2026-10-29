namespace Translation.App;

public sealed class AppSettings
{
    public string Hotkey { get; set; } = "Ctrl+Alt+F9";

    public bool AutoStart { get; set; }

    /// <summary>窗口隐藏多少分钟后释放翻译/OCR 模型与词典；0 为不释放。</summary>
    public int IdleReleaseMinutes { get; set; } = 3;

    public double WindowWidth { get; set; } = 680;

    public double WindowHeight { get; set; } = 480;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }
}
