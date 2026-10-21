using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Translation.App;

public partial class MainWindow : Window
{
    private readonly App _app;
    private string _dictionaryText = string.Empty;

    public MainWindow(App app, AppSettings settings)
    {
        _app = app;
        InitializeComponent();
        Width = Math.Max(MinWidth, settings.WindowWidth);
        Height = Math.Max(MinHeight, settings.WindowHeight);
        if (settings.WindowLeft is { } left && settings.WindowTop is { } top)
        {
            Left = left;
            Top = top;
        }
    }

    public void ShowInitial()
    {
        ShowWindow();
    }

    public void ShowWindow()
    {
        ShowRestored();
        Activate();
        NativeMethods.ForceForeground(new WindowInteropHelper(this).Handle);
    }

    public (double Left, double Top, double Width, double Height) GetPersistedBounds()
    {
        var normal = WindowState == System.Windows.WindowState.Normal;
        var left = normal ? Left : RestoreBounds.Left;
        var top = normal ? Top : RestoreBounds.Top;
        var width = normal ? Width : RestoreBounds.Width;
        var height = normal ? Height : RestoreBounds.Height;
        if (width <= 0 || double.IsNaN(width))
        {
            width = MinWidth;
        }

        if (height <= 0 || double.IsNaN(height))
        {
            height = MinHeight;
        }

        return (left, top, Math.Max(MinWidth, width), Math.Max(MinHeight, height));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var preference = 2; // DWMWCP_ROUND (Windows 11)
        _ = DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int valueSize);

    public void ShowForText(string? clipboardText, Stopwatch hotkeyWatch)
    {
        if (!string.IsNullOrWhiteSpace(clipboardText))
        {
            SourceBox.Text = clipboardText.Length > 20000
                ? clipboardText[..20000]
                : clipboardText;
        }
        else
        {
            SourceBox.Text = string.Empty;
            ResultBox.Text = string.Empty;
            ShowResultPanel();
        }

        UpdateDirection();
        var windowMs = ShowWindowAndMeasure(hotkeyWatch);

        if (string.IsNullOrWhiteSpace(SourceBox.Text))
        {
            StatusText.Text = "剪切板为空，可直接输入";
            return;
        }

        SourceBox.ScrollToHome();
        _ = TranslateAsync(windowMs);
    }

    public void ShowForImage(BitmapSource image, Stopwatch hotkeyWatch)
    {
        SourceBox.Text = string.Empty;
        ResultBox.Text = string.Empty;
        ShowResultPanel();
        UpdateDirection();
        StatusText.Text = "正在识别图片…";
        var windowMs = ShowWindowAndMeasure(hotkeyWatch);
        _ = RecognizeImageAsync(image, windowMs);
    }

    private long ShowWindowAndMeasure(Stopwatch hotkeyWatch)
    {
        ShowRestored();
        Activate();
        NativeMethods.ForceForeground(new WindowInteropHelper(this).Handle);
        SourceBox.Focus();
        UpdateLayout();

        hotkeyWatch.Stop();
        var windowMs = hotkeyWatch.ElapsedMilliseconds;
        Log($"hotkey->window {windowMs}ms");
        return windowMs;
    }

    private async Task RecognizeImageAsync(BitmapSource image, long windowMs)
    {
        var ocrWatch = Stopwatch.StartNew();
        try
        {
            var ocr = await _app.EnsureOcrAsync();
            var text = await Task.Run(() => ocr.Recognize(image));
            ocrWatch.Stop();

            SourceBox.Text = text.Trim();
            SourceBox.CaretIndex = SourceBox.Text.Length;
            SourceBox.ScrollToHome();
            UpdateDirection();
            Log(
                $"ocr {image.PixelWidth}x{image.PixelHeight} " +
                $"chars={SourceBox.Text.Length} {ocrWatch.ElapsedMilliseconds}ms");

            if (string.IsNullOrWhiteSpace(SourceBox.Text))
            {
                ResultBox.Text = string.Empty;
                ShowResultPanel();
                StatusText.Text = "图片中未识别到文字";
                return;
            }

            await TranslateAsync(windowMs + ocrWatch.ElapsedMilliseconds);
        }
        catch (Exception error)
        {
            ocrWatch.Stop();
            ResultBox.Text = string.Empty;
            ShowResultPanel();
            StatusText.Text = $"OCR 失败: {error.Message}";
            Log($"ocr failed: {error.Message}");
        }
    }

