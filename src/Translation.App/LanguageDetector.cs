using System.Globalization;
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

        foreach (var ch in normalized)
        {
            if ((ch >= '一' && ch <= '鿿') || (ch >= '㐀' && ch <= '䶿'))
            {
                cjk++;
            }
            else if ((ch >= '぀' && ch <= 'ヿ') || (ch >= '가' && ch <= '힯'))
            {
                return DetectedLanguage.Unsupported;
            }
            else if (char.IsLetter(ch) && ch < 'ɐ')
            {
                latin++;
            }
        }

        if (cjk > 0 && cjk * 5 >= cjk + latin)
        {
            return DetectedLanguage.Chinese;
        }

        return latin > 0 ? DetectedLanguage.English : DetectedLanguage.Empty;
    }
}
