using System.Windows.Input;
using Translation.App;
using Xunit;

namespace Translation.App.Tests;

public class HotkeyDefinitionTests
{
    [Fact]
    public void Default_是CtrlAltF9()
    {
        Assert.Equal("Ctrl+Alt+F9", HotkeyDefinition.Default.ToString());
    }

    [Theory]
    [InlineData("Ctrl+Alt+F9", "Ctrl+Alt+F9")]
    [InlineData("ctrl+f9", "Ctrl+F9")]
    [InlineData("Ctrl+Shift+A", "Ctrl+Shift+A")]
    [InlineData("Alt+1", "Alt+1")]
    [InlineData("Win+Space", "Win+Space")]
    public void TryParse_接受常见组合并规范化(string text, string expected)
    {
        Assert.True(HotkeyDefinition.TryParse(text, out var definition, out var error), error);
        Assert.Equal(expected, definition.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("F9")]                 // 缺少修饰键
    [InlineData("Ctrl+Alt")]           // 缺少主键
    [InlineData("Ctrl+A+B")]           // 两个主键
    [InlineData("Ctrl+Control")]       // 修饰键不能作为主键
    [InlineData("Ctrl+Shift")]         // 同上
    [InlineData("Ctrl+F25")]           // 超出 F1-F24
    [InlineData("Ctrl+Unknown")]
    public void TryParse_拒绝非法组合(string? text)
    {
        Assert.False(HotkeyDefinition.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void FromKeys_与TryParse结果一致()
    {
        var fromKeys = HotkeyDefinition.FromKeys(
            ModifierKeys.Control | ModifierKeys.Alt,
            Key.F9);

        Assert.True(HotkeyDefinition.TryParse("Ctrl+Alt+F9", out var parsed, out _));
        Assert.Equal(parsed, fromKeys);
    }

    [Fact]
    public void FromKeys_无主键时抛异常()
    {
        Assert.Throws<ArgumentException>(() =>
            HotkeyDefinition.FromKeys(ModifierKeys.Control, Key.None));
    }

    [Fact]
    public void TryParse_拒绝修饰键原名作为主键()
    {
        // "Menu" 是 Alt 的 WinForms 名称，不应被当作主键。
        Assert.False(HotkeyDefinition.TryParse("Ctrl+Menu", out _, out _));
        Assert.False(HotkeyDefinition.TryParse("Ctrl+ShiftKey", out _, out _));
    }
}
