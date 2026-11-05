using System.Text;

namespace Translation.App;

public enum DetectedLanguage
{
    Empty,
    Chinese,
    English,
    Unsupported,
}

public static class LanguageDetector
{
    public static DetectedLanguage Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return DetectedLanguage.Empty;
        }

        var normalized = text.Normalize(NormalizationForm.FormKC);
        var cjk = 0;
        var latin = 0;

        foreach (var rune in normalized.EnumerateRunes())
        {
            var value = rune.Value;
            if (IsCjk(value))
            {
                cjk++;
            }
            else if (IsJapaneseOrKorean(value))
            {
                return DetectedLanguage.Unsupported;
            }
            else if (Rune.IsLetter(rune))
            {
                // Latin / Latin-1 Supplement / Latin Extended-A/B（含 é、ñ 等）。
                if (value < 0x0250)
                {
                    latin++;
                }
                else
                {
                    // 希腊、西里尔、阿拉伯、希伯来等当前模型不支持。
                    return DetectedLanguage.Unsupported;
                }
            }
        }

        // 中英混排时汉字占有效字母的 20% 以上即按中文处理。
        if (cjk > 0 && cjk * 5 >= cjk + latin)
        {
            return DetectedLanguage.Chinese;
        }

        return latin > 0 ? DetectedLanguage.English : DetectedLanguage.Empty;
    }

    private static bool IsCjk(int value)
        => (value >= 0x4E00 && value <= 0x9FFF)      // CJK 统一表意文字
            || (value >= 0x3400 && value <= 0x4DBF)  // 扩展 A
            || (value >= 0x20000 && value <= 0x2FA1F) // 扩展 B-F 及兼容补充
            || (value >= 0xF900 && value <= 0xFAFF);  // 兼容表意文字

    private static bool IsJapaneseOrKorean(int value)
        => (value >= 0x3040 && value <= 0x30FF)      // 平假名/片假名
            || (value >= 0x1100 && value <= 0x11FF)  // 谚文字母
            || (value >= 0xA960 && value <= 0xA97F)
            || (value >= 0xAC00 && value <= 0xD7AF)  // 谚文音节
            || (value >= 0xD7B0 && value <= 0xD7FF);
}
