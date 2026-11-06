using System.Buffers.Binary;
using System.Globalization;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class MargreteUnifiedInputTests
{
    private const string Header = "@VER\t8\n@TICKS\t480\n@MAINBPM\t120\n@BEAT\t0\t3\t4\n";

    [Theory]
    [InlineData(8, false, "I", Color.PNK)]
    [InlineData(8, false, "0I", Color.DEF)]
    [InlineData(8, false, "28I", Color.DEF)]
    [InlineData(8, false, "N", Color.DEF)]
    [InlineData(8, false, "0N", Color.DEF)]
    [InlineData(8, true, "I", Color.PNK)]
    [InlineData(8, true, "0I", Color.PNK)]
    [InlineData(8, true, "N", Color.DEF)]
    [InlineData(8, true, "0N", Color.DEF)]
    [InlineData(9, false, "I", Color.PNK)]
    [InlineData(9, false, "0I", Color.DEF)]
    [InlineData(9, false, "28I", Color.DEF)]
    [InlineData(9, false, "N", Color.DEF)]
    [InlineData(9, false, "0N", Color.DEF)]
    [InlineData(9, true, "I", Color.PNK)]
    [InlineData(9, true, "0I", Color.DEF)]
    [InlineData(9, true, "N", Color.DEF)]
    [InlineData(9, true, "0N", Color.DEF)]
    public async Task AirHoldColor_PreservesCanonicalAndLegacyPadding(int version, bool highPrecision, string suffix, Color expected)
    {
        var lane = version == 8 ? '4' : 'K';
        var chart = await ReadText($"@VER\t{version}\n@TICKS\t480\n@MAINBPM\t120\n@BPM\t0'0\t120\n@BEAT\t0\t4\t4\n" +
            $"@FLAG\tSOFFSET\tFALSE\n@FLAG\tHIPRECISION\t{highPrecision}\n" +
            $"#1'0:t{lane}4\n#1'0:H{lane}4{suffix}\n#480>s\n");
        var hold = Assert.Single(chart.Notes.Children.OfType<U.AirHold>());
        Assert.Equal(expected, hold.Color);
        Assert.Equal(1920, hold.Tick.Original);
        Assert.Equal(2400, Assert.Single(hold.Children).Tick.Original);
        Assert.IsType<U.Tap>(hold.PairNote);
    }

    [Theory]
    [InlineData("", "2'1", 2881)]
    [InlineData("' Created with Margrete v1.8.0\n", "2'1", 2881)]
    [InlineData("' Created with Margrete v1.8.0-23b6ce2\n@COPYRIGHT\tMGR_ORIGIN=ZERO_V1;\n", "2'1", 2881)]
    [InlineData("' Created with Margrete v2.3.3\n", "1'961", 2881)]
    [InlineData("'MGR_ORIGIN=ZERO_V1\n", "1'961", 2881)]
    [InlineData("@COPYRIGHT\tMGR_ORIGIN=ZERO_V1;\n", "1'961", 2881)]
    [InlineData("'PenguinTools dialect: extended\n", "1'961", 2881)]
    [InlineData("'PenguinTools dialect: original\n'MGR_ORIGIN=ZERO_V1\n", "2'1", 2881)]
    public async Task SourceCoordinates_AreResolvedWithoutAnEditorSelection(string marker, string position, int expected)
    {
        var chart = await ReadText(marker + Header + $"@BPM\t{position}\t180\n@TIL\t0\t{position}\t2\n#{position}:t44\n");
        Assert.Equal(expected, Assert.Single(chart.Notes.Children.OfType<U.Tap>()).Tick.Original);
        Assert.Contains(chart.Events.Children.OfType<U.BpmEvent>(), n => n.Bpm == 180 && n.Tick.Original == expected);
        Assert.Contains(chart.Events.Children.OfType<U.ScrollSpeedEvent>(), n => n.Speed == 2 && n.Tick.Original == expected);
    }

    [Theory]
    [InlineData("'MGR_ORIGIN=ZERO_V1\n")]
    [InlineData("' Created with Margrete v2.3.4\n'MGR_ORIGIN=ZERO_V1\n")]
    public async Task Version9_OriginMarkerDoesNotChangeMusicalBarSyntax(string marker)
    {
        var chart = await ReadText(marker + Header.Replace("@VER\t8", "@VER\t9", StringComparison.Ordinal) + "#2'1:tK4\n");
        Assert.Equal(2881, Assert.Single(chart.Notes.Children.OfType<U.Tap>()).Tick.Original);
        Assert.True(chart.Meta.BgmFixedZeroOrigin);
    }

    [Theory]
    [InlineData("'PenguinTools dialect: original\n'PenguinTools dialect: extended\n")]
    [InlineData("'PenguinTools dialect: invalid\n")]
    [InlineData("@VER\t9\n")]
    [InlineData("@COPYRIGHT\tMGR_CLKCNT=-1;\n")]
    [InlineData("@COPYRIGHT\tMGR_CLKCNT=3;MGR_CLKCNT=5;\n")]
    public async Task ConflictingOrMalformedMetadata_IsNotDefaulted(string extra)
    {
        var path = TestTempPaths.Create(".ugc");
        try
        {
            await File.WriteAllTextAsync(path, Header + extra + "#0'0:t44\n", TestContext.Current.CancellationToken);
            var result = await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(TestContext.Current.CancellationToken);
            Assert.False(result.Succeeded);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("", false, 0)]
    [InlineData("", true, 1440)]
    [InlineData("'MGR_ORIGIN=ZERO_V1\n", true, 0)]
    public async Task Preroll_IsAppliedOnceAccordingToOrigin(string marker, bool blank, int expected)
    {
        var source = await ReadText(marker + Header + $"@FLAG\tSOFFSET\t{blank.ToString().ToUpperInvariant()}\n#0'0:t44\n");
        var converted = new C2SChartConverter(new C2SConvertRequest(source)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        Assert.Equal(expected, Assert.Single(converted.Value!.Notes.OfType<C.Tap>()).Tick.Original);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedSlideSegments_KeepTheirOwnerAndSurviveRepeatedNativeRoundTrips(bool mgxc)
    {
        // The supplement ordinal counts all serialized root notes, not only SLIDEs.
        var chart = await ReadText("@VER\t9\n@TICKS\t480\n@MAINBPM\t120\n" +
            "#0'0:tG4\n#0'480:sK40\n#240>sM4\n#480>sO4\n'MGR_NOLINE_SEGMENTS_V1\t1\t10\n");
        var path = TestTempPaths.Create(mgxc ? ".mgxc" : ".ugc");
        try
        {
            for (var cycle = 0; cycle < 3; cycle++)
            {
                var slide = Assert.Single(chart.Notes.Children.OfType<U.Slide>());
                Assert.True(slide.NoLine);
                Assert.False(((U.SlideJoint)slide.FirstChild!).NoLine);
                var converted = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
                Assert.True(converted.Succeeded, converted.ToString());
                Assert.Contains(converted.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_C2s_segmented_slide_judgements);
                Assert.Equal<bool>([true, false], converted.Value!.Notes.OfType<C.Slide>().Select(n => n.NoLine));
                var written = mgxc ? await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(TestContext.Current.CancellationToken)
                    : await new UgcChartWriter(path, chart).WriteAsync(TestContext.Current.CancellationToken);
                Assert.True(written.Succeeded, written.ToString());
                chart = await Read(path);
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void UniformSlideVisibility_DoesNotWarnAboutTheUnusedTail(bool noLine, bool tailNoLine)
    {
        var chart = new U.Chart();
        var slide = new U.Slide { Tick = 0, Lane = 0, Width = 4, NoLine = noLine };
        slide.AppendChild(new U.SlideJoint { Tick = 480, Lane = 4, Width = 4, Joint = Joint.D, NoLine = noLine });
        slide.AppendChild(new U.SlideJoint { Tick = 960, Lane = 8, Width = 4, Joint = Joint.D, NoLine = tailNoLine });
        chart.Notes.AppendChild(slide);

        var converted = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();

        Assert.True(converted.Succeeded, converted.ToString());
        Assert.DoesNotContain(converted.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_C2s_segmented_slide_judgements);
        Assert.All(converted.Value!.Notes.OfType<C.Slide>(), segment => Assert.Equal(noLine, segment.NoLine));
    }

    [Fact]
    public async Task MissingExtensionFeatures_UseNativeDefaultsAndCommon384Output()
    {
        var chart = await ReadText(Header + "@FLAG\tCLICK\tFALSE\n#0'7:t44\n");
        Assert.Null(chart.Extras.ClickCount);
        Assert.False(chart.Extras.ClickEnabled);
        var converted = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        Assert.True(converted.Succeeded);
        var path = TestTempPaths.Create(".c2s");
        try
        {
            Assert.True((await new C2SChartWriter(new C2SWriteRequest(path, converted.Value!)).WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
            var rows = (await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken))
                .Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Where(p => p.Length > 1).ToArray();
            Assert.Equal("384", Assert.Single(rows, p => p[0] == "RESOLUTION")[1]);
            foreach (var key in new[] { "MUSIC", "SEQUENCEID", "DIFFICULT", "LEVEL" })
            {
                Assert.Equal(0, decimal.Parse(Assert.Single(rows, p => p[0] == key)[1], CultureInfo.InvariantCulture));
            }

            Assert.Equal("1", Assert.Single(rows, p => p[0] == "TAP")[2]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task NativeClickCount_OverridesOldBookmarkAndKeepsDisabledState()
    {
        var chart = await ReadText(Header + "@CLKCNT\t3\n@FLAG\tCLICK\tFALSE\n#0'0:t44\n");
        var path = TestTempPaths.Create(".mgxc");
        try
        {
            Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
            var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            var at = bytes.AsSpan().IndexOf("lcpy"u8);
            var size = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at + 6));
            var value = System.Text.Encoding.UTF8.GetBytes("MGR_CLKCNT=7;");
            var next = bytes[..(at + 6)].Concat(BitConverter.GetBytes((ushort)value.Length)).Concat(value).Concat(bytes[(at + 8 + size)..]).ToArray();
            var delta = value.Length - size;
            BinaryPrimitives.WriteInt32LittleEndian(next.AsSpan(4), BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)) + delta);
            BinaryPrimitives.WriteInt32LittleEndian(next.AsSpan(16), BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16)) + delta);
            await File.WriteAllBytesAsync(path, next, TestContext.Current.CancellationToken);
            var parsed = await Read(path);
            Assert.Equal(7, parsed.Extras.ClickCount);
            Assert.False(parsed.Extras.ClickEnabled);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ZeroLengthMeterAndClickCount_ProduceTheSameC2sAcrossCoordinateConventions()
    {
        const string settings = "@TICKS\t480\n@MAINBPM\t120\n@BEAT\t0\t3\t4\n@BEAT\t1\t0\t4\n@BEAT\t2\t4\t4\n@CLKCNT\t7\n";
        var legacy = await ReadText("@VER\t8\n'PenguinTools dialect: extended\n" + settings + "#0'1480:t44\n");
        var current = await ReadText("@VER\t9\n'MGR_ORIGIN=ZERO_V1\n" + settings + "#2'40:tK4\n");
        Assert.Equal(await C2sRows(legacy), await C2sRows(current));
        Assert.Contains(legacy.Events.Children.OfType<U.BeatEvent>(), b => b.Numerator == 0 && b.Bar == 1);
    }

    [Fact]
    public async Task TerminalZeroMeter_SurvivesNativeFieldsWithoutRoundTripMetadata()
    {
        var chart = await ReadText("'PenguinTools dialect: extended\n" + Header +
            "@BEAT\t2\t0\t4\n@CLKCNT\t7\n#1'961:t44\n");
        var path = TestTempPaths.Create(".ugc");
        try
        {
            Assert.True((await new UgcChartWriter(path, chart).WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
            var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
            Assert.Contains("'PenguinTools dialect: original", lines);
            Assert.DoesNotContain("'PenguinTools dialect: extended", lines);
            Assert.Contains("@BEAT\t2\t0\t4", lines);
            Assert.Contains("#2'1:tK4", lines);
            await File.WriteAllLinesAsync(path, lines.Where(l => !l.StartsWith("@COPYRIGHT", StringComparison.Ordinal)),
                TestContext.Current.CancellationToken);
            var native = await Read(path);
            Assert.Contains(native.Events.Children.OfType<U.BeatEvent>(), b => b.Bar == 2 && b.Numerator == 0);
            Assert.Equal(2881, Assert.Single(native.Notes.Children.OfType<U.Tap>()).Tick.Original);
            Assert.Equal(7, native.Extras.ClickCount);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task OlderUgcExtensions_AreDecodedByTheirFields()
    {
        var chart = await ReadText(Header + "@FLAG\tHIPRECISION\tTRUE\n#0'0:s\uE0014\n#240>n24\n#480>s64\n" +
            "#1'0:C04286Y240\n#480>c451E\n#2'0:t\uE1004\n");
        var slide = Assert.Single(chart.Notes.Children.OfType<U.Slide>());
        Assert.Equal(-2, slide.Lane);
        Assert.True(slide.NoLine);
        Assert.False(((U.SlideJoint)slide.FirstChild!).NoLine);
        Assert.Equal(36, Assert.Single(chart.Notes.Children.OfType<U.Tap>()).Lane);
        var crash = Assert.Single(chart.Notes.Children.OfType<U.AirCrash>());
        Assert.Equal(240, crash.Density.Original);
        Assert.Equal(AirLadderAttr.AxisY, crash.Attr);
        Assert.Equal(80, crash.Height);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task BinarySlideSegmentFlag_IsDistinctFromWholeSlideVisibility(int version)
    {
        var chart = await ReadText(Header + "#0'0:s440\n#240>s64\n#480>s44\n");
        var path = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct)).Succeeded);
            if (version == 2)
            {
                await LegacyEditorFixture.DowngradeAsync(path, ct);
            }

            var bytes = await File.ReadAllBytesAsync(path, ct);
            var offset = bytes.AsSpan().IndexOf("dat2"u8) + 8;
            bytes[offset + 4] = 127;
            await File.WriteAllBytesAsync(path, bytes, ct);
            var restored = Assert.Single((await Read(path)).Notes.Children.OfType<U.Slide>());
            Assert.True(restored.NoLine);
            Assert.False(((U.SlideJoint)restored.FirstChild!).NoLine);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(11, AirLadderAttr.AxisY)]
    [InlineData(12, AirLadderAttr.AxisZ)]
    public async Task BinaryVersion2CrashAxis_UsesTheLegacyDirectionField(byte direction, AirLadderAttr expected)
    {
        var chart = await ReadText(Header + "#0'0:C4480,240\n#480>c448\n");
        var path = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct)).Succeeded);
            await LegacyEditorFixture.DowngradeAsync(path, ct);
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var offset = bytes.AsSpan().IndexOf("dat2"u8) + 8;
            bytes[offset + 2] = direction;
            await File.WriteAllBytesAsync(path, bytes, ct);
            Assert.Equal(expected, Assert.Single((await Read(path)).Notes.Children.OfType<U.AirCrash>()).Attr);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LegacyCurve_IsRetainedInMgxcAndFailsUnsupportedExportsWithoutReplacingAFile()
    {
        var chart = await ReadText(Header + "#0'0:s440\n#240>v84\n#480>s44\n");
        var native = TestTempPaths.Create(".mgxc");
        var text = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(native, chart)).WriteAsync(ct)).Succeeded);
            var restored = await Read(native);
            var control = Assert.Single(Assert.Single(restored.Notes.Children.OfType<U.Slide>()).Children.OfType<U.SlideJoint>(), n => n.IsCurveControl);
            Assert.Equal(U.CurveInterpretation.Legacy, control.CurveInterpretation);
            Assert.False(new C2SChartConverter(new C2SConvertRequest(restored)).Convert().Succeeded);
            await File.WriteAllTextAsync(text, "keep existing output", ct);
            var error = await Assert.ThrowsAsync<TimedDiagnosticException>(() => new UgcChartWriter(text, restored).WriteAsync(ct));
            Assert.Equal(MsgKeys.MgCrit_Unsupported_legacy_curve, error.Descriptor.Key);
            Assert.Equal("keep existing output", await File.ReadAllTextAsync(text, ct));
        }
        finally { File.Delete(native); File.Delete(text); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitLegacyCrushJudgements_AreRetainedInNativeFormats(bool binary)
    {
        var chart = await ReadText(Header + "#0'0:t44\n");
        var root = new U.AirCrash { Tick = 480, Lane = 4, Width = 4, Height = 80, HasExplicitJudgements = true, Density = 0 };
        root.AppendChild(new U.AirCrashJoint { Tick = 720, Lane = 5, Width = 4, Height = 100, IsJudgement = true });
        root.AppendChild(new U.AirCrashJoint { Tick = 1200, Lane = 6, Width = 4, Height = 80 });
        root.AppendChild(new U.AirCrashJoint { Tick = 1440, Lane = 4, Width = 4, Height = 80, IsJudgement = true });
        chart.Notes.AppendChild(root);
        var path = TestTempPaths.Create(binary ? ".mgxc" : ".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            for (var i = 0; i < 2; i++)
            {
                var written = binary ? await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct)
                    : await new UgcChartWriter(path, chart).WriteAsync(ct);
                Assert.True(written.Succeeded, written.ToString());
                chart = await Read(path);
                var restored = Assert.Single(chart.Notes.Children.OfType<U.AirCrash>());
                Assert.True(restored.HasExplicitJudgements);
                Assert.Equal<bool>([true, false, true], restored.Children.OfType<U.AirCrashJoint>().Select(n => n.IsJudgement));
                Assert.False(new C2SChartConverter(new C2SConvertRequest(chart)).Convert().Succeeded);
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, AirLadderAttr.AxisY, 0)]
    [InlineData(true, AirLadderAttr.AxisY, 0)]
    [InlineData(false, AirLadderAttr.AxisZ, 0)]
    [InlineData(true, AirLadderAttr.AxisZ, 0)]
    [InlineData(false, AirLadderAttr.DEF, 120)]
    [InlineData(true, AirLadderAttr.DEF, 120)]
    public async Task ExplicitLegacyCrushConflictingAttributes_RejectWithoutReplacingOutput(bool binary, AirLadderAttr axis, int density)
    {
        var chart = await ReadText(Header + "#0'0:t44\n");
        var root = new U.AirCrash
        {
            Tick = 480,
            Lane = 4,
            Width = 4,
            Height = 80,
            HasExplicitJudgements = true,
            Density = density,
            Attr = axis
        };
        root.AppendChild(new U.AirCrashJoint { Tick = 960, Lane = 4, Width = 4, Height = 80, IsJudgement = true });
        chart.Notes.AppendChild(root);
        var path = TestTempPaths.Create(binary ? ".mgxc" : ".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "keep existing output", ct);
            var written = binary ? await new MgxcChartWriter(new(path, chart)).WriteAsync(ct)
                : await new UgcChartWriter(path, chart).WriteAsync(ct);
            Assert.False(written.Succeeded);
            Assert.Contains(written.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.MgCrit_Legacy_crash_attributes);
            Assert.Equal("keep existing output", await File.ReadAllTextAsync(path, ct));
            Assert.Equal(axis, root.Attr);
            Assert.Equal(density, root.Density.Original);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LockedCurveIdentity_IsUpdatedForNativeNormalizationButNotForEditedPaths(bool edited)
    {
        var chart = await ReadText(Header + "#0'0:t44\n");
        var tap = Assert.Single(chart.Notes.Children.OfType<U.Tap>());
        var air = new U.AirSlide { Height = 80 };
        air.AppendChild(new U.AirSlideJoint { Tick = 480, Lane = 8, Width = 4, Height = 100, Joint = Joint.C });
        air.AppendChild(new U.AirSlideJoint { Tick = 960, Lane = 4, Width = 4, Height = 80, Joint = Joint.D });
        tap.MakePair(air);
        chart.Notes.AppendChild(air);
        var path = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct)).Succeeded);
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var at = bytes.AsSpan().IndexOf("dat2"u8) + 8;
            var records = new List<int[]>();
            for (; at < bytes.Length; at += 20)
            {
                if (bytes[at] != 9)
                {
                    continue;
                }

                bytes[at + 2] = 0; // Native saves can omit the root direction; the writer emits Up.
                records.Add([9, bytes[at + 1], 0, bytes[at + 3], bytes[at + 4], (sbyte)bytes[at + 7], bytes[at + 6],
                    BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 8)), 0,
                    BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 12)), BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 16))]);
            }
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(1);
                writer.Write(1);
                writer.Write(0);
                WriteLockPath(writer, records);
                WriteLockPath(writer, records);
                writer.Write(0); // No frozen samples in this minimal editable snapshot.
            }
            await File.WriteAllBytesAsync(path, WithLockMetadata(bytes, stream.ToArray()), ct);
            chart = await Read(path);
            Assert.Single(chart.Extras.LockedCurves);
            if (edited)
            {
                Assert.Single(chart.Notes.Children.OfType<U.AirSlide>()).FirstChild!.Lane++;
            }

            Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct)).Succeeded);
            bytes = await File.ReadAllBytesAsync(path, ct);
            Assert.Equal(!edited, bytes.AsSpan().IndexOf("xbl1"u8) >= 0);
            if (!edited)
            {
                chart = await Read(path);
                Assert.Single(chart.Extras.LockedCurves);
            }
        }
        finally { File.Delete(path); }
    }

    private static void WriteLockPath(BinaryWriter writer, List<int[]> records)
    {
        foreach (var value in records[0])
        {
            writer.Write(value);
        }

        writer.Write(records.Count - 1);
        foreach (var record in records.Skip(1))
        {
            foreach (var value in record)
            {
                writer.Write(value);
            }
        }
    }

    private static byte[] WithLockMetadata(byte[] source, byte[] payload)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("xbl1"u8);
        writer.Write((ushort)4);
        writer.Write(checked((ushort)(payload.Length + 8)));
        writer.Write(payload.Length);
        writer.Write(0);
        writer.Write(payload);
        var field = stream.ToArray();
        var at = 20 + BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(16)) - 8;
        var result = source[..at].Concat(field).Concat(source[at..]).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), result.Length - 8);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(16), BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(16)) + field.Length);
        return result;
    }

    private static async Task<string[]> C2sRows(U.Chart chart)
    {
        var converted = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var path = TestTempPaths.Create(".c2s");
        try
        {
            Assert.True((await new C2SChartWriter(new C2SWriteRequest(path, converted.Value!)).WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
            return await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
        }
        finally { File.Delete(path); }
    }

    private static async Task<U.Chart> ReadText(string text)
    {
        var path = TestTempPaths.Create(".ugc");
        try
        {
            await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);
            return await Read(path);
        }
        finally { File.Delete(path); }
    }

    private static async Task<U.Chart> Read(string path)
    {
        var ct = TestContext.Current.CancellationToken;
        var result = path.EndsWith(".mgxc", StringComparison.Ordinal)
            ? await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct)
            : await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
        Assert.True(result.Succeeded, result.ToString());
        return result.Value!;
    }
}
