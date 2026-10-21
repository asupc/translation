using Translation.App;
using Xunit;

namespace Translation.App.Tests;

public class EntryParserTests
{
    [Fact]
    public void ParseTranslation_按字面n分隔并提取词性前缀()
    {
        // ECDICT 实际存储的是字面 "\n"（反斜杠+n 两个字符）。
        var senses = EntryParser.ParseTranslation(@"n. 班级\nn. 阶级\nvt. 把…分类");

        Assert.Equal(3, senses.Count);
        Assert.Equal("n.", senses[0].Pos);
        Assert.Equal("班级", senses[0].Text);
        Assert.Equal("vt.", senses[2].Pos);
        Assert.Equal("把…分类", senses[2].Text);
    }

    [Fact]
    public void ParseTranslation_兼容真实换行与领域标签()
    {
        var senses = EntryParser.ParseTranslation("[计] 类别\nn. 种类");

        Assert.Equal(2, senses.Count);
        Assert.Equal("[计]", senses[0].Domain);
        Assert.Equal("类别", senses[0].Text);
        Assert.Equal("n.", senses[1].Pos);
        Assert.Equal("种类", senses[1].Text);
    }

    [Fact]
    public void ParseTranslation_空与无前缀行不抛异常()
    {
        Assert.Empty(EntryParser.ParseTranslation(null));
        Assert.Empty(EntryParser.ParseTranslation("  "));

        var senses = EntryParser.ParseTranslation("无前缀释义");
        var sense = Assert.Single(senses);
        Assert.Equal(string.Empty, sense.Pos);
        Assert.Equal("无前缀释义", sense.Text);
    }

    [Fact]
    public void ParseExchange_七类词形按固定顺序输出()
    {
        var forms = EntryParser.ParseExchange("s:classes/3:classes/p:classified/i:classifying/d:classified");

        Assert.Equal(new[] { "p", "d", "i", "3", "s" }, forms.Select(form => form.Kind).ToArray());
        Assert.Equal("classified", forms[0].Word);
        Assert.Equal("classes", forms[4].Word);
    }

    [Fact]
    public void ParseExchange_忽略未知键与01键()
    {
        Assert.Empty(EntryParser.ParseExchange("0:class/1:whatever"));

        var forms = EntryParser.ParseExchange("p:ran/x:bad");
        var form = Assert.Single(forms);
        Assert.Equal("ran", form.Word);
        Assert.Empty(EntryParser.ParseExchange(null));
    }

    [Theory]
    [InlineData("p", "过去式")]
    [InlineData("d", "过去分词")]
    [InlineData("i", "现在分词")]
    [InlineData("3", "第三人称单数")]
    [InlineData("s", "名词复数")]
    [InlineData("r", "比较级")]
    [InlineData("t", "最高级")]
    [InlineData("x", "词形变化")]
    public void KindLabel_映射中文标签(string kind, string label)
    {
        Assert.Equal(label, EntryParser.KindLabel(kind));
    }
}