    private void ShowRestored()
    {
        // 恢复上次的位置；首次运行或原位置已不在任何显示器内时居中。
        if (!double.IsNaN(Left) && !double.IsNaN(Top) && IsPositionVisible())
        {
            Show();
            return;
        }

        ShowCentered();
    }

    private bool IsPositionVisible()
    {
        var virtualRight = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
        var x = Left + 80;
        var y = Top + 20;
        return x >= SystemParameters.VirtualScreenLeft
            && x <= virtualRight
            && y >= SystemParameters.VirtualScreenTop
            && y <= virtualBottom;
    }

    private void ShowCentered()
    {
        // Phase 0 原型：在主要工作区居中显示，保证多屏/混合 DPI 下不被裁切。
        // “跟随鼠标所在显示器”留到 Phase 2，用 GetMonitorInfo + 目标显示器 DPI 实现。
        Show();
        UpdateLayout();

        var area = SystemParameters.WorkArea;
        Left = area.Left + Math.Max(12, (area.Width - ActualWidth) / 2);
        Top = area.Top + Math.Max(12, (area.Height - ActualHeight) / 3);
    }

    private async Task TranslateAsync(long windowMs = -1)
    {
        var text = SourceBox.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        var language = LanguageDetector.Detect(text);
        if (language == DetectedLanguage.Unsupported)
        {
            ResultBox.Text = string.Empty;
            ShowResultPanel();
            StatusText.Text = "暂不支持该语言";
            return;
        }

        if (language == DetectedLanguage.Empty)
        {
            ResultBox.Text = string.Empty;
            ShowResultPanel();
            StatusText.Text = "没有可翻译的文本";
            return;
        }

        UpdateDirection();
        StatusText.Text = "翻译中…";
        var stopwatch = Stopwatch.StartNew();

        // 单个英文单词优先走离线词典（不依赖翻译模型加载）；未命中回退句子翻译。
        if (language == DetectedLanguage.English && WordPattern.IsSingleWord(text))
        {
            var entry = await LookupDictionaryAsync(text);
            if (entry is not null)
            {
                ShowDictionaryEntry(entry);
                stopwatch.Stop();
                StatusText.Text = $"词典 · {stopwatch.Elapsed.TotalMilliseconds:F0}ms · 离线";
                Log($"dictionary '{entry.Word}' {stopwatch.Elapsed.TotalMilliseconds:F0}ms");
                return;
            }
        }

        try
        {
            await _app.EnsureCoreReadyAsync();
            var (translated, elapsedMs) = await _app.Core!.TranslateAsync(text, language);
            ShowResultPanel();
            ResultBox.Text = translated;
            ResultBox.ScrollToHome();
            stopwatch.Stop();

            var totalMs = windowMs >= 0 ? windowMs + stopwatch.ElapsedMilliseconds : stopwatch.ElapsedMilliseconds;
            var direction = language == DetectedLanguage.Chinese ? "中 → 英" : "英 → 中";
            StatusText.Text = $"{direction} · {elapsedMs:F0}ms · 离线";
            Log($"translate {elapsedMs:F0}ms hotkey->result {totalMs}ms");
        }
        catch (Exception error)
        {
            stopwatch.Stop();
            ResultBox.Text = string.Empty;
            ShowResultPanel();
            StatusText.Text = $"翻译失败: {error.Message}";
            Log($"translate failed: {error.Message}");
        }
    }

    private async Task<DictionaryEntry?> LookupDictionaryAsync(string text)
    {
        try
        {
            return await _app.Dictionary.LookupAsync(text);
        }
        catch (Exception error)
        {
            // 词典查询失败不阻塞翻译，回退句子翻译。
            Log($"dictionary failed: {error.Message}");
            return null;
        }
    }

    private void ShowDictionaryEntry(DictionaryEntry entry)
    {
        ResultLabel.Text = "词典";
        ResultBox.Visibility = Visibility.Collapsed;
        DictionaryPanel.Visibility = Visibility.Visible;

        _dictionaryText = entry.ToPlainText();
        DictionaryPanel.Document = BuildDocument(entry);
    }

