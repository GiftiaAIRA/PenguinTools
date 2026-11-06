using PenguinTools.Chart;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core;
using Xunit;

namespace PenguinTools.Tests.Parser;

public sealed class C2sUnchangedJudgeSourceTests
{
    [Theory]
    [InlineData("BPM 1 0 120\nBPM 1 0 180")]
    [InlineData("MET 1 0 3 4\nMET 1 0 5 4")]
    [InlineData("DCM 1 0 96 2\nDCM 1 0 96 3")]
    [InlineData("SLP 1 0 96 2 0\nSLP 1 0 96 3 0")]
    [InlineData("SFL 1 0 96 2\nSFL 1 0 96 3")]
    [InlineData("STP 1 0 96\nSTP 1 0 192")]
    public async Task SameTickEventEditsCannotRestoreOldOrder(string events)
    {
        var input = TestTempPaths.Create(".c2s");
        var output = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(input, Source.Replace("TAP 2 0", events + "\nTAP 2 0"), ct);
            var parsed = await new C2SParser(new(input)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var chart = parsed.Value!;
            Assert.NotNull(chart.Extras.UnchangedJudgeSource);
            var targets = chart.Events.Where(e => e.Tick.Original > 0).ToArray();
            Assert.Equal(2, targets.Length);
            var first = chart.Events.IndexOf(targets[0]);
            var second = chart.Events.IndexOf(targets[1]);
            (chart.Events[first], chart.Events[second]) = (chart.Events[second], chart.Events[first]);
            var saved = await new C2SChartWriter(new(output, chart)).WriteAsync(ct);
            Assert.True(saved.Succeeded, saved.ToString());
            var expected = events.Split('\n').Reverse().Select(l => C2SRoundTrip.Normalize(l, 384));
            var actual = (await File.ReadAllLinesAsync(output, ct))
                .Where(l => l.StartsWith(targets[0].Id + "\t1\t", StringComparison.Ordinal))
                .Select(l => C2SRoundTrip.Normalize(l, 384));
            Assert.Equal(expected, actual);
            Assert.Contains(saved.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_C2s_source_judgements_edited);
        }
        finally { File.Delete(input); File.Delete(output); }
    }

    private const string Source = """
        VERSION 1.15.00 1.15.00
        MUSIC 0
        SEQUENCEID 0
        DIFFICULT 0
        LEVEL 0
        CREATOR Test
        BPM_DEF 120 120 120 120
        MET_DEF 4 4
        RESOLUTION 384
        CLK_DEF 384
        PROGJUDGE_BPM 240
        PROGJUDGE_AER 0.999
        TUTORIAL 0
        BPM 0 0 120
        MET 0 0 4 4
        TAP 2 0 4 4
        TAP 1 0 0 4
        T_JUDGE_TAP 7
        T_JUDGE_HLD 0
        T_JUDGE_SLD 0
        T_JUDGE_AIR 0
        T_JUDGE_FLK 0
        T_JUDGE_ALL 7
        """;

