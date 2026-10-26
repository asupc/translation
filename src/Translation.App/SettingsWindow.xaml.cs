using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Translation.App;

public partial class SettingsWindow : Window
{
    private HotkeyDefinition _hotkey;
    private HotkeyDefinition _initialHotkey;
    private bool _capturing;

    public SettingsWindow(AppSettings draft)
    {
        Draft = draft;
        InitializeComponent();
        if (!HotkeyDefinition.TryParse(draft.Hotkey, out var hotkey, out _))
        {
            hotkey = HotkeyDefinition.Default;
        }

        _hotkey = hotkey;
        _initialHotkey = hotkey;
        HotkeyBox.Text = hotkey.ToString();
        AutoStartBox.IsChecked = draft.AutoStart;
    }

    public AppSettings Draft { get; }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        HotkeyBox.Text = "按下组合键…";
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _capturing = false;
        // 无效提示或半成品（"Ctrl+…"）不作数，回到最近一次有效热键。
        HotkeyBox.Text = _hotkey.ToString();
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing)
        {
            return;
        }

        // 捕获模式接管所有按键：Esc 放弃修改，其余按键用于组成热键。
        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            _hotkey = _initialHotkey;
            HotkeyBox.Text = _hotkey.ToString();
            HotkeyBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            return;
        }

        // Alt 组合键以 System 键上报，实际键在 SystemKey。
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierKey(key))
        {
            // 只按了修饰键：实时显示已按下的部分，等待主键。
            HotkeyBox.Text = BuildPartialText(Keyboard.Modifiers);
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            HotkeyBox.Text = "需包含 Ctrl/Alt/Shift/Win 修饰键";
            return;
        }

        _hotkey = HotkeyDefinition.FromKeys(Keyboard.Modifiers, key);
        HotkeyBox.Text = _hotkey.ToString();
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
            or Key.System or Key.ImeProcessed or Key.DeadCharProcessed;
    }

    private static string BuildPartialText(ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add("…");
        return string.Join("+", parts);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Draft.Hotkey = _hotkey.ToString();
        Draft.AutoStart = AutoStartBox.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