    /// <summary>把词条渲染为富文本文档；富文本支持用户自由拖选复制。</summary>
    private static FlowDocument BuildDocument(DictionaryEntry entry)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
        };

        var header = new Paragraph { Margin = new Thickness(0) };
        header.Inlines.Add(new Run(entry.Word)
        {
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("#F2F2F2"),
        });
        if (!string.IsNullOrEmpty(entry.Phonetic))
        {
            header.Inlines.Add(new Run($" /{entry.Phonetic}/")
            {
                FontSize = 13,
                Foreground = ThemeBrush("#9A9A9A"),
            });
        }

        document.Blocks.Add(header);

        var tags = BuildTagsText(entry);
        if (tags.Length > 0)
        {
            document.Blocks.Add(new Paragraph(new Run(tags)
            {
                FontSize = 11,
                Foreground = ThemeBrush("#6F6F6F"),
            })
            { Margin = new Thickness(0, 4, 0, 0) });
        }

        if (!string.IsNullOrEmpty(entry.InflectionNote))
        {
            document.Blocks.Add(new Paragraph(new Run(entry.InflectionNote)
            {
                FontSize = 12,
                Foreground = ThemeBrush("#2F6FED"),
            })
            { Margin = new Thickness(0, 6, 0, 0) });
        }

        foreach (var sense in entry.Senses)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 2, 0, 0), LineHeight = 22 };
            if (sense.Prefix.Length > 0)
            {
                paragraph.Inlines.Add(new Run(sense.Prefix)
                {
                    FontSize = 14,
                    Foreground = ThemeBrush("#8C8C8C"),
                });
            }

            paragraph.Inlines.Add(new Run(sense.Display)
            {
                FontSize = 14,
                Foreground = ThemeBrush("#D8D8D8"),
            });
            document.Blocks.Add(paragraph);
        }

        if (entry.Forms.Count > 0)
        {
            document.Blocks.Add(new Paragraph(new Run("词形变化")
            {
                FontSize = 12,
                Foreground = ThemeBrush("#8C8C8C"),
            })
            { Margin = new Thickness(0, 12, 0, 2) });

            foreach (var form in entry.Forms)
            {
                var paragraph = new Paragraph { Margin = new Thickness(0, 2, 0, 0) };
                paragraph.Inlines.Add(new Run(form.Label + " ")
                {
                    FontSize = 13,
                    Foreground = ThemeBrush("#8C8C8C"),
                });
                paragraph.Inlines.Add(new Run(form.Word)
                {
                    FontSize = 13,
                    Foreground = ThemeBrush("#D8D8D8"),
                });
                document.Blocks.Add(paragraph);
            }
        }

        return document;
    }

    private static string BuildTagsText(DictionaryEntry entry)
    {
        var parts = new List<string>();
        if (entry.Collins > 0)
        {
            parts.Add(new string('★', Math.Min(entry.Collins, 5)).PadRight(5, '☆'));
        }

        if (entry.Oxford)
        {
            parts.Add("牛津3000");
        }

        if (entry.Tags.Count > 0)
        {
            parts.Add(string.Join(" ", entry.Tags));
        }

        return string.Join(" · ", parts);
    }

    private static System.Windows.Media.Brush ThemeBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private void ShowResultPanel()
    {
        ResultLabel.Text = "译文";
        ResultBox.Visibility = Visibility.Visible;
        DictionaryPanel.Visibility = Visibility.Collapsed;
    }

    private void UpdateDirection()
    {
        var language = LanguageDetector.Detect(SourceBox.Text);
        DirectionText.Text = language switch
        {
            DetectedLanguage.Chinese => "中 → 英",
            DetectedLanguage.English => "英 → 中",
            DetectedLanguage.Unsupported => "不支持",
            _ => "自动",
        };
    }

    private void Translate_Click(object sender, RoutedEventArgs e)
    {
        _ = TranslateAsync();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        // 复制当前可见面板的内容：词典模式复制词典纯文本，否则复制译文。
        var dictionaryVisible = DictionaryPanel.Visibility == Visibility.Visible;
        var text = dictionaryVisible ? _dictionaryText : ResultBox.Text;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(text);
            StatusText.Text = dictionaryVisible ? "词典结果已复制" : "译文已复制";
        }
        catch (Exception error)
        {
            StatusText.Text = $"复制失败: {error.Message}";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        HideWindow();
    }

    private void HideWindow()
    {
        _app.SaveWindowState();
        Hide();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void SourceBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // IME 候选框打开时 Enter 由输入法处理，不触发翻译。
        if (e.Key == Key.ImeProcessed)
        {
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            _ = TranslateAsync();
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideWindow();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _ = TranslateAsync();
            e.Handled = true;
        }
    }

    public void SetStatus(string message)
    {
        StatusText.Text = message;
    }

    private static void Log(string message)
    {
        AppLog.Write(message);
    }
}
