using System.Text.RegularExpressions;

namespace Translation.App;

/// <summary>判断输入是否"单个英文单词"，并做查词前的规整。</summary>
public static partial class WordPattern
{
    private const int MaxWordLength = 64;

    [GeneratedRegex(@"^[A-Za-z][A-Za-z.'\-]*$")]
    private static partial Regex SingleWordRegex();

    /// <summary>剥掉首尾引号与尾部句读（"class." -> "class"），保留内部的 ' . -（don't / U.S. / state-of-the-art）。</summary>
    public static string Normalize(string text)
    {
        var value = text.Trim().Trim('\'', '"', '‘', '’', '“', '”');
        value = value.TrimEnd('.', ',', ';', ':', '!', '?');
        return value.Trim('\'', '"', '‘', '’', '“', '”');
    }

    public static bool IsSingleWord(string text)
    {
        var value = Normalize(text);
        return value.Length is > 0 and <= MaxWordLength && SingleWordRegex().IsMatch(value);
    }
}

/// <summary>解析 ECDICT 的 translation / exchange 原始字段。所有方法对脏数据容错，不抛异常。</summary>
public static partial class EntryParser
{
    // 释义行的词性前缀白名单（n. / vt. / adj. ...）。
    [GeneratedRegex(@"^(?:n|v|vt|vi|adj|adv|a|prep|conj|pron|int|num|art|aux|auxv|abbr)\.\s*")]
    private static partial Regex PosPrefixRegex();

    // 释义行的领域标签，如 [化] [医] [网络]。
    [GeneratedRegex(@"^\[[^\]]{1,6}\]\s*")]
    private static partial Regex DomainRegex();

    // 词形变化的固定展示顺序：过去式、过去分词、现在分词、三单、复数、比较级、最高级。
    private static readonly string[] KindOrder = { "p", "d", "i", "3", "s", "r", "t" };

    /// <summary>把多行中文释义拆成释义行；分隔符兼容字面 "\n"（ECDICT 实际存储）与真实换行。</summary>
    public static IReadOnlyList<SenseLine> ParseTranslation(string? translation)
    {
        if (string.IsNullOrWhiteSpace(translation))
        {
            return Array.Empty<SenseLine>();
        }

        var lines = translation.Split(new[] { "\\n", "\r\n", "\n" }, StringSplitOptions.TrimEntries);
        var senses = new List<SenseLine>();
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var rest = line;
            var pos = string.Empty;
            var prefix = PosPrefixRegex().Match(rest);
            if (prefix.Success)
            {
                pos = prefix.Value.TrimEnd();
                rest = rest[prefix.Length..];
            }

            var domain = string.Empty;
            var tag = DomainRegex().Match(rest);
            if (tag.Success)
            {
                domain = tag.Value.TrimEnd();
                rest = rest[tag.Length..];
            }

            senses.Add(new SenseLine(pos, domain, rest));
        }

        return senses;
    }

    /// <summary>拆解词形变化字段 "p:did/d:done"；只取七类词形，按固定顺序输出并去重。</summary>
    public static IReadOnlyList<WordForm> ParseExchange(string? exchange)
    {
        if (string.IsNullOrWhiteSpace(exchange))
        {
            return Array.Empty<WordForm>();
        }

        var forms = new List<WordForm>();
        foreach (var part in exchange.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var kind = part[..separator];
            var word = part[(separator + 1)..];
            if (KindOrder.Contains(kind) && word.Length > 0 && forms.All(form => form.Kind != kind))
            {
                forms.Add(new WordForm(kind, word));
            }
        }

        forms.Sort((x, y) => KindOrder.IndexOf(x.Kind) - KindOrder.IndexOf(y.Kind));
        return forms;
    }

    /// <summary>词形类型的中文标签。</summary>
    public static string KindLabel(string kind) => kind switch
    {
        "p" => "过去式",
        "d" => "过去分词",
        "i" => "现在分词",
        "3" => "第三人称单数",
        "s" => "名词复数",
        "r" => "比较级",
        "t" => "最高级",
        _ => "词形变化",
    };
}
