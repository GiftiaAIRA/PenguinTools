using PenguinTools.Chart;
using PenguinTools.Chart.Models.umgr;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core.Metadata;
using Xunit;

namespace PenguinTools.Tests.Parser;

public class ChartMediaPathTests
{
    [Theory]
    [InlineData(".mgxc", ".mgxc")]
    [InlineData(".mgxc", ".ugc")]
    [InlineData(".ugc", ".mgxc")]
    [InlineData(".ugc", ".ugc")]
    public async Task CrossDirectoryRoundTrip_PreservesMediaTargets(string inputExtension, string outputExtension)
    {
        var root = TestTempPaths.Create("");
        var sourceDirectory = Path.Combine(root, "source");
        var outputDirectory = Path.Combine(root, "output");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(outputDirectory);
        var source = Path.Combine(sourceDirectory, "chart" + inputExtension);
        var output = Path.Combine(outputDirectory, "chart" + outputExtension);
        var returned = Path.Combine(sourceDirectory, "returned" + inputExtension);
        try
        {
            var chart = CreateChart(source);
            await Write(source, chart);
            var parsedSource = await Read(source);
            await Write(output, parsedSource);
            Assert.Equal("media/audio.wav", parsedSource.Meta.BgmFilePath);
            Assert.Equal(source, parsedSource.Meta.FilePath);
            var parsedOutput = await Read(output);
            AssertSameMediaTargets(parsedSource.Meta, parsedOutput.Meta);

            await Write(returned, parsedOutput);
            AssertSameMediaTargets(parsedSource.Meta, (await Read(returned)).Meta);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(".mgxc")]
    [InlineData(".ugc")]
    public async Task SameDirectoryWrite_PreservesRelativeAndAbsoluteReferences(string extension)
    {
        var path = TestTempPaths.Create(extension);
        try
        {
            var chart = CreateChart(path);
            chart.Meta.BgmFilePath = "./media/audio.wav";
            chart.Meta.JacketFilePath = Path.Combine(Path.GetDirectoryName(path)!, "jacket.png");
            await Write(path, chart);
            var parsed = await Read(path);
            Assert.Equal(chart.Meta.BgmFilePath, parsed.Meta.BgmFilePath);
            Assert.Equal(chart.Meta.JacketFilePath, parsed.Meta.JacketFilePath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Chart.Models.umgr.Chart CreateChart(string path)
    {
        var chart = new Chart.Models.umgr.Chart();
        chart.Meta.FilePath = path;
        chart.Meta.MainBpm = 120;
        chart.Meta.BgmFilePath = "media/audio.wav";
        chart.Meta.JacketFilePath = "media/封面 image.png";
        chart.Meta.BgiFilePath = "media/background.png";
        chart.Meta.IsCustomStage = true;
        chart.Meta.FieldImage = "media/field.png";
        chart.Events.AppendChild(new BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new BeatEvent { Bar = 0, Tick = 0, Numerator = 4, Denominator = 4 });
        chart.Notes.AppendChild(new Tap { Tick = 1920, Lane = 0, Width = 4 });
        return chart;
    }

    [Theory]
    [InlineData(".mgxc", "")]
    [InlineData(".ugc", "")]
    [InlineData(".mgxc", @"\\server\share\audio.wav")]
    [InlineData(".ugc", @"\\server\share\audio.wav")]
    public async Task CrossDirectoryWrite_PreservesEmptyAndRootedReferences(string extension, string reference)
    {
        if (reference.Length > 0 && !OperatingSystem.IsWindows())
        {
            reference = "/media/audio.wav";
        }

        var path = TestTempPaths.Create(extension);
        try
        {
            var source = Path.Combine(Path.GetDirectoryName(path)!, "source", "chart" + extension);
            var chart = CreateChart(source);
            chart.Meta.BgmFilePath = reference;
            await Write(path, chart);
            Assert.Equal(reference, (await Read(path)).Meta.BgmFilePath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task Write(string path, Chart.Models.umgr.Chart chart)
    {
        var ct = TestContext.Current.CancellationToken;
        var result = Path.GetExtension(path) == ".mgxc"
            ? await new MgxcChartWriter(new MgxcWriteRequest(path, chart, EditorTarget.Standard)).WriteAsync(ct)
            : await new UgcChartWriter(path, chart, EditorTarget.Standard).WriteAsync(ct);
        Assert.True(result.Succeeded, result.ToString());
    }

    private static async Task<Chart.Models.umgr.Chart> Read(string path)
    {
        var ct = TestContext.Current.CancellationToken;
        var result = Path.GetExtension(path) == ".mgxc"
            ? await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()),
                TestMediaTool.Instance).ParseAsync(ct)
            : await new UgcParser(new UgcParseRequest(path, TestAssets.Load()),
                TestMediaTool.Instance).ParseAsync(ct);
        Assert.True(result.Succeeded, result.ToString());
        return result.Value!;
    }

    private static void AssertSameMediaTargets(Meta expected, Meta actual)
    {
        Assert.Equal(expected.FullBgmFilePath, actual.FullBgmFilePath);
        Assert.Equal(expected.FullJacketFilePath, actual.FullJacketFilePath);
        Assert.Equal(expected.FullBgiFilePath, actual.FullBgiFilePath);
        Assert.Equal(Path.GetFullPath(expected.FieldImage, Path.GetDirectoryName(expected.FilePath)!),
            Path.GetFullPath(actual.FieldImage, Path.GetDirectoryName(actual.FilePath)!));
    }
}
