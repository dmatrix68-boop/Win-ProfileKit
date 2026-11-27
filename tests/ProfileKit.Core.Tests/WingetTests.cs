using ProfileKit.Core.Winget;

namespace ProfileKit.Core.Tests;

public class WingetTests
{
    private const string Sample = """
        {
          "$schema": "https://aka.ms/winget-packages.schema.2.0.json",
          "CreationDate": "2026-01-15T09:00:00.000-00:00",
          "Sources": [
            {
              "Packages": [
                { "PackageIdentifier": "Microsoft.VisualStudioCode" },
                { "PackageIdentifier": "VideoLAN.VLC", "Version": "3.0.21" }
              ],
              "SourceDetails": { "Argument": "https://cdn.winget.microsoft.com/cache", "Identifier": "Microsoft.Winget.Source_8wekyb3d8bbwe", "Name": "winget", "Type": "Microsoft.PreIndexed.Package" }
            }
          ],
          "WinGetVersion": "1.9.25200"
        }
        """;

    [Fact]
    public void Parses_export_file()
    {
        var list = WingetExport.Parse(Sample);
        Assert.Equal(2, list.Count);
        Assert.Equal("Microsoft.VisualStudioCode", list[0].Id);
        Assert.Null(list[0].Version);
        Assert.Equal("3.0.21", list[1].Version);
        Assert.Equal("winget", list[1].Source);
    }

    [Fact]
    public void Created_export_round_trips()
    {
        var json = WingetExport.Create([new("7zip.7zip", "24.09"), new("Notepad++.Notepad++")]);
        Assert.Contains("\"$schema\"", json);
        var list = WingetExport.Parse(json);
        Assert.Equal(["7zip.7zip", "Notepad++.Notepad++"], list.Select(p => p.Id));
        Assert.Equal("24.09", list[0].Version);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(3010, true)]
    [InlineData(1641, true)]
    [InlineData(unchecked((int)0x8A150061), true)]
    [InlineData(unchecked((int)0x8A15002B), true)]
    [InlineData(unchecked((int)0x8A150014), false)]
    [InlineData(1, false)]
    public void Exit_codes(int code, bool success) => Assert.Equal(success, WingetExitCodes.IsSuccess(code));
}
