using ProfileKit.Core.Paths;

namespace ProfileKit.Core.Tests;

public class PathTokensTests
{
    [Theory]
    [InlineData(@"%APPDATA%\Notepad++", @"C:\Users\max\AppData\Roaming\Notepad++")]
    [InlineData(@"%LOCALAPPDATA%\Tool\x.ini", @"C:\Users\max\AppData\Local\Tool\x.ini")]
    [InlineData(@"%USERPROFILE%", @"C:\Users\max")]
    [InlineData(@"%programfiles%\WinRAR\rarreg.key", @"C:\Program Files\WinRAR\rarreg.key")]
    [InlineData(@"%PROGRAMFILES(X86)%\Tool", @"C:\Program Files (x86)\Tool")]
    public void Expand_resolves_tokens(string tokenized, string expected) =>
        Assert.Equal(expected, PathTokens.Expand(tokenized, Win.User("max")));

    [Theory]
    [InlineData(@"C:\Users\max\AppData\Roaming\Notepad++", @"%APPDATA%\Notepad++")]
    [InlineData(@"c:\users\MAX\appdata\local\Tool", @"%LOCALAPPDATA%\Tool")]
    [InlineData(@"C:\Users\max\AppData\LocalLow\X", @"%LOCALAPPDATALOW%\X")]
    [InlineData(@"C:\Users\max\.gitconfig", @"%USERPROFILE%\.gitconfig")]
    [InlineData(@"C:\Program Files (x86)\Foo", @"%PROGRAMFILES(X86)%\Foo")]
    [InlineData(@"C:/Users/max/AppData/Roaming/", @"%APPDATA%")]
    public void Tokenize_picks_most_specific_token(string absolute, string expected) =>
        Assert.Equal(expected, PathTokens.TryTokenize(absolute, Win.User("max")));

    [Fact]
    public void Tokenize_returns_null_outside_known_locations() =>
        Assert.Null(PathTokens.TryTokenize(@"D:\Daten\x", Win.User("max")));

    [Fact]
    public void Tokenize_does_not_match_similar_user_names() =>
        Assert.Null(PathTokens.TryTokenize(@"C:\Users\max2\AppData", Win.User("max")));

    [Theory]
    [InlineData(@"C:\absolut")]
    [InlineData(@"%UNBEKANNT%\x")]
    [InlineData(@"%APPDATA%\..\..\Windows")]
    [InlineData(@"%APPDATA")]
    public void Parse_rejects_invalid_paths(string path) =>
        Assert.Throws<FormatException>(() => PathTokens.Parse(path));

    [Fact]
    public void Package_path_uses_folder_names() =>
        Assert.Equal(@"PROGRAMFILESX86\Foo\a.ini", PathTokens.ToPackagePath(@"%PROGRAMFILES(X86)%\Foo\a.ini"));

    [Fact]
    public void User_scope_is_detected()
    {
        Assert.True(PathTokens.IsUserScoped(@"%APPDATA%\X"));
        Assert.False(PathTokens.IsUserScoped(@"%PROGRAMFILES%\X"));
    }
}
