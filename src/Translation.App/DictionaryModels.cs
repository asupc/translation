using System.Text;

namespace Translation.App;

/// <summary>词典释义行：词性前缀（如 "n."）、领域标签（如 "[医]"）与释义正文。</summary>
public sealed record SenseLine(string Pos, string Domain, string Text)
{
    /// <summary>词性前缀的展示形式；空词性返回空串，否则带尾随空格。</summary>
    public string Prefix => Pos.Length == 0 ? string.Empty : Pos + " ";

    /// <summary>领域标签 + 释义正文，供 XAML 绑定。</summary>
    public string Display => (Domain.Length == 0 ? string.Empty : Domain + " ") + Text;
}

/// <summary>词条自身的词形变化，如 (p, ran) 表示过去式 ran。</summary>
public sealed record WordForm(string Kind, string Word)
{
    /// <summary>词形类型的中文标签，如 "过去式"。</summary>
    public string Label => EntryParser.KindLabel(Kind);
}

/// <summary>一次词典查询的完整结果。</summary>
public sealed class DictionaryEntry
{
    public required string Query { get; init; }

    /// <summary>展示的词条；输入是变形词时为还原后的原词。</summary>
    public required string Word { get; init; }

    public string? Phonetic { get; init; }

    /// <summary>柯林斯星级 0-5。</summary>
    public int Collins { get; init; }

    /// <summary>是否牛津 3000 核心词。</summary>
    public bool Oxford { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }
    public required IReadOnlyList<SenseLine> Senses { get; init; }
    public required IReadOnlyList<WordForm> Forms { get; init; }

    /// <summary>变形词提示，如 "classes 是 class 的名词复数"；直接命中时为 null。</summary>
    public string? InflectionNote { get; init; }

    /// <summary>面板内容的纯文本，供复制按钮使用。</summary>
    public string ToPlainText()
    {
        var builder = new StringBuilder();
        builder.Append(Word);
        if (!string.IsNullOrEmpty(Phonetic))
        {
            builder.Append(" /").Append(Phonetic).Append('/');
        }

        if (!string.IsNullOrEmpty(InflectionNote))
        {
            builder.AppendLine().Append(InflectionNote);
        }

        foreach (var sense in Senses)
        {
            builder.AppendLine().Append(sense.Prefix).Append(sense.Display);
        }

        if (Forms.Count > 0)
        {
            builder.AppendLine().Append("词形变化:");
            foreach (var form in Forms)
            {
                builder.AppendLine().Append(form.Label).Append(' ').Append(form.Word);
            }
        }

        return builder.ToString();
    }
}
