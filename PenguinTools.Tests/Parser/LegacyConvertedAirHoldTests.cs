using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class LegacyConvertedAirHoldTests
{
    private static string Header(int version) => $"@VER\t{version}\n@TICKS\t480\n@FLAG\tHIPRECISION\tTRUE\n" +
        "@FLAG\tSOFFSET\tFALSE\n@BEAT\t0\t4\t4\n@BPM\t0'0\t120\n";

    [Theory]
    [InlineData(8, false, 'I')]
    [InlineData(8, false, 'N')]
    [InlineData(9, false, 'I')]
    [InlineData(9, false, 'N')]
    [InlineData(9, true, 'I')]
    [InlineData(9, true, 'N')]
    public async Task CompleteNativeExport_RetainsColorNodesAndC2sAcrossSaves(int version, bool legacyChildren, char color)
    {
        var lane = version == 8 ? '4' : 'K';
        var childLane = legacyChildren ? '4' : lane;
        var source = Header(version) + $"#1'0:t{lane}4\n#1'0:H{lane}428{color}\n" +
            $"#240>s{childLane}428\n#480>c{childLane}428\n@USETIL\t1\n#2'0:t{lane}4\n";
        var parsed = await ReadText(source);
        Assert.True(parsed.Succeeded, parsed.ToString());
        Assert.Contains(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_Ugc_legacy_air_hold_normalized);
        var chart = parsed.Value!;
        var hold = Assert.Single(chart.Notes.Children.OfType<U.AirHold>());
        Assert.Equal(color == 'I' ? Color.PNK : Color.DEF, hold.Color);
        Assert.Equal(1920, hold.Tick.Original);
        Assert.Collection(hold.Children,
            c => { Assert.Equal(2160, c.Tick.Original); Assert.Equal(Joint.D, Assert.IsType<U.AirHoldJoint>(c).Joint); },
            c => { Assert.Equal(2400, c.Tick.Original); Assert.Equal(Joint.C, Assert.IsType<U.AirHoldJoint>(c).Joint); });
        var canonical = await ReadText(Header(version) + $"#1'0:t{lane}4\n#1'0:H{lane}4{color}\n" +
            "#240>s\n#480>c\n@USETIL\t1\n" + $"#2'0:t{lane}4\n");
        Assert.True(canonical.Succeeded, canonical.ToString());
        var expected = await C2sRows(canonical.Value!);
        Assert.Equal(expected, await C2sRows(chart));
        var ct = TestContext.Current.CancellationToken;
        foreach (var format in new[] { ".mgxc", ".ugc" })
        {
            var path = TestTempPaths.Create(format);
            try
            {
                for (var cycle = 0; cycle < 3; cycle++)
                {
                    var written = format == ".mgxc" ? await new MgxcChartWriter(new(path, chart)).WriteAsync(ct)
                        : await new UgcChartWriter(path, chart).WriteAsync(ct);
                    Assert.True(written.Succeeded, written.ToString());
                    var read = format == ".mgxc"
                        ? await new MgxcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct)
                        : await new UgcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
                    Assert.True(read.Succeeded, read.ToString());
                    chart = read.Value!;
                    Assert.Equal(expected, await C2sRows(chart));
                    Assert.DoesNotContain(read.Diagnostics.Diagnostics,
                        d => d.Message.Key == MsgKeys.Warn_Ugc_legacy_air_hold_normalized);
                }
            }
            finally { File.Delete(path); }
        }
    }

    [Theory]
    [InlineData("H4428I", "")]
    [InlineData("H4428I", "#480>s\n")]
    [InlineData("H4428I", "#480>s5428\n")]
    [InlineData("H4428I", "#480>s4528\n")]
    [InlineData("H4428I", "#480>s4429\n")]
    [InlineData("H4428I", "#480>s4428X\n")]
    [InlineData("H4428I", "#480>x4428\n")]
    [InlineData("H4428I", "#-1>s4428\n")]
    [InlineData("H4428I", "@USETIL\t1\n#480>s4428\n")]
    [InlineData("H4428IX", "#480>s4428\n")]
    [InlineData("H4428X", "#480>s4428\n")]
    [InlineData("H4428I", "#240>c4428\n#480>s4428\n")]
    [InlineData("H4428I", "#240>v4428\n#480>s4428\n")]
    [InlineData("H4428I", "#480>v4428\n")]
    [InlineData("H4428I", "#240>s4428\n#240>s4428\n")]
    [InlineData("H4428I", "#480>s4428\n#240>c4428\n")]
    [InlineData("H4428I", "#0>s4428\n")]
    public async Task IncompleteOrInconsistentBlock_IsRejectedWithoutGuessing(string head, string children)
    {
        var result = await ReadText(Header(8) + "#1'0:t44\n#1'0:" + head + "\n" + children);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.MgCrit_Ugc_legacy_air_hold);
    }

    [Fact]
    public async Task MixedChildCoordinateEncodings_AreRejected()
    {
        var result = await ReadText(Header(9) + "#1'0:tK4\n#1'0:HK428I\n#240>c4428\n#480>sK428\n");
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.MgCrit_Ugc_legacy_air_hold);
    }

    private static async Task<OperationResult<U.Chart>> ReadText(string text)
    {
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, text, ct);
            var result = await new UgcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.Equal(text, await File.ReadAllTextAsync(path, ct));
            return result;
        }
        finally { File.Delete(path); }
    }

    private static async Task<string[]> C2sRows(U.Chart chart)
    {
        var converted = new C2SChartConverter(new(chart)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new C2SChartWriter(new(path, converted.Value!)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            return await File.ReadAllLinesAsync(path, ct);
        }
        finally { File.Delete(path); }
    }
}
