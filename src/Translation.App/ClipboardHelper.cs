using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace Translation.App;

public sealed record ClipboardContent(string? Text, BitmapSource? Image);

internal static class ClipboardHelper
{
    private const int Attempts = 5;

    public static ClipboardContent TryGetContent()
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            try
            {
                if (System.Windows.Clipboard.ContainsText(System.Windows.TextDataFormat.UnicodeText))
                {
                    return new ClipboardContent(
                        System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.UnicodeText),
                        null);
                }

                if (System.Windows.Clipboard.ContainsImage())
                {
                    var image = System.Windows.Clipboard.GetImage();
                    if (image is not null)
                    {
                        if (image.CanFreeze)
                        {
                            image.Freeze();
                        }

                        return new ClipboardContent(null, image);
                    }
                }

                return new ClipboardContent(null, null);
            }
            catch (ExternalException)
            {
                // 剪贴板被其他进程占用，短暂重试（最多约 50ms，避免阻塞 UI）。
                Thread.Sleep(10);
            }
        }

        return new ClipboardContent(null, null);
    }

    public static bool TrySetText(string text)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(10);
            }
        }

        return false;
    }
}
