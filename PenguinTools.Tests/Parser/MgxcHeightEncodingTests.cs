using System.Buffers.Binary;
using System.Text;
using PenguinTools.Chart;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Writer.mgxc;
using Xunit;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class MgxcHeightEncodingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(-1, false, 65537, 1)]
    [InlineData(-1, true, 65537, 65537)]
    [InlineData(0, true, 65537, 1)]
    [InlineData(1, false, 65537, 65537)]
    [InlineData(1, false, -80, -80)]
    public async Task ExplicitLayoutOverridesOriginAndReservedBits(int layout, bool origin, int raw, int expected)
    {
        var path = TestTempPaths.Create(".mgxc");
        try
        {
            await File.WriteAllBytesAsync(path, Fixture(layout < 0 ? [] : [layout], origin, raw), Ct);
            var parsed = await new MgxcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(Ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Equal(expected, Assert.Single(parsed.Value!.Notes.Children.OfType<U.AirCrash>()).Height);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(0, 1)]
    public async Task UnknownOrConflictingLayoutIsRejected(int first, int second)
    {
        var path = TestTempPaths.Create(".mgxc");
        try
        {
            await File.WriteAllBytesAsync(path, Fixture([first, second], false, 80), Ct);
            var parsed = await new MgxcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(Ct);
            Assert.False(parsed.Succeeded);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(-80)]
    [InlineData(65536)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public async Task ExtendedWriterRetainsSignedHeightAcrossRepeatedReads(int height)
    {
        var chart = new U.Chart();
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        var crash = new U.AirCrash { Tick = 480, Lane = 0, Width = 4, Height = height, Density = 240 };
        crash.AppendChild(new U.AirCrashJoint { Tick = 960, Lane = 4, Width = 4, Height = height });
        chart.Notes.AppendChild(crash);
        var path = TestTempPaths.Create(".mgxc");
        try
        {
            for (var cycle = 0; cycle < 3; cycle++)
            {
                var result = await new MgxcChartWriter(new(path, chart, EditorTarget.Extended)).WriteAsync(Ct);
                Assert.True(result.Succeeded, result.ToString());
                var bytes = await File.ReadAllBytesAsync(path, Ct);
                var marker = bytes.AsSpan().IndexOf("xh32"u8);
                Assert.True(marker >= 0);
                Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(marker + 6)));
                var parsed = await new MgxcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(Ct);
                Assert.True(parsed.Succeeded, parsed.ToString());
                chart = parsed.Value!;
                var actual = Assert.Single(chart.Notes.Children.OfType<U.AirCrash>());
                Assert.Equal(height, actual.Height);
                Assert.Equal(height, Assert.IsType<U.AirCrashJoint>(actual.LastChild).Height);
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(EditorTarget.Legacy, -80)]
    [InlineData(EditorTarget.Legacy, 70000)]
    [InlineData(EditorTarget.Standard, -80)]
    [InlineData(EditorTarget.Standard, 70000)]
    public async Task OriginalEditorTarget_RejectsHeightsItsNativeReaderWouldTruncate(EditorTarget target, int height)
    {
        var chart = new U.Chart();
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        var crash = new U.AirCrash { Tick = 480, Lane = 0, Width = 4, Height = height, Density = 240 };
        crash.AppendChild(new U.AirCrashJoint { Tick = 960, Lane = 4, Width = 4, Height = height });
        chart.Notes.AppendChild(crash);
        var path = TestTempPaths.Create(".mgxc");
        try
        {
            await File.WriteAllTextAsync(path, "existing destination", Ct);
            var result = await new MgxcChartWriter(new(path, chart, target)).WriteAsync(Ct);
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics.Diagnostics,
                d => d.Message.Key == PenguinTools.Core.MsgKeys.MgCrit_Unrepresentable_height);
            Assert.Equal("existing destination", await File.ReadAllTextAsync(path, Ct));
            Assert.Equal(height, crash.Height);
            Assert.Equal(height, Assert.IsType<U.AirCrashJoint>(crash.LastChild).Height);
        }
        finally { File.Delete(path); }
    }

    // Independent wire fixture: v4 has one signed-height or uint16-height CRUSH path.
    private static byte[] Fixture(int[] markers, bool origin, int height)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("MGXC"u8);
        writer.Write(0);
        writer.Write(4);
        Block("meta", () =>
        {
            foreach (var value in markers)
            {
                writer.Write("xh32"u8);
                writer.Write((ushort)1);
                writer.Write((ushort)value);
            }
            if (origin)
            {
                var text = Encoding.UTF8.GetBytes(ChartExtras.FixedZeroOriginMarker + ";");
                writer.Write("lcpy"u8);
                writer.Write((ushort)4);
                writer.Write((ushort)text.Length);
                writer.Write(text);
            }
            writer.Write("HSCL"u8);
            writer.Write((ushort)3);
            writer.Write((ushort)0);
            writer.Write(10d);
            writer.Write("mbpm"u8);
            writer.Write((ushort)3);
            writer.Write((ushort)0);
            writer.Write(120d);
        });
        Block("evnt", () => { });
        Block("dat2", () =>
        {
            Note(1, 480);
            writer.Write(240);
            Note(5, 960);
        });
        stream.Position = 4;
        writer.Write(checked((int)stream.Length - 8));
        return stream.ToArray();

        void Block(string tag, Action content)
        {
            writer.Write(Encoding.ASCII.GetBytes(tag));
            var size = stream.Position;
            writer.Write(0);
            var start = stream.Position;
            content();
            var end = stream.Position;
            stream.Position = size;
            writer.Write(checked((int)(end - start)));
            stream.Position = end;
        }
        void Note(byte kind, int tick)
        {
            writer.Write(new byte[] { 10, kind, 0, 0, 0, 0, 4, 0 });
            writer.Write(height);
            writer.Write(tick);
            writer.Write(0);
        }
    }
}
