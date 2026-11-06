using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using Xunit;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class TrailingSpeedEventTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultipleEditorRoundTrips_PreserveTrailingResetAndUnusedTimeline(bool ugcFirst)
    {
        var source = TestTempPaths.Create(".c2s");
        var binary = TestTempPaths.Create(".mgxc");
        var text = TestTempPaths.Create(".ugc");
        var output = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(source, "VERSION\t1.14.00\nRESOLUTION\t384\nBPM_DEF\t120\t120\t120\t120\nMET_DEF\t4\t4\nBPM\t0\t0\t120\nMET\t0\t0\t4\t4\nSLP\t0\t0\t384\t2\t0\nSLP\t0\t0\t768\t3\t2\nTAP\t0\t96\t4\t2\n", ct);
            var parsed = await new C2SParser(new(source)).ParseAsync(ct);
            Assert.True(parsed.Succeeded);
            var converted = new UgcChartConverter(new(parsed.Value!)).Convert();
            Assert.True(converted.Succeeded);
            var chart = converted.Value!;
            for (var cycle = 0; cycle < 6; cycle++)
            {
                if ((cycle % 2 == 0) == ugcFirst)
                {
                    Assert.True((await new UgcChartWriter(text, chart).WriteAsync(ct)).Succeeded);
                    var result = await new UgcParser(new(text, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
                    Assert.True(result.Succeeded);
                    chart = result.Value!;
                }
                else
                {
                    Assert.True((await new MgxcChartWriter(new(binary, chart)).WriteAsync(ct)).Succeeded);
                    var result = await new MgxcParser(new(binary, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
                    Assert.True(result.Succeeded);
                    chart = result.Value!;
                }
                Assert.Contains(chart.Events.Children.OfType<U.ScrollSpeedEvent>(), e => e.Timeline == 0 && e.Tick.Original == 1920 && e.Speed == 1);
                Assert.Contains(chart.Events.Children.OfType<U.ScrollSpeedEvent>(), e => e.Timeline == 2 && e.Tick.Original == 3840 && e.Speed == 1);
                var c2s = new C2SChartConverter(new(chart)).Convert();
                Assert.True(c2s.Succeeded);
                Assert.True((await new C2SChartWriter(new(output, c2s.Value!)).WriteAsync(ct)).Succeeded);
                var lines = await File.ReadAllLinesAsync(output, ct);
                Assert.Contains(lines, l => l.Split('\t') is ["SLP", "0", "0", "384", _, "0"]);
                Assert.Contains(lines, l => l.Split('\t') is ["SLP", "0", "0", "768", _, "2"]);
            }
            // A real edit must still invalidate the old interval snapshot.
            var reset = chart.Events.Children.OfType<U.ScrollSpeedEvent>().Single(e => e.Timeline == 0 && e.Tick.Original == 1920);
            reset.Tick = 960;
            var edited = new C2SChartConverter(new(chart)).Convert();
            Assert.True(edited.Succeeded);
            Assert.True((await new C2SChartWriter(new(output, edited.Value!)).WriteAsync(ct)).Succeeded);
            Assert.Contains(await File.ReadAllLinesAsync(output, ct), l => l.Split('\t') is ["SLP", "0", "0", "192", _, "0"]);
        }
        finally
        {
            File.Delete(source);
            File.Delete(binary);
            File.Delete(text);
            File.Delete(output);
        }
    }
}
