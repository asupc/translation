using System.Windows.Media.Imaging;

namespace Translation.App;

public sealed record ClipboardContent(string? Text, BitmapSource? Image);

internal static class ClipboardHelper
{
    public static ClipboardContent TryGetContent()
    {
        for (var attempt = 0; attempt < 5; attempt++)
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
            catch (Exception)
            {
                Thread.Sleep(20);
            }
        }

        return new ClipboardContent(null, null);
    }
}
