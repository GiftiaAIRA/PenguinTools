using PenguinTools.Chart;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Core;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class NoteWidthRulesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("#0'0:tG0")]
    [InlineData("#0'0:sG00\n#480>sK4")]
    [InlineData("#0'0:sG40\n#480>sK0")]
    [InlineData("#0'0:tG4\n#0'0:SG428N\n#480>sK028")]
    [InlineData("#0'0:CG4280,240\n#480>cK028")]
    public async Task Ugc_PlayableZeroWidthFailsInsteadOfDroppingOrWidening(string body)
    {
        var path = TestTempPaths.Create(".ugc");
        try
        {
            await File.WriteAllTextAsync(path,
                "@VER\t9\n@TICKS\t480\n@FLAG\tHIPRECISION\tTRUE\n@BPM\t0'0\t120\n@BEAT\t0\t4\t4\n" + body, Ct);
            var result = await new UgcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(Ct);
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.MgCrit_Invalid_note_width);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mgxc_ZeroWidthHeadOrJudgementFailsAfterBinaryRead(bool head)
    {
        var chart = new U.Chart();
        chart.Events.AppendChild(new U.BpmEvent { Bpm = 120 });
        var slide = new U.Slide { Width = 4 };
        slide.AppendChild(new U.SlideJoint { Tick = 480, Lane = 4, Width = 4 });
        chart.Notes.AppendChild(slide);
        var path = TestTempPaths.Create(".mgxc");
        try
        {
            Assert.True((await new MgxcChartWriter(new(path, chart)).WriteAsync(Ct)).Succeeded);
            var data = await File.ReadAllBytesAsync(path, Ct);
            var offset = data.AsSpan().IndexOf("dat2"u8) + 8 + (head ? 0 : 20);
            data[offset + 6] = 0;
            await File.WriteAllBytesAsync(path, data, Ct);
            var result = await new MgxcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(Ct);
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.MgCrit_Invalid_note_width);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task C2sWriter_InvalidHeadOrJudgementDoesNotReplaceDestination(bool head)
    {
        var chart = new C.Chart();
        chart.Notes.Add(new C.Slide
        {
            Width = head ? 0 : 4,
            EndTick = 480,
            EndLane = 4,
            EndWidth = head ? 4 : 0,
            Joint = Joint.D
        });
        var path = TestTempPaths.Create(".c2s");
        try
        {
            await File.WriteAllTextAsync(path, "existing chart", Ct);
            var result = await new C2SChartWriter(new(path, chart)).WriteAsync(Ct);
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.MgCrit_Invalid_note_width);
            Assert.Equal("existing chart", await File.ReadAllTextAsync(path, Ct));
        }
        finally { File.Delete(path); }
    }
}
