using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;

namespace Translation.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\TranslationApp.SingleInstance";
    private const string ShowEventName = @"Local\TranslationApp.Show";

    private WinForms.NotifyIcon? _tray;
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

        Core = new TranslationCore(
            Paths.TranslationModel("opus-mt-zh-en-int8"),
            Paths.TranslationModel("opus-mt-en-zh-int8"));

        _mainWindow = new MainWindow(this, _settings);
        var startHidden = Array.Exists(
            e.Args,
            argument => string.Equals(argument, "--tray", StringComparison.OrdinalIgnoreCase));
        if (!startHidden)
        {
            _mainWindow.ShowInitial();
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
        _coreInitTask = InitializeCoreAsync();
        _ = WarmOcrAsync();
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
        if (_coreInitTask is null)
        {
            throw new InvalidOperationException("翻译核心尚未初始化");
        }

        await _coreInitTask;
        if (!CoreReady)
        {
            throw new InvalidOperationException(CoreError ?? "翻译模型尚未加载完成");
        }
    }

    private async Task WarmOcrAsync()
    {
        try
        {
            await EnsureOcrAsync();
            AppLog.Write("ocr warm");
        }
        catch (Exception error)
        {
            AppLog.Write($"ocr warm failed: {error.Message}");
        }
    }

    public async Task<OcrService> EnsureOcrAsync()
    {
        _ocrInitTask ??= Task.Run(() => new OcrService(Paths.OcrModels));
        _ocr = await _ocrInitTask;
        return _ocr;
    }

    private void OnTranslateHotkey()
    {
        AppLog.Write("translate hotkey fired");
        var stopwatch = Stopwatch.StartNew();
        var content = ClipboardHelper.TryGetContent();

        if (content.Text is not null)
        {
            AppLog.Write($"clipboard text length={content.Text.Length}");
            _mainWindow?.ShowForText(content.Text, stopwatch);
        }
        else if (content.Image is not null)
        {
            AppLog.Write($"clipboard image {content.Image.PixelWidth}x{content.Image.PixelHeight}");
            _mainWindow?.ShowForImage(content.Image, stopwatch);
        }
        else
        {
            AppLog.Write("clipboard empty");
            _mainWindow?.ShowForText(null, stopwatch);
        }
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

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("显示", null, (_, _) => ShowMainWindow());
        menu.Items.Add("设置", null, (_, _) => OpenSettings());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Shutdown());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowMainWindow();
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
            SaveSettings();
            _mainWindow?.SetStatus("设置已保存");
            AppLog.Write($"settings saved hotkey={_settings.Hotkey} autostart={_settings.AutoStart}");
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
        _showListenerCts?.Cancel();
        _showListenerCts?.Dispose();
        _showEvent?.Dispose();
        _hotkeys?.Dispose();
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
