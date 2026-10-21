using Translation.App;
using Xunit;

namespace Translation.App.Tests;

public class WordPatternTests
{
    [Theory]
    [InlineData("class")]
    [InlineData("Class")]
    [InlineData("don't")]
    [InlineData("state-of-the-art")]
    [InlineData("U.S.")]
    [InlineData("class.")]        // 热键场景：剪切板单词带尾部句读
    [InlineData("class,")]
    [InlineData("\"class\"")]     // 首尾引号
    public void IsSingleWord_接受单个英文单词(string text)
    {
        Assert.True(WordPattern.IsSingleWord(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("the class is big")]
    [InlineData("e-mail address")]
    [InlineData("3rd")]
    [InlineData("班级")]
    public void IsSingleWord_拒绝非单个单词(string text)
    {
        Assert.False(WordPattern.IsSingleWord(text));
    }

    [Fact]
    public void Normalize_剥掉首尾引号与尾部句读()
    {
        Assert.Equal("class", WordPattern.Normalize("\"class.\""));
        Assert.Equal("don't", WordPattern.Normalize("don't"));
    }
}
