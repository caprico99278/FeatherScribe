using FeatherScribe.Core;

namespace FeatherScribe.Tests;

public class HotkeyParserTests
{
    [Fact]
    public void TryParse_CtrlAltSpace()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+Space", out var definition));
        Assert.Equal(
            HotkeyParser.Modifiers.Control | HotkeyParser.Modifiers.Alt,
            definition.Modifiers);
        Assert.Equal(0x20u, definition.VirtualKey);
    }

    [Fact]
    public void TryParse_CtrlAltShiftSpace()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+Shift+Space", out var definition));
        Assert.Equal(
            HotkeyParser.Modifiers.Control | HotkeyParser.Modifiers.Alt | HotkeyParser.Modifiers.Shift,
            definition.Modifiers);
    }

    [Theory]
    [InlineData("Ctrl+Alt+M", 'M')]
    [InlineData("ctrl+alt+m", 'M')]
    [InlineData("Ctrl+Alt+B", 'B')]
    public void TryParse_LetterKeys_CaseInsensitive(string text, char expected)
    {
        Assert.True(HotkeyParser.TryParse(text, out var definition));
        Assert.Equal((uint)expected, definition.VirtualKey);
    }

    [Fact]
    public void TryParse_FunctionKey()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+F12", out var definition));
        Assert.Equal(0x7Bu, definition.VirtualKey); // VK_F12
    }

    [Fact]
    public void TryParse_CtrlShiftF8_DefaultNoFormatHotkey()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Shift+F8", out var definition));
        Assert.Equal(
            HotkeyParser.Modifiers.Control | HotkeyParser.Modifiers.Shift,
            definition.Modifiers);
        Assert.Equal(0x77u, definition.VirtualKey); // VK_F8
    }

    [Fact]
    public void TryParse_CtrlAltShiftM_DefaultMemoHotkey()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+Shift+M", out var definition));
        Assert.Equal(
            HotkeyParser.Modifiers.Control | HotkeyParser.Modifiers.Alt | HotkeyParser.Modifiers.Shift,
            definition.Modifiers);
        Assert.Equal((uint)'M', definition.VirtualKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl++")]
    [InlineData("Ctrl+Alt+漢")]
    [InlineData("Ctrl+A+B")]
    public void TryParse_Invalid_ReturnsFalse(string? text)
    {
        Assert.False(HotkeyParser.TryParse(text, out _));
    }
}
