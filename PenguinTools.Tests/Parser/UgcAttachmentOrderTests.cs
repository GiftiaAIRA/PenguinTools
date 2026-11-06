using PenguinTools.Chart;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class UgcAttachmentOrderTests
{
    [Fact]
    public async Task CoincidentAttachments_AreWrittenInNativePassOrderAcrossGroundKinds()
    {
        var chart = Chart();
        var tap = new U.Tap { Tick = 960, Lane = 4, Width = 2 };
        var hold = new U.Hold { Tick = 0, Lane = 4, Width = 2 };
        var holdTail = new U.HoldJoint { Tick = 960, Lane = 4, Width = 2 };
        hold.AppendChild(holdTail);
        var slide = new U.Slide { Tick = 240, Lane = 8, Width = 2 };
        var slideTail = new U.SlideJoint { Tick = 960, Lane = 4, Width = 2 };
        slide.AppendChild(slideTail);
        chart.Notes.AppendChild(tap);
        chart.Notes.AppendChild(hold);
        chart.Notes.AppendChild(slide);
        Pair(chart, tap, new U.Air());
        Pair(chart, holdTail, new U.AirSlide { Height = 80 });
        Pair(chart, slideTail, new U.AirHold());
        var originalOrder = chart.Notes.Children.ToArray();
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new UgcChartWriter(path, chart).WriteAsync(ct)).Succeeded);
            var lines = await File.ReadAllLinesAsync(path, ct);
            // Native readers claim Up AIR for H first, then S, across ground kinds.
            var roots = lines.Where(l => l.StartsWith('#') && l.Contains(':')).ToArray();
            Assert.Equal(["#0'240:sO20", "#0'960:HK2N", "#0'0:hK2", "#0'960:SK228N", "#0'960:tK2", "#0'960:aK2UCN"], roots);
            await File.WriteAllLinesAsync(path, lines.Where(l => !l.StartsWith("@COPYRIGHT", StringComparison.Ordinal)), ct);
            var parsed = await new UgcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded);
            Assert.IsType<U.SlideJoint>(Assert.Single(parsed.Value!.Notes.Children.OfType<U.AirHold>()).PairNote);
            Assert.IsType<U.HoldJoint>(Assert.Single(parsed.Value.Notes.Children.OfType<U.AirSlide>()).PairNote);
            Assert.IsType<U.Tap>(Assert.Single(parsed.Value.Notes.Children.OfType<U.Air>()).PairNote);
            Assert.Equal(originalOrder, chart.Notes.Children);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(AirDirection.IR, true)]
    [InlineData(AirDirection.UL, false)]
    [InlineData(AirDirection.UR, false)]
    [InlineData(AirDirection.DW, false)]
    public async Task OnlyStraightUpAir_CompetesWithLongAir(AirDirection direction, bool reorder)
    {
        var chart = Chart();
        var first = new U.Tap { Tick = 480, Lane = 4, Width = 2 };
        var second = new U.Tap { Tick = 480, Lane = 4, Width = 2 };
        chart.Notes.AppendChild(first);
        chart.Notes.AppendChild(second);
        Pair(chart, first, new U.Air { Direction = direction });
        Pair(chart, second, new U.AirSlide { Height = 80 });
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new UgcChartWriter(path, chart).WriteAsync(ct)).Succeeded);
            var roots = (await File.ReadAllLinesAsync(path, ct)).Where(l => l.StartsWith('#') && l.Contains(':')).ToArray();
            Assert.Equal(reorder, roots[1].Contains(":S", StringComparison.Ordinal));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ConflictingBlockOrder_RejectsUgcWithoutChangingSourceOrExistingFile()
    {
        var chart = Chart();
        for (var i = 0; i < 2; i++)
        {
            var slide = new U.Slide { Tick = 0, Lane = i * 8, Width = 2 };
            chart.Notes.AppendChild(slide);
            for (var j = 0; j < 2; j++)
            {
                var point = new U.SlideJoint { Tick = 480 + j * 480, Lane = 4, Width = 2 };
                slide.AppendChild(point);
                Pair(chart, point, i == j ? new U.Air() : new U.AirHold());
            }
        }
        var order = chart.Notes.Children.ToArray();
        var text = TestTempPaths.Create(".ugc");
        var binary = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(text, "existing chart", ct);
            var orderError = Assert.ThrowsAny<DiagnosticException>(() => UgcNoteOrder.Apply(chart));
            Assert.Equal(MsgKeys.MgCrit_Ugc_air_attachment_order, orderError.Descriptor.Key);
            var result = await new UgcChartWriter(text, chart).WriteAsync(ct);
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.MgCrit_Editor_interior_air);
            Assert.Equal("existing chart", await File.ReadAllTextAsync(text, ct));
            Assert.Equal(order, chart.Notes.Children);
            await File.WriteAllTextAsync(binary, "existing chart", ct);
            var binaryResult = await new MgxcChartWriter(new(binary, chart)).WriteAsync(ct);
            Assert.False(binaryResult.Succeeded);
            Assert.Contains(binaryResult.Diagnostics.Diagnostics,
                d => d.Message.Key == MsgKeys.MgCrit_Editor_interior_air);
            Assert.Equal("existing chart", await File.ReadAllTextAsync(binary, ct));
            Assert.Equal(order, chart.Notes.Children);
        }
        finally { File.Delete(text); File.Delete(binary); }
    }

    private static void Pair(U.Chart chart, U.PositiveNote parent, U.NegativeNote air)
    {
        parent.MakePair(air);
        if (air is U.AirSlide slide)
        {
            slide.AppendChild(new U.AirSlideJoint { Tick = parent.Tick.Original + 240, Lane = 6, Width = 2, Height = 80 });
        }
        else if (air is U.AirHold hold)
        {
            hold.AppendChild(new U.AirHoldJoint { Tick = parent.Tick.Original + 240 });
        }
        chart.Notes.AppendChild(air);
    }

    private static U.Chart Chart()
    {
        var chart = new U.Chart();
        chart.Meta.BgmEnableBarOffset = false;
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0, Numerator = 4, Denominator = 4 });
        return chart;
    }
}
