using System.Diagnostics;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using PenguinTools.Core.Xml;
using PenguinTools.Media;
using Xunit;

namespace PenguinTools.Tests.Media;

public sealed class AudioConverterTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 0)]
    public async Task ConvertAsync_PreviewUsesOnlyTheMaterializedOpeningMeasure(
        bool enabled, bool fixedOrigin, int expectedPrerollSeconds)
    {
        var directory = CreateDirectory();
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var source = Path.Combine(directory, "source.wav");
            await File.WriteAllTextAsync(source, "source", ct);
            var meta = new Meta
            {
                Id = 1234,
                BgmFilePath = source,
                BgmEnableBarOffset = enabled,
                BgmFixedZeroOrigin = fixedOrigin,
                BgmPreviewStart = 10,
                BgmPreviewStop = 20
            };
            var tool = new RecordingAudioService();
            var result = await new AudioConverter(new AudioConvertRequest(meta, directory,
                Path.Combine(directory, "working.wav"), AudioConvertRequest.DefaultHcaEncryptionKey), tool)
                .ConvertAsync(ct);

            Assert.True(result.Succeeded);
            Assert.Equal((decimal)expectedPrerollSeconds, tool.NormalizedOffset);
            var request = Assert.IsType<CriConvertRequest>(tool.CriRequest);
            Assert.Equal((10L + expectedPrerollSeconds) * 1000, request.PreviewStartMs);
            Assert.Equal((20L + expectedPrerollSeconds) * 1000, request.PreviewStopMs);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ConvertAsync_FailedNormalizationDoesNotEncodeStaleAudioOrOverwriteCueMetadata()
    {
        var directory = CreateDirectory();
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var source = Path.Combine(directory, "source.wav");
            var working = Path.Combine(directory, "working.wav");
            await File.WriteAllTextAsync(source, "source", ct);
            await File.WriteAllTextAsync(working, "stale audio from another conversion", ct);
            var cue = new CueFileXml(1234);
            var cueDirectory = Path.Combine(directory, cue.DataName);
            Directory.CreateDirectory(cueDirectory);
            var cuePath = Path.Combine(cueDirectory, "CueFile.xml");
            await File.WriteAllTextAsync(cuePath, "existing metadata", ct);
            var tool = new RecordingAudioService { NormalizationExitCode = InterExitCode.Failure };

            var error = await Assert.ThrowsAsync<DiagnosticException>(() => new AudioConverter(
                new AudioConvertRequest(new Meta { Id = 1234, BgmFilePath = source }, directory,
                    working, AudioConvertRequest.DefaultHcaEncryptionKey), tool).ConvertAsync(ct));

            Assert.Equal(MsgKeys.Error_Invalid_audio, error.Descriptor.Key);
            Assert.Null(tool.CriRequest);
            Assert.Equal("existing metadata", await File.ReadAllTextAsync(cuePath, ct));
            Assert.Equal("stale audio from another conversion", await File.ReadAllTextAsync(working, ct));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class RecordingAudioService : IAudioService
    {
        public InterExitCode NormalizationExitCode { get; init; } = InterExitCode.Success;
        public decimal NormalizedOffset { get; private set; }
        public CriConvertRequest? CriRequest { get; private set; }

        public Task<ProcessCommandResult> NormalizeAudioAsync(string src, string dst, decimal offset,
            CancellationToken ct = default)
        {
            NormalizedOffset = offset;
            return Task.FromResult(new ProcessCommandResult(new ProcessStartInfo { FileName = "test" },
                (int)NormalizationExitCode, string.Empty, string.Empty));
        }

        public Task ConvertCriAsync(CriConvertRequest request, CancellationToken ct = default)
        {
            CriRequest = request;
            return Task.CompletedTask;
        }

        public Task<MediaValidationResult> CheckAudioValidAsync(string src, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CriExtractResult> ExtractCriAudioAsync(CriExtractOptions options, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
