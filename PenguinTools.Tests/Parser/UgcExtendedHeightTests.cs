using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class UgcExtendedHeightTests
{
    [Theory]
    [InlineData(8, true, "\uE112G", 1960)]
    [InlineData(9, true, "\uE112G", 1960)]
    [InlineData(8, false, "\uE1A0", 1960)]
    [InlineData(9, false, "\uE1A0", 1960)]
    [InlineData(8, true, "ZZ", 1295)]
    [InlineData(9, true, "ZZ", 1295)]
    [InlineData(9, true, "zz", 1295)]
    public async Task ExistingExtension_ConvertsExactHeightWithoutChangingLaneWidthOrAxis(int version, bool high, string digits, int height)
    {
        var path = TestTempPaths.Create(".ugc");
        var binary = TestTempPaths.Create(".mgxc");
        var output = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var lane = version == 9 ? 'G' : '0';
            var end = version == 9 ? 'K' : '4';
            await File.WriteAllTextAsync(path, Header(version, high) +
                $"#0'0:t{lane}4\n#0'0:S{lane}4{digits}N\n#480>s{end}4{digits}\n" +
                $"#1'0:C{lane}4{digits}BY,240\n#480>c{end}4{digits}\n", ct);
            var parsed = await new UgcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Check(parsed.Value!, height);
            Assert.True((await new MgxcChartWriter(new(binary, parsed.Value!)).WriteAsync(ct)).Succeeded);
            var reloaded = await new MgxcParser(new(binary, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(reloaded.Succeeded, reloaded.ToString());
            Check(reloaded.Value!, height);
            var c2s = new C2SChartConverter(new(reloaded.Value!)).Convert();
            Assert.True(c2s.Succeeded, c2s.ToString());
            var air = Assert.Single(c2s.Value!.Notes.OfType<C.AirSlide>());
            Assert.Equal(height, air.Height.Original);
            Assert.Equal(height, air.EndHeight.Original);
            await File.WriteAllTextAsync(output, "existing destination", ct);
            var written = await new UgcChartWriter(output, reloaded.Value!).WriteAsync(ct);
            if (height > 1295)
            {
                Assert.False(written.Succeeded);
                Assert.Equal("existing destination", await File.ReadAllTextAsync(output, ct));
            }
            else
            {
                Assert.True(written.Succeeded, written.ToString());
            }
        }
        finally { File.Delete(path); File.Delete(binary); File.Delete(output); }
    }

    [Theory]
    [InlineData(8, "?G", false)]
    [InlineData(9, "?G", false)]
    [InlineData(8, "2?", true)]
    [InlineData(9, "2?", true)]
    [InlineData(9, "2\uE112", true)]
    public async Task InvalidHeight_FailsAtItsLineInsteadOfBecomingMinusOne(int version, string digits, bool child)
    {
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var lane = version == 9 ? 'G' : '0';
            var text = Header(version, true) + $"#0'0:t{lane}4\n#0'0:S{lane}4{(child ? "28" : digits)}N\n#480>s{lane}4{(child ? digits : "28")}\n";
            await File.WriteAllTextAsync(path, text, ct);
            var result = await new UgcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.False(result.Succeeded);
            var issue = Assert.Single(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Error_Invalid_Header);
            Assert.Equal(path, issue.Path);
            Assert.Equal(child ? 9 : 8, issue.Line);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    public async Task ExistingSignedHeight_CanReturnToItsActualC2sHeight(int version)
    {
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var lane = version == 9 ? 'G' : '0';
            await File.WriteAllTextAsync(path, Header(version, true) + $"#0'0:t{lane}4\n#0'0:S{lane}4\uE000GN\n#480>s{lane}428\n", ct);
            var parsed = await new UgcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var result = new C2SChartConverter(new(parsed.Value!)).Convert();
            Assert.True(result.Succeeded, result.ToString());
            Assert.Equal(0, Assert.Single(result.Value!.Notes.OfType<C.AirSlide>()).Height.Result);
        }
        finally { File.Delete(path); }
    }

    private static string Header(int version, bool high) =>
        $"@VER\t{version}\n@TICKS\t480\n@MAINBPM\t120\n@BEAT\t0\t4\t4\n@FLAG\tHIPRECISION\t{high.ToString().ToUpperInvariant()}\n@ENDHEAD\n";

    private static void Check(U.Chart chart, int height)
    {
        var air = Assert.Single(chart.Notes.Children.OfType<U.AirSlide>());
        Assert.Equal(height, air.Height);
        Assert.Equal(0, air.Lane);
        Assert.Equal(4, air.Width);
        var tail = Assert.IsType<U.AirSlideJoint>(Assert.Single(air.Children));
        Assert.Equal(height, tail.Height);
        Assert.Equal(4, tail.Lane);
        Assert.Equal(480, tail.Tick.Original);
        var crash = Assert.Single(chart.Notes.Children.OfType<U.AirCrash>());
        Assert.Equal(height, crash.Height);
        Assert.Equal(AirLadderAttr.AxisY, crash.Attr);
        Assert.Equal(Color.VLT, crash.Color);
        Assert.Equal(240, crash.Density.Original);
    }
}
