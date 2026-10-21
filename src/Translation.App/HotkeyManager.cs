using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Translation.App;

public sealed class HotkeyManager : IDisposable
{
    private const int TextHotkeyId = 1;

    private HwndSource? _source;

    public event EventHandler? TextHotkeyPressed;

    public bool TextRegistered { get; private set; }
    public int TextError { get; private set; }

    public bool Register(HotkeyDefinition definition)
    {
        if (_source is null)
        {
            var parameters = new HwndSourceParameters("TranslationAppHotkeys")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ExtendedWindowStyle = 0x00000080, // WS_EX_TOOLWINDOW
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
        }

        if (TextRegistered)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, TextHotkeyId);
            TextRegistered = false;
        }

        TextRegistered = NativeMethods.RegisterHotKey(
            _source.Handle,
            TextHotkeyId,
            definition.Modifiers,
            definition.VirtualKey);
        TextError = TextRegistered ? 0 : Marshal.GetLastWin32Error();

        return TextRegistered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_HOTKEY)
        {
            return IntPtr.Zero;
        }

        if (wParam.ToInt32() == TextHotkeyId)
        {
            TextHotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is null)
        {
            return;
        }

        if (TextRegistered)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, TextHotkeyId);
            TextRegistered = false;
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }
}
