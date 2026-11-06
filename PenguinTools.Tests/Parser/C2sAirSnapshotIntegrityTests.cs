using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class C2sAirSnapshotIntegrityTests
{
    [Theory]
    [InlineData(false, false, "")]
    [InlineData(false, true, "")]
    [InlineData(true, false, "")]
    [InlineData(true, true, "")]
    [InlineData(false, false, ".mgxc")]
    [InlineData(false, true, ".mgxc")]
    [InlineData(true, false, ".mgxc")]
    [InlineData(true, true, ".mgxc")]
    [InlineData(false, false, ".ugc")]
    [InlineData(false, true, ".ugc")]
    [InlineData(true, false, ".ugc")]
    [InlineData(true, true, ".ugc")]
    public async Task EditedAir_IsNotReplacedBySourceSnapshot(bool delete, bool missingKey, string savedFormat)
    {
        var chart = await CreateNative();
        var original = Assert.Single(chart.Notes.Children.OfType<U.Air>());
        if (delete)
        {
            original.MakePair(null);
            Assert.True(chart.Notes.RemoveChild(original));
        }
        else
        {
            var parent = chart.Notes.Children.OfType<U.Slide>()
                .Select(slide => Assert.IsType<U.SlideJoint>(slide.LastChild))
                .Single(point => point.PairNote is null);
            var added = new U.Air
            {
                Tick = parent.Tick,
                Lane = parent.Lane,
                Width = parent.Width,
                Direction = AirDirection.UR
            };
            added.MakePair(parent);
            chart.Notes.AppendChild(added);
        }
        if (missingKey)
        {
            chart.Meta.C2sAirEditKey = null;
        }

        if (savedFormat.Length > 0)
        {
            chart = await SaveAndRead(chart, savedFormat);
        }

        var result = new C2SChartConverter(new(chart)).Convert();
        Assert.True(result.Succeeded, result.ToString());
        Assert.Equal(delete ? 0 : 2, result.Value!.Notes.OfType<C.Air>().Count());
        if (!delete)
        {
            var added = Assert.Single(result.Value.Notes.OfType<C.Air>(), air => air.Direction == AirDirection.UR);
            Assert.Contains(added.Parent, result.Value.Notes);
            Assert.Equal(480, added.Parent!.Tick.Original);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnchangedAir_PreservesActualParentWithOrWithoutSnapshotKey(bool missingKey)
    {
        var chart = await CreateNative();
        if (missingKey)
        {
            chart.Meta.C2sAirEditKey = null;
        }

        var result = new C2SChartConverter(new(chart)).Convert();
        Assert.True(result.Succeeded, result.ToString());
        var air = Assert.Single(result.Value!.Notes.OfType<C.Air>());
        Assert.Equal(AirDirection.UL, air.Direction);
        Assert.Contains(air.Parent, result.Value.Notes);
        Assert.Equal(0, air.Parent!.Tick.Original);
    }

    [Theory]
    [InlineData(false, ".mgxc")]
    [InlineData(true, ".mgxc")]
    [InlineData(false, ".ugc")]
    [InlineData(true, ".ugc")]
    public async Task EditedFile_ResaveDoesNotValidateAnOlderAirSnapshot(bool delete, string savedFormat)
    {
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new UgcChartWriter(path, await CreateNative()).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var lines = await File.ReadAllLinesAsync(path, ct);
            var edited = delete
                ? lines.Where(line => !line.Contains(":a", StringComparison.Ordinal))
                : lines.Concat(["#2'0:tG4", "#2'0:aG4URN"]);
            await File.WriteAllLinesAsync(path, edited, ct);
            var parsed = await new UgcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.False(parsed.Value!.Extras.BinarySnapshotValid);
            var saved = await SaveAndRead(parsed.Value, savedFormat);
            var back = new C2SChartConverter(new(saved)).Convert();
            Assert.True(back.Succeeded, back.ToString());
            Assert.Equal(delete ? 0 : 2, back.Value!.Notes.OfType<C.Air>().Count());
            if (!delete)
            {
                var added = Assert.Single(back.Value.Notes.OfType<C.Air>(), air => air.Direction == AirDirection.UR);
                Assert.Equal(3840, added.Tick.Original);
                Assert.IsType<C.Tap>(added.Parent);
            }
        }
        finally { File.Delete(path); }
    }

    private static async Task<U.Chart> CreateNative()
    {
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, """
                VERSION 1.15.00 1.15.00
                RESOLUTION 384
                BPM_DEF 120 120 120 120
                MET_DEF 4 4
                SLD 0 0 4 4 192 4 4
                SLD 0 96 4 4 96 4 4
                AUL 0 192 4 4 SLD
                """, ct);
            var parsed = await new C2SParser(new(path)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var converted = new UgcChartConverter(new(parsed.Value!)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            Assert.False(string.IsNullOrEmpty(converted.Value!.Meta.C2sAirSnapshot));
            return converted.Value;
        }
        finally { File.Delete(path); }
    }

    private static async Task<U.Chart> SaveAndRead(U.Chart chart, string extension)
    {
        var path = TestTempPaths.Create(extension);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = extension == ".mgxc"
                ? await new MgxcChartWriter(new(path, chart)).WriteAsync(ct)
                : await new UgcChartWriter(path, chart).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var parsed = extension == ".mgxc"
                ? await new MgxcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct)
                : await new UgcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            return parsed.Value!;
        }
        finally { File.Delete(path); }
    }
}
