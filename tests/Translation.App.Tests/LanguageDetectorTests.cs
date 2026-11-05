using Translation.App;
using Xunit;

namespace Translation.App.Tests;

public class LanguageDetectorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t\r\n")]
    [InlineData("123 !?")]
    public void Detect_空文本(string? text)
    {
        Assert.Equal(DetectedLanguage.Empty, LanguageDetector.Detect(text!));
    }

    [Theory]
    [InlineData("你好")]
    [InlineData("你好，世界")]
    [InlineData("这是一个测试")]
    [InlineData("hello 世界")]     // 汉字占比达到阈值按中文
    [InlineData("𠀀")]             // CJK 扩展 B（代理对）
    public void Detect_中文(string text)
    {
        Assert.Equal(DetectedLanguage.Chinese, LanguageDetector.Detect(text));
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("Hello, world!")]
    [InlineData("café")]
    [InlineData("naïve")]
    [InlineData("ｈｅｌｌｏ")]     // 全角经 FormKC 归一化为 ASCII
    public void Detect_英文(string text)
    {
        Assert.Equal(DetectedLanguage.English, LanguageDetector.Detect(text));
    }

    [Theory]
    [InlineData("こんにちは")]     // 日文假名
    [InlineData("안녕하세요")]     // 韩文
    [InlineData("привет")]         // 西里尔
    [InlineData("γειά")]           // 希腊
    [InlineData("مرحبا")]          // 阿拉伯
    public void Detect_不支持的语言(string text)
    {
        Assert.Equal(DetectedLanguage.Unsupported, LanguageDetector.Detect(text));
    }
}
