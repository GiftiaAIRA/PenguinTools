using System.Text;
using PenguinTools.Chart.Parser;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;

namespace PenguinTools.Tests.Parser;

public sealed class MgxcFieldLengthTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(int.MinValue, 0)]
    [InlineData(129, 128)]
    [InlineData(int.MaxValue, 8)]
    public void InvalidStringLength_ReportsDiagnosticWithoutAllocating(int length, int available)
    {
        using var reader = new BinaryReader(new MemoryStream(new byte[available]));
        var error = Assert.Throws<DiagnosticException>(() => reader.ReadUtf8String(length));
        Assert.Equal(MsgKeys.Error_Size_Incompatible, error.ToDiagnostic().Message.Key);
        Assert.Equal(0, reader.BaseStream.Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TruncatedStringField_ReportsDiagnostic(bool wide)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        if (wide)
        {
            writer.Write(4);
            writer.Write(-1);
        }
        else
        {
            writer.Write((short)4);
            writer.Write((short)-1);
        }
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        var error = Assert.Throws<DiagnosticException>(() => wide ? reader.ReadWideField() : reader.ReadField());
        Assert.Equal(MsgKeys.Error_Size_Incompatible, error.ToDiagnostic().Message.Key);
    }

    [Theory]
    [InlineData(32767)]
    [InlineData(32768)]
    [InlineData(65535)]
    public void SignedInlineNumberAndUnsignedStringLength_RemainReadable(int length)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((short)1);
        writer.Write((short)-1);
        writer.Write((short)4);
        writer.Write((ushort)length);
        writer.Write(Encoding.UTF8.GetBytes(new string('A', length)));
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        Assert.Equal(-1, reader.ReadField());
        Assert.Equal(new string('A', length), reader.ReadField());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(129)]
    [InlineData(int.MaxValue)]
    public void InvalidBlockLength_ReportsDiagnostic(int length)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("meta"u8);
        writer.Write(length);
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        var error = Assert.Throws<DiagnosticException>(() => reader.ReadBlock("meta", _ => Assert.Fail("Invalid block was read")));
        Assert.Equal(MsgKeys.Error_Size_Incompatible, error.ToDiagnostic().Message.Key);
    }

    [Fact]
    public async Task NativeWrappedMetadataLength_ReturnsParseFailure()
    {
        var path = TestTempPaths.Create(".mgxc");
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write("MGXC"u8);
                writer.Write(20);
                writer.Write(4);
                writer.Write("meta"u8);
                writer.Write(8);
                writer.Write("lcpy"u8);
                writer.Write((short)4);
                writer.Write((short)-3681);
            }
            var result = await new MgxcParser(new(path, TestAssets.Load()), TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Error_Size_Incompatible);
        }
        finally { File.Delete(path); }
    }
}
