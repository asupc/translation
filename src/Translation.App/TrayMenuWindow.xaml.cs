using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace Translation.App;

/// <summary>
/// 托盘右键菜单。用 WPF 自绘以匹配主窗口的深色风格
/// （WinForms ContextMenuStrip 是系统浅色模板，与窗口差异明显）。
/// </summary>
public partial class TrayMenuWindow : Window
{
    private readonly Action _onShow;
    private readonly Action _onSettings;
    private readonly Action _onExit;
    private bool _actionTaken;
    private bool _closing;

    public TrayMenuWindow(Action onShow, Action onSettings, Action onExit)
    {
        _onShow = onShow;
        _onSettings = onSettings;
        _onExit = onExit;
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // 与主窗口一致：交给 DWM 做圆角（Win11），不自绘圆角以避免透明窗口的渲染开销。
        var handle = new WindowInteropHelper(this).Handle;
        var preference = 2; // DWMWCP_ROUND
        _ = DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int valueSize);

    /// <summary>在鼠标位置弹出：贴着托盘图标向左上展开，超出屏幕时自动翻转并夹在工作区内。</summary>
    public void ShowNearCursor()
    {
        // 先以透明状态显示，等布局确定尺寸和 DPI 后再定位，避免闪烁。
        Opacity = 0;
        Show();
        UpdateLayout();
        PositionNearCursor();
        Opacity = 1;

        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.ForceForeground(handle);
        Activate();
        ShowItem.Focus();
    }

    private void PositionNearCursor()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var cursor = WinForms.Cursor.Position; // 物理像素
        var workArea = WinForms.Screen.FromPoint(cursor).WorkingArea; // 物理像素

        var scaleX = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
        var scaleY = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;

        var cursorX = cursor.X / scaleX;
        var cursorY = cursor.Y / scaleY;
        var left = workArea.Left / scaleX;
        var top = workArea.Top / scaleY;
        var right = workArea.Right / scaleX;
        var bottom = workArea.Bottom / scaleY;

        var width = ActualWidth;
        var height = ActualHeight;

        // 托盘在底部：菜单向上展开；放不下则改到光标下方（顶部任务栏的情况）。
        var x = cursorX - width;
        var y = cursorY - height;
        if (y < top)
        {
            y = cursorY;
        }

        Left = Math.Max(left, Math.Min(x, right - width));
        Top = Math.Max(top, Math.Min(y, bottom - height));
    }

    private void CloseThen(Action action)
    {
        if (_actionTaken)
        {
            return;
        }

        _actionTaken = true;
        Close();
        // 等菜单窗口真正退出后再执行动作，避免在菜单消息处理中嵌套打开对话框。
        Dispatcher.BeginInvoke(action);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // 关闭过程中 Deactivated 仍会到达，用它挡住重复 Close（窗口正在关闭时 Close 会抛异常）。
        _closing = true;
        base.OnClosing(e);
    }

    private void Show_Click(object sender, RoutedEventArgs e) => CloseThen(_onShow);

    private void Settings_Click(object sender, RoutedEventArgs e) => CloseThen(_onSettings);

    private void Exit_Click(object sender, RoutedEventArgs e) => CloseThen(_onExit);

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // 点击菜单以外的地方即关闭（与系统托盘菜单行为一致）。
        Dismiss();
    }

    /// <summary>点到菜单的空白处也关掉，手感跟系统菜单一致。</summary>
    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Dismiss();
        e.Handled = true;
    }

    /// <summary>关闭菜单；已开始关闭时不重复 Close（窗口关闭期间 Close 会抛异常）。</summary>
    public void Dismiss()
    {
        if (_actionTaken || _closing)
        {
            return;
        }

        _actionTaken = true;
        Close();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Dismiss();
                e.Handled = true;
                break;
            case Key.Down:
                MoveFocus(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveFocus(-1);
                e.Handled = true;
                break;
        }
    }

    /// <summary>上下键在三个菜单项之间移动焦点（首尾循环）。</summary>
    private void MoveFocus(int direction)
    {
        var items = new[] { ShowItem, SettingsItem, ExitItem };
        var current = Array.IndexOf(items, Keyboard.FocusedElement);
        var next = current < 0
            ? (direction > 0 ? 0 : items.Length - 1)
            : (current + direction + items.Length) % items.Length;
        items[next].Focus();
    }
}
