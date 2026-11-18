using System.Buffers.Binary;
using System.Text;
using PenguinTools.Chart;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class NativeExTapRoleTests
{
    [Fact]
    public void CoincidentDuplicateExTaps_PreserveWhichCarrierPaintsTheHead()
    {
        var chart = new U.Chart();
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0, Numerator = 4, Denominator = 4 });
        var hold = new U.Hold { Tick = 0, Lane = 4, Width = 4 };
        hold.AppendChild(new U.HoldJoint { Tick = 480 });
        chart.Notes.AppendChild(hold);
        U.ExTap[] taps =
        [
            new() { Lane = 4, Width = 4, Effect = ExEffect.UP, Role = U.ExTapRole.SharedLongCarrier },
            new() { Lane = 4, Width = 4, Effect = ExEffect.DW, Role = U.ExTapRole.SharedLongCarrier },
            new() { Lane = 4, Width = 4, Effect = ExEffect.UP, Role = U.ExTapRole.Explicit }
        ];
        foreach (var tap in taps)
        {
            chart.Notes.AppendChild(tap);
        }
        var token = NativeExTapRoles.Capture([hold], taps);
        foreach (var tap in taps)
        {
            tap.Role = U.ExTapRole.Auto;
        }

        Assert.True(NativeExTapRoles.Restore(chart, token));
        Assert.Equal(U.ExTapRole.Auto, taps[0].Role);
        Assert.Equal(U.ExTapRole.Explicit, taps[2].Role);
        new ChartPostProcessor(chart, new DiagnosticCollector(), TestAssets.Load()).Run();
        Assert.Equal(ExEffect.UP, hold.Effect);
    }

    [Theory]
    [InlineData(EditorTarget.Legacy, false, false)]
    [InlineData(EditorTarget.Standard, false, false)]
    [InlineData(EditorTarget.Extended, false, false)]
    [InlineData(EditorTarget.Legacy, true, false)]
    [InlineData(EditorTarget.Standard, true, false)]
    [InlineData(EditorTarget.Extended, true, false)]
    [InlineData(EditorTarget.Extended, false, true)]
    public async Task MgxcCopyrightAlone_RestoresIndependentExTapsAcrossNativeUgc(EditorTarget target, bool editTail, bool paired)
    {
        var chart = new U.Chart();
        chart.Meta.BgmEnableBarOffset = false;
        chart.Meta.Title = "Original";
        chart.Meta.Copyright = "test attribution";
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0, Numerator = 4, Denominator = 4 });
        AddHoldAndExTaps(chart, 0, paired);
        AddHoldAndExTaps(chart, 1920, paired);

        var mgxc = TestTempPaths.Create(".mgxc");
        var ugc = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new MgxcChartWriter(new(mgxc, chart, target)).WriteAsync(ct)).Succeeded);
            Assert.True((await new UgcChartWriter(ugc, chart, target).WriteAsync(ct)).Succeeded);
            var copyright = ReadCopyright(await File.ReadAllBytesAsync(mgxc, ct));
            Assert.StartsWith("PT_EXROLE_V1=", copyright, StringComparison.Ordinal);
            var lines = await File.ReadAllLinesAsync(ugc, ct);
            // The stock editor may drop converter bookmarks and default
            // headers while carrying across the native copyright field.
            var native = lines.Where(line => !line.StartsWith("@CLKCNT", StringComparison.Ordinal))
                .Select(line => line.StartsWith("@COPYRIGHT", StringComparison.Ordinal)
                    ? "@COPYRIGHT\t" + copyright
                    : line.Replace("@TITLE\tOriginal", "@TITLE\tRenamed", StringComparison.Ordinal)).ToList();
            if (editTail)
            {
                var firstTail = native.FindIndex(line => line == "#480>s");
                Assert.True(firstTail >= 0);
                native[firstTail] = "#960>s";
            }
            await File.WriteAllLinesAsync(ugc, native, ct);

            var parsed = await new UgcParser(new(ugc, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded);
            Assert.Equal("Renamed", parsed.Value!.Meta.Title);
            Assert.Equal("test attribution", parsed.Value.Meta.Copyright);
            Assert.Equal(editTail, parsed.Diagnostics.Diagnostics.Any(d => d.Message.Key == MsgKeys.Warn_Ex_tap_roles_not_restored));
            var converted = new C2SChartConverter(new(parsed.Value)).Convert();
            Assert.True(converted.Succeeded);
            var independent = converted.Value!.Notes.OfType<C.ExTap>().ToArray();
            Assert.Equal(editTail ? [1920] : [0, 1920], independent.Select(note => note.Tick.Original));
            Assert.All(independent, note => Assert.Equal(ExEffect.DW, note.Effect));
            var holds = converted.Value.Notes.OfType<C.Hold>().ToArray();
            Assert.Equal(editTail ? ExEffect.DW : null, holds[0].Effect);
            Assert.Null(holds[1].Effect);
            Assert.Equal(editTail ? 960 : 480, holds[0].EndTick.Original);
            if (paired)
            {
                var airs = converted.Value.Notes.OfType<C.Air>().ToArray();
                Assert.Equal(2, airs.Length);
                Assert.All(airs, air => Assert.Contains(Assert.IsType<C.ExTap>(air.Parent), independent));
            }
        }
        finally
        {
            File.Delete(mgxc);
            File.Delete(ugc);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MarkerWithoutRoom_WarnsAndKeepsTheUserCopyright(bool ugc)
    {
        var chart = new U.Chart();
        chart.Meta.BgmEnableBarOffset = false;
        // Leave room for the timing markers but not for the role marker.
        chart.Meta.Copyright = new string('A', ushort.MaxValue - 40);
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0, Numerator = 4, Denominator = 4 });
        AddHoldAndExTaps(chart, 0, paired: false);
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = ugc
                ? await new UgcChartWriter(path, chart, EditorTarget.Standard).WriteAsync(ct)
                : await new MgxcChartWriter(new(path, chart, EditorTarget.Standard)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            Assert.Contains(written.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_Roundtrip_metadata_not_saved);
            var copyright = ugc
                ? (await File.ReadAllLinesAsync(path, ct)).Single(l => l.StartsWith("@COPYRIGHT\t", StringComparison.Ordinal))
                : ReadCopyright(await File.ReadAllBytesAsync(path, ct));
            Assert.DoesNotContain(NativeExTapRoles.Prefix, copyright, StringComparison.Ordinal);
            Assert.Contains(chart.Meta.Copyright, copyright, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    private static void AddHoldAndExTaps(U.Chart chart, int tick, bool paired)
    {
        var hold = new U.Hold { Tick = tick, Lane = 4, Width = 4 };
        hold.AppendChild(new U.HoldJoint { Tick = tick + 480 });
        chart.Notes.AppendChild(hold);
        var ex = new U.ExTap { Tick = tick, Lane = 4, Width = 4, Effect = ExEffect.DW, Role = U.ExTapRole.Explicit };
        chart.Notes.AppendChild(ex);
        if (paired)
        {
            var air = new U.Air { Direction = AirDirection.DW };
            ex.MakePair(air);
            chart.Notes.AppendChild(air);
        }
    }

    private static string ReadCopyright(byte[] mgxc)
    {
        var field = mgxc.AsSpan().IndexOf("lcpy"u8);
        Assert.True(field >= 0);
        var size = BinaryPrimitives.ReadUInt16LittleEndian(mgxc.AsSpan(field + 6));
        return Encoding.UTF8.GetString(mgxc.AsSpan(field + 8, size));
    }
}
