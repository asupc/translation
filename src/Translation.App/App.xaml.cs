using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace Translation.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\TranslationApp.SingleInstance";
    private const string ShowEventName = @"Local\TranslationApp.Show";

    private WinForms.NotifyIcon? _tray;
    private TrayMenuWindow? _trayMenu;
    private HotkeyManager? _hotkeys;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private Task? _coreInitTask;
    private OcrService? _ocr;
    private Task<OcrService>? _ocrInitTask;
    private AppSettings _settings = new();
    private SettingsStore? _settingsStore;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private CancellationTokenSource? _showListenerCts;
    private readonly DispatcherTimer _idleTimer = new();
    private int _activeOperations;
    private bool _releasing;

    public TranslationCore? Core { get; private set; }
    public bool CoreReady { get; private set; }
    public string? CoreError { get; private set; }

    /// <summary>离线词典（ECDICT SQLite）；库文件缺失时查询返回 null，自动回退句子翻译。</summary>
    public DictionaryService Dictionary { get; } = new(Paths.DictionaryDatabase);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _instanceMutex = new Mutex(true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            AppLog.Write("second instance detected, signaling existing instance");
            SignalExistingInstance();
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();

        if (!HotkeyDefinition.TryParse(_settings.Hotkey, out var hotkey, out var hotkeyError))
        {
            AppLog.Write($"invalid saved hotkey '{_settings.Hotkey}': {hotkeyError}");
            hotkey = HotkeyDefinition.Default;
            _settings.Hotkey = hotkey.ToString();
        }

        try
        {
            _settings.AutoStart = AutoStartManager.IsEnabled();
        }
        catch (Exception error)
        {
            AppLog.Write($"autostart read failed: {error.Message}");
            _settings.AutoStart = false;
        }

        _idleTimer.Tick += OnIdleTimerTick;
        PreloadCore();

        _mainWindow = new MainWindow(this, _settings);
        var startHidden = Array.Exists(
            e.Args,
            argument => string.Equals(argument, "--tray", StringComparison.OrdinalIgnoreCase));
        if (!startHidden)
        {
            _mainWindow.ShowInitial();
        }
        else
        {
            ArmIdleRelease();
        }

        SetupTray();

        _hotkeys = new HotkeyManager();
        _hotkeys.TextHotkeyPressed += (_, _) => OnTranslateHotkey();
        var hotkeyRegistered = _hotkeys.Register(hotkey);
        AppLog.Write(
            $"startup hotkey={hotkey} registered={hotkeyRegistered} error={_hotkeys.TextError}");
        if (!hotkeyRegistered)
        {
            System.Windows.MessageBox.Show(
                $"全局热键 {hotkey} 注册失败（error={_hotkeys.TextError}）。\n" +
                "请在托盘的“设置”中更换热键。",
                "翻译");
        }

        StartShowListener();
    }

    private static void SignalExistingInstance()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var handle = EventWaitHandle.OpenExisting(ShowEventName);
                handle.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(100);
            }
        }
    }

    private void StartShowListener()
    {
        _showListenerCts = new CancellationTokenSource();
        var token = _showListenerCts.Token;
        _ = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                bool signaled;
                try
                {
                    signaled = _showEvent?.WaitOne(250) == true;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (signaled && !Dispatcher.HasShutdownStarted)
                {
                    Dispatcher.BeginInvoke(new Action(ShowMainWindow));
                }
            }
        }, token);
    }

    /// <summary>启动时与空闲释放后（懒）加载翻译模型。</summary>
    private void PreloadCore()
    {
        if (_coreInitTask is not null)
        {
            return;
        }

        Core = new TranslationCore(
            Paths.TranslationModel("opus-mt-zh-en-int8"),
            Paths.TranslationModel("opus-mt-en-zh-int8"));
        _coreInitTask = InitializeCoreAsync();
    }

    private async Task InitializeCoreAsync()
    {
        try
        {
            await Core!.InitializeAsync();
            CoreReady = true;
            _mainWindow?.SetStatus("就绪");
            AppLog.Write("translation core ready");
        }
        catch (Exception error)
        {
            CoreError = error.Message;
            _mainWindow?.SetStatus($"模型加载失败: {error.Message}");
            AppLog.Write($"translation core failed: {error.Message}");
        }
    }

    public async Task EnsureCoreReadyAsync()
    {
        // 空闲释放后 Core 已销毁，首次翻译在这里重新加载。
        PreloadCore();
        await _coreInitTask!;
        if (!CoreReady)
        {
            throw new InvalidOperationException(CoreError ?? "翻译模型尚未加载完成");
        }
    }

    public async Task<OcrService> EnsureOcrAsync()
    {
        _ocrInitTask ??= Task.Run(() => new OcrService(Paths.OcrModels));
        _ocr = await _ocrInitTask;
        return _ocr;
    }

    /// <summary>窗口隐藏：按设置武装空闲释放计时；0 为不释放。</summary>
    public void OnMainWindowHidden()
    {
        ArmIdleRelease();
    }

    /// <summary>窗口显示：取消空闲释放，并预热翻译模型掩盖释放后的加载延迟。</summary>
    public void OnMainWindowShown()
    {
        _idleTimer.Stop();
        PreloadCore();
    }

    /// <summary>翻译/OCR 进行中计数；空闲释放据此推迟，避免销毁使用中的模型。</summary>
    public void BeginOperation() => _activeOperations++;

    public void EndOperation() => _activeOperations--;

    private void ArmIdleRelease()
    {
        _idleTimer.Stop();
        if (_settings.IdleReleaseMinutes > 0)
        {
            _idleTimer.Interval = TimeSpan.FromMinutes(_settings.IdleReleaseMinutes);
            _idleTimer.Start();
        }
    }

    private async void OnIdleTimerTick(object? sender, EventArgs e)
    {
        if (_releasing)
        {
            return;
        }

        _idleTimer.Stop();
        if (_activeOperations > 0)
        {
            // 有翻译/OCR 还在跑，稍后重试。
            _idleTimer.Interval = TimeSpan.FromSeconds(30);
            _idleTimer.Start();
            return;
        }

        _releasing = true;
        try
        {
            // 先等可能仍在初始化的模型任务收尾，避免销毁加载中的引擎；
            // InitializeCoreAsync 内部已兜底异常，这里的 catch 只为防御。
            if (_coreInitTask is not null)
            {
                try { await _coreInitTask; } catch { }
            }

            if (_ocrInitTask is not null)
            {
                try { await _ocrInitTask; } catch { }
            }

            // await 期间窗口可能被呼出使用，恢复后在 UI 线程再查一次。
            if (_mainWindow is { IsVisible: true } || _activeOperations > 0)
            {
                return;
            }

            var core = Core;
            Core = null;
            _coreInitTask = null;
            CoreReady = false;
            CoreError = null;

            var ocr = _ocr;
            _ocr = null;
            _ocrInitTask = null;

            Dictionary.Dispose();
            if (core is not null || ocr is not null)
            {
                await Task.Run(() =>
                {
                    ocr?.Dispose();
                    core?.Dispose();
                });
            }

            AppLog.Write(
                $"idle release after {_settings.IdleReleaseMinutes}min: translation/ocr models and dictionary freed");
        }
        finally
        {
            _releasing = false;
            // 期间若 OCR/翻译被用过会重新加载，隐藏状态下重新武装等下一轮空闲。
            if (_mainWindow is not { IsVisible: true })
            {
                ArmIdleRelease();
            }
        }
    }

    private void OnTranslateHotkey()
    {
        AppLog.Write("translate hotkey fired");
        _mainWindow?.ShowForHotkey();
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/app.ico", UriKind.Absolute);
            var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
            if (stream is not null)
            {
                return new System.Drawing.Icon(stream);
            }
        }
        catch (Exception error)
        {
            AppLog.Write($"load tray icon failed: {error.Message}");
        }

        return System.Drawing.SystemIcons.Application;
    }

    private void SetupTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Translation",
            Visible = true,
        };

        // 菜单改用 WPF 自绘（TrayMenuWindow），与主窗口深色风格一致；
        // 这里只处理鼠标事件并在光标处弹出。
        _tray.MouseUp += Tray_MouseUp;
        _tray.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void Tray_MouseUp(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button != WinForms.MouseButtons.Right)
        {
            return;
        }

        if (_trayMenu is not null)
        {
            // 已在弹出中再点右键：收起菜单（与系统托盘菜单一致）。
            _trayMenu.Dismiss();
            return;
        }

        var menu = new TrayMenuWindow(ShowMainWindow, OpenSettings, ShutdownTray);
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_trayMenu, menu))
            {
                _trayMenu = null;
            }
        };
        _trayMenu = menu;
        menu.ShowNearCursor();
    }

    /// <summary>托盘“退出”：走正常关机流程，确保设置与状态落盘。</summary>
    private void ShutdownTray()
    {
        Dispatcher.BeginInvoke(new Action(Shutdown));
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        bool autoStart;
        try
        {
            autoStart = AutoStartManager.IsEnabled();
        }
        catch (Exception error)
        {
            AppLog.Write($"autostart read failed: {error.Message}");
            autoStart = false;
        }

        var draft = new AppSettings
        {
            Hotkey = _settings.Hotkey,
            AutoStart = autoStart,
            WindowWidth = _settings.WindowWidth,
            WindowHeight = _settings.WindowHeight,
            IdleReleaseMinutes = _settings.IdleReleaseMinutes,
        };

        var window = new SettingsWindow(draft);
        if (_mainWindow is { IsVisible: true })
        {
            window.Owner = _mainWindow;
        }

        _settingsWindow = window;
        try
        {
            if (window.ShowDialog() == true)
            {
                ApplySettings(draft);
            }
        }
        finally
        {
            _settingsWindow = null;
        }
    }

    private void ApplySettings(AppSettings draft)
    {
        if (!HotkeyDefinition.TryParse(draft.Hotkey, out var hotkey, out var error))
        {
            System.Windows.MessageBox.Show(error ?? "热键格式不正确", "翻译设置");
            return;
        }

        var previousHotkey = _settings.Hotkey;
        if (_hotkeys is null || !_hotkeys.Register(hotkey))
        {
            if (_hotkeys is not null
                && HotkeyDefinition.TryParse(previousHotkey, out var previous, out _))
            {
                _hotkeys.Register(previous);
            }

            System.Windows.MessageBox.Show(
                $"热键 {hotkey} 注册失败，可能被其他程序占用。",
                "翻译设置");
            return;
        }

        try
        {
            var executablePath = Environment.ProcessPath;
            if (draft.AutoStart)
            {
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    throw new InvalidOperationException("无法确定程序路径");
                }

                AutoStartManager.SetEnabled(true, executablePath);
            }
            else
            {
                AutoStartManager.SetEnabled(false, executablePath ?? string.Empty);
            }

            _settings.Hotkey = hotkey.ToString();
            _settings.AutoStart = draft.AutoStart;
            _settings.IdleReleaseMinutes = draft.IdleReleaseMinutes;
            SaveSettings();
            if (_mainWindow is not { IsVisible: true })
            {
                // 窗口隐藏时新时长立即生效：重新武装（或关闭）空闲释放。
                ArmIdleRelease();
            }

            _mainWindow?.SetStatus("设置已保存");
            _mainWindow?.SetHotkeyHint(hotkey.ToString());
            AppLog.Write(
                $"settings saved hotkey={_settings.Hotkey} autostart={_settings.AutoStart} " +
                $"idleRelease={_settings.IdleReleaseMinutes}min");
        }
        catch (Exception exception)
        {
            AppLog.Write($"apply settings failed: {exception.Message}");
            System.Windows.MessageBox.Show(
                $"保存设置失败: {exception.Message}",
                "翻译设置");
        }
    }

    private void SaveSettings()
    {
        if (_settingsStore is null)
        {
            return;
        }

        if (_mainWindow is not null)
        {
            var (left, top, width, height) = _mainWindow.GetPersistedBounds();
            // 窗口从未显示过（--tray 启动后直接退出）时不要覆盖已保存的位置。
            if (!double.IsNaN(left))
            {
                _settings.WindowLeft = left;
            }

            if (!double.IsNaN(top))
            {
                _settings.WindowTop = top;
            }

            _settings.WindowWidth = width;
            _settings.WindowHeight = height;
        }

        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception error)
        {
            AppLog.Write($"save settings failed: {error.Message}");
        }
    }

    public void SaveWindowState()
    {
        SaveSettings();
    }

    public void ShowMainWindow()
    {
        AppLog.Write("show main window");
        _mainWindow?.ShowWindow();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SaveSettings();
        _idleTimer.Stop();
        _showListenerCts?.Cancel();
        _showListenerCts?.Dispose();
        _showEvent?.Dispose();
        _hotkeys?.Dispose();
        _trayMenu?.Close();
        _tray?.Dispose();
        _ocr?.Dispose();
        Dictionary.Dispose();
        Core?.Dispose();

        if (_instanceMutex is not null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 当前实例未持有 mutex（理论上不会发生）。
            }

            _instanceMutex.Dispose();
        }

        base.OnExit(e);
    }
}
