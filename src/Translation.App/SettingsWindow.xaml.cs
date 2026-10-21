using System.Windows;

namespace Translation.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow(AppSettings draft)
    {
        Draft = draft;
        InitializeComponent();
        HotkeyBox.Text = draft.Hotkey;
        AutoStartBox.IsChecked = draft.AutoStart;
    }

    public AppSettings Draft { get; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!HotkeyDefinition.TryParse(HotkeyBox.Text, out var hotkey, out var error))
        {
            System.Windows.MessageBox.Show(error ?? "热键格式不正确", "翻译设置");
            return;
        }

        Draft.Hotkey = hotkey.ToString();
        Draft.AutoStart = AutoStartBox.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
