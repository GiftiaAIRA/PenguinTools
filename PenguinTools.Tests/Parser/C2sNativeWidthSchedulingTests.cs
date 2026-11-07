using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Writer.c2s;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class C2sNativeWidthSchedulingTests
{
    [Theory]
    [InlineData("SLIDE", 17, 16)]
    [InlineData("ALD", 17, 16)]
    [InlineData("AHD", 17, 16)]
    [InlineData("ASD", 17, 16)]
    public async Task NativeEquivalentWidths_DoNotChangePathOwnership(string kind, int firstWidth, int secondWidth)
    {
        var chart = new U.Chart();
        chart.Extras.FixedZeroOrigin = true;
        chart.Events.AppendChild(new U.BpmEvent { Bpm = 120 });
        var first = AddPath(chart, kind, 0, firstWidth, false);
        var second = AddPath(chart, kind, 240, secondWidth, true);
        var conversion = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        Assert.True(conversion.Succeeded, conversion.ToString());
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new C2SChartWriter(new C2SWriteRequest(path, conversion.Value!)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Empty(parsed.Diagnostics.Diagnostics);
            var restored = new UgcChartConverter(new UgcConvertRequest(parsed.Value!)).Convert();
            Assert.True(restored.Succeeded, restored.ToString());
            var roots = restored.Value!.Notes.Children.Where(n => n.GetType() == first.GetType())
                .OrderBy(n => n.Tick.Original).ToArray();

            Assert.Equal(2, roots.Length);
            Assert.Equal(first.Tick, roots[0].Tick);
            Assert.Equal(second.Tick, roots[1].Tick);
            Assert.Equal(480, roots[0].LastChild!.Tick.Original);
            Assert.Equal([480, 960], roots[1].Children.Select(n => n.Tick.Original));
            Assert.Equal(secondWidth, roots[0].LastChild!.Width);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClippedControlWidths_AreNormalizedBeforeScheduling(bool airSlide)
    {
        var chart = new U.Chart();
        chart.Extras.FixedZeroOrigin = true;
        chart.Events.AppendChild(new U.BpmEvent { Bpm = 120 });
        var kind = airSlide ? "ASD" : "SLIDE";
        var first = AddPath(chart, kind, 0, 4, false);
        var second = AddPath(chart, kind, 240, 4, true);
        SetControl(first.FirstChild!, 0);
        SetControl(second.FirstChild!, 1);

        var converted = new C2SChartConverter(new(chart)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new C2SChartWriter(new(path, converted.Value!)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var parsed = await new C2SParser(new(path)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Empty(parsed.Diagnostics.Diagnostics);
            var restored = new UgcChartConverter(new(parsed.Value!)).Convert();
            Assert.True(restored.Succeeded, restored.ToString());
            var roots = restored.Value!.Notes.Children.Where(n => n.GetType() == first.GetType())
                .OrderBy(n => n.Tick.Original).ToArray();

            Assert.Equal(2, roots.Length);
            Assert.Equal([480], roots[0].Children.Select(n => n.Tick.Original));
            Assert.Equal([480, 960], roots[1].Children.Select(n => n.Tick.Original));
            Assert.All(roots, root => Assert.Equal(1, root.FirstChild!.Width));
            Assert.Equal(0, first.FirstChild!.Width);
            Assert.Equal(1, second.FirstChild!.Width);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Writer_UsesNativeWidthsWithoutEditingTheSource()
    {
        var chart = new C.Chart();
        var slide = new C.Slide { Width = 0, EndWidth = 17, EndTick = 480, Joint = Joint.D };
        chart.Notes.Add(slide);
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new C2SChartWriter(new(path, chart)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var fields = Assert.Single(await File.ReadAllLinesAsync(path, ct), line => line.StartsWith("SLD\t", StringComparison.Ordinal))
                .Split('\t');
            Assert.Equal("1", fields[4]);
            Assert.Equal("16", fields[7]);
            Assert.Equal(0, slide.Width);
            Assert.Equal(17, slide.EndWidth);
        }
        finally { File.Delete(path); }
    }

    private static void SetControl(U.Note note, int width)
    {
        note.Width = width;
        if (note is U.SlideJoint slide)
        {
            slide.Joint = Joint.C;
        }
        else
        {
            ((U.AirSlideJoint)note).Joint = Joint.C;
        }
    }

    private static U.Note AddPath(U.Chart chart, string kind, int start, int width, bool continuing)
    {
        U.Note root = kind switch
        {
            "SLIDE" => new U.Slide { Tick = start, Lane = 4, Width = width },
            "ALD" => new U.AirCrash { Tick = start, Lane = 4, Width = width, Density = 320 },
            "AHD" => new U.AirHold(),
            _ => new U.AirSlide()
        };
        if (root is U.NegativeNote air)
        {
            var tap = new U.Tap { Tick = start, Lane = 4, Width = width };
            tap.MakePair(air);
            chart.Notes.AppendChild(tap);
        }
        foreach (var tick in continuing ? new[] { 480, 960 } : new[] { 480 })
        {
            U.Note joint = kind switch
            {
                "SLIDE" => new U.SlideJoint(),
                "ALD" => new U.AirCrashJoint(),
                "AHD" => new U.AirHoldJoint(),
                _ => new U.AirSlideJoint()
            };
            joint.Tick = tick;
            joint.Lane = 4;
            joint.Width = width;
            root.AppendChild(joint);
        }
        chart.Notes.AppendChild(root);
        return root;
    }
}
