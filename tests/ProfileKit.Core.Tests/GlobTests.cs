using ProfileKit.Core.Paths;
using ProfileKit.Core.Planning;

namespace ProfileKit.Core.Tests;

public class GlobTests
{
    [Theory]
    [InlineData("*.log", "a.log", true)]
    [InlineData("*.log", @"sub\deep\a.LOG", true)]
    [InlineData("*.log", "a.log.txt", false)]
    [InlineData(@"backup\**", @"backup\x\y.txt", true)]
    [InlineData(@"backup\**", @"other\backup\y.txt", false)]
    [InlineData(@"**\cache\*", @"a\b\cache\f", true)]
    [InlineData(@"**\cache\*", @"cache\f", true)]
    [InlineData("session.xml", "session.xml", true)]
    [InlineData("session.xml", "my-session.xml", false)]
    [InlineData("conf?.ini", "conf1.ini", true)]
    [InlineData("backup/**", "backup/a", true)]
    public void Matches(string pattern, string path, bool expected) => Assert.Equal(expected, new Glob(pattern).IsMatch(path));

    [Fact]
    public void Module_filter_supports_lists_and_patterns()
    {
        var f = new ModuleFilter("windows.*, 7zip");
        Assert.True(f.Matches("windows.mouse"));
        Assert.True(f.Matches("7zip"));
        Assert.False(f.Matches("winrar"));
        Assert.True(new ModuleFilter(null).Matches("anything"));
    }
}
