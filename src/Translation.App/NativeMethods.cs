using System.Runtime.InteropServices;

namespace Translation.App;

internal static class NativeMethods
{
    public const int MOD_ALT = 0x0001;
    public const int MOD_CONTROL = 0x0002;
    public const int MOD_SHIFT = 0x0004;
    public const int MOD_WIN = 0x0008;
    public const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    public static void ForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || SetForegroundWindow(hwnd))
        {
            return;
        }

        var foreground = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
        var currentThread = GetCurrentThreadId();
        if (foregroundThread == currentThread)
        {
            SetForegroundWindow(hwnd);
            return;
        }

        AttachThreadInput(currentThread, foregroundThread, true);
        SetForegroundWindow(hwnd);
        AttachThreadInput(currentThread, foregroundThread, false);
    }
}
