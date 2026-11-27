using System.Text;
using ProfileKit.Core.Text;

namespace ProfileKit.Core.Tests;

public class PathRewriterTests
{
    private readonly PathRewriter _rw = new(@"C:\Users\alt", @"C:\Users\neu");

    [Theory]
    [InlineData(@"path=C:\Users\alt\Documents", @"path=C:\Users\neu\Documents")]
    [InlineData(@"{""p"":""C:\\Users\\alt\\x""}", @"{""p"":""C:\\Users\\neu\\x""}")]
    [InlineData("<dir>C:/Users/alt/Downloads</dir>", "<dir>C:/Users/neu/Downloads</dir>")]
    [InlineData("file:///C:/Users/alt/a.txt", "file:///C:/Users/neu/a.txt")]
    [InlineData(@"c:\users\ALT", @"C:\Users\neu")]
    [InlineData(@"""C:\Users\alt""", @"""C:\Users\neu""")]
    [InlineData(@"a=C:\Users\alt;b=C:\Users\alt\x", @"a=C:\Users\neu;b=C:\Users\neu\x")]
    public void Rewrites_all_notations(string input, string expected) => Assert.Equal(expected, _rw.Rewrite(input));

    [Theory]
    [InlineData(@"C:\Users\alt2\x")]
    [InlineData(@"C:\Users\altbau")]
    [InlineData(@"D:\Users\alt\x")]
    public void Leaves_other_paths_untouched(string input) => Assert.Equal(input, _rw.Rewrite(input));

    [Fact]
    public void Identity_when_paths_equal()
    {
        var rw = new PathRewriter(@"C:\Users\max", @"c:\users\max\");
        Assert.True(rw.IsIdentity);
        Assert.Null(rw.RewriteBytes(Encoding.UTF8.GetBytes(@"C:\Users\max")));
    }

    [Fact]
    public void Replacement_with_dollar_is_literal()
    {
        var rw = new PathRewriter(@"C:\Users\alt", @"C:\Users\a$1b");
        Assert.Equal(@"C:\Users\a$1b\x", rw.Rewrite(@"C:\Users\alt\x"));
    }

    [Fact]
    public void Utf16_with_bom_is_preserved()
    {
        var enc = new UnicodeEncoding(false, true);
        var bytes = enc.GetPreamble().Concat(enc.GetBytes(@"[x]" + "\r\n" + @"p=C:\Users\alt\y")).ToArray();
        var result = _rw.RewriteBytes(bytes);
        Assert.NotNull(result);
        Assert.Equal(0xFF, result![0]);
        Assert.Equal(0xFE, result[1]);
        Assert.Equal(@"[x]" + "\r\n" + @"p=C:\Users\neu\y", enc.GetString(result, 2, result.Length - 2));
    }

    [Fact]
    public void Utf8_bom_is_preserved()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(@"ü=C:\Users\alt")).ToArray();
        var result = _rw.RewriteBytes(bytes)!;
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, result[..3]);
        Assert.Equal(@"ü=C:\Users\neu", Encoding.UTF8.GetString(result, 3, result.Length - 3));
    }

    [Fact]
    public void Ansi_text_is_rewritten_byte_faithfully()
    {
        // Windows-1252 "ä" (0xE4) ist kein gültiges UTF-8 → Latin-1-Fallback
        var bytes = new byte[] { 0xE4, (byte)'=' }.Concat(Encoding.ASCII.GetBytes(@"C:\Users\alt")).ToArray();
        var result = _rw.RewriteBytes(bytes)!;
        Assert.Equal(0xE4, result[0]);
        Assert.Equal(@"=C:\Users\neu", Encoding.ASCII.GetString(result, 1, result.Length - 1));
    }

    [Fact]
    public void Binary_files_are_not_touched()
    {
        var bytes = new byte[] { 1, 0, 2, 0, 0, 0, 3 }.Concat(Encoding.ASCII.GetBytes(@"C:\Users\alt")).ToArray();
        Assert.Null(_rw.RewriteBytes(bytes));
    }

    [Fact]
    public void Unchanged_text_returns_null() => Assert.Null(_rw.RewriteBytes(Encoding.UTF8.GetBytes("nichts")));
}
