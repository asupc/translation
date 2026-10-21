namespace Translation.App;

public sealed class AppSettings
{
    public string Hotkey { get; set; } = "Ctrl+Alt+F9";

    public bool AutoStart { get; set; }

    public double WindowWidth { get; set; } = 680;

    public double WindowHeight { get; set; } = 480;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }
}