    [Theory]
    [InlineData("direct", false)]
    [InlineData("mgxc", false)]
    [InlineData("ugc", false)]
    [InlineData("direct", true)]
    [InlineData("mgxc", true)]
    [InlineData("ugc", true)]
    [InlineData("direct", false, true)]
    [InlineData("mgxc", false, true)]
    [InlineData("ugc", false, true)]
    public async Task SourceTotalsRequireUnchangedActualRecords(string format, bool edit, bool unknown = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "input.c2s");
            await File.WriteAllTextAsync(input, Source + (unknown ? "\nXYZ 0 0 0 4\n" : ""), TestContext.Current.CancellationToken);
            var parsed = await new C2SParser(new C2SParseRequest(input)).ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var chart = parsed.Value!;
            Assert.Equal(unknown, chart.Extras.UnchangedJudgeSource is null);
            if (format != "direct")
            {
                var model = new UgcChartConverter(new UgcConvertRequest(chart)).Convert();
                Assert.True(model.Succeeded, model.ToString());
                var native = Path.Combine(dir, "chart." + format);
                var saved = format == "mgxc"
                    ? await new MgxcChartWriter(new MgxcWriteRequest(native, model.Value!)).WriteAsync(TestContext.Current.CancellationToken)
                    : await new UgcChartWriter(native, model.Value!).WriteAsync(TestContext.Current.CancellationToken);
                Assert.True(saved.Succeeded, saved.ToString());
                var reloaded = format == "mgxc"
                    ? await new MgxcParser(new MgxcParseRequest(native, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(TestContext.Current.CancellationToken)
                    : await new UgcParser(new UgcParseRequest(native, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(TestContext.Current.CancellationToken);
                Assert.True(reloaded.Succeeded, reloaded.ToString());
                var back = new C2SChartConverter(new C2SConvertRequest(reloaded.Value!)).Convert();
                Assert.True(back.Succeeded, back.ToString());
                chart = back.Value!;
            }
            if (edit)
            {
                chart.Notes[0].Lane += 1;
            }

            var output = Path.Combine(dir, "output.c2s");
            var written = await new C2SChartWriter(new C2SWriteRequest(output, chart)).WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());
            var text = await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken);
            Assert.Contains("T_JUDGE_ALL\t" + (edit || unknown ? 2 : 7), text);
            if (unknown)
            {
                Assert.DoesNotContain(written.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_C2s_source_judgements_retained);
                Assert.DoesNotContain("XYZ", text);
            }
            else
            {
                Assert.Contains(written.Diagnostics.Diagnostics, d => d.Message.Key == (edit ? MsgKeys.Warn_C2s_source_judgements_edited : MsgKeys.Warn_C2s_source_judgements_retained));
            }
            if (!edit && !unknown)
            {
                var rows = text.Split('\n').Where(l => l.StartsWith("TAP\t", StringComparison.Ordinal)).ToArray();
                Assert.Equal(2, rows.Length);
                Assert.Equal("TAP\t2\t0\t4\t4", rows[0]);
                Assert.Equal("TAP\t1\t0\t0\t4", rows[1]);
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("options")]
    [InlineData("subtick")]
    [InlineData("malformed")]
    [InlineData("ASO")]
    [InlineData("HHD")]
    [InlineData("HHX")]
    [InlineData("adjusted")]
    [InlineData("unknown")]
    public async Task ChangedJudgeSettingsOrQuantizationCannotReuseTotals(string mode)
    {
        var dir = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var source = mode == "subtick" ? Source.Replace("RESOLUTION 384", "RESOLUTION 1920").Replace("TAP 1 0", "TAP 1 1")
                : mode == "malformed" ? Source.Replace("T_JUDGE_ALL 7", "T_JUDGE_ALL 8") : Source;
            source = mode switch
            {
                "ASO" => source + "\nASO 0 0 0 4 5 6 192 4 4 10 11 DEF\n",
                "HHD" => source + "\nHHD 0 0 0 4 5 192 0 4 5 0\n",
                "HHX" => source + "\nHHX 0 0 0 4 5 192 0 4 5 0 UP\n",
                "adjusted" => source.Replace("BPM 0 0 120", "BPM 0 0 1e40"),
                "unknown" => source + "\nXYZ 0 0 0 4\n",
                _ => source
            };
            var input = Path.Combine(dir, "input.c2s");
            await File.WriteAllTextAsync(input, source, TestContext.Current.CancellationToken);
            var parsed = await new C2SParser(new C2SParseRequest(input)).ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            if (mode == "unknown")
            {
                Assert.Null(parsed.Value!.Extras.UnchangedJudgeSource);
                Assert.Contains(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Mg_Unrecognized_note);
            }
            if (mode == "options")
            {
                parsed.Value!.Extras.Headers["PROGJUDGE_BPM"] = "PROGJUDGE_BPM\t60";
            }

            var output = Path.Combine(dir, "output.c2s");
            var result = await new C2SChartWriter(new C2SWriteRequest(output, parsed.Value!)).WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded, result.ToString());
            Assert.Contains("T_JUDGE_ALL\t2", await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(dir, true); }
    }
}
