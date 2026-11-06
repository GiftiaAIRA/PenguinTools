using System.Xml.Linq;
using PenguinTools.Core.Metadata;
using PenguinTools.Core.Xml;
using Xunit;

namespace PenguinTools.Tests.Workflow;

public sealed class MusicXmlTests
{
    [Fact]
    public async Task SavedMusicXml_PreservesEachDifficultyMetadataAndEnablesNonMainUltima()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var master = new Meta
            {
                Id = 4321,
                Difficulty = Difficulty.Master,
                Designer = "Master designer",
                MainBpm = 137.25m,
                Level = 14.6m
            };
            var ultima = master with
            {
                Difficulty = Difficulty.Ultima,
                Designer = "Ultima designer",
                MainBpm = 180.5m,
                Level = 15.2m
            };
            var music = new MusicXml(new Dictionary<Difficulty, Meta>
            {
                [Difficulty.Master] = master,
                [Difficulty.Ultima] = ultima
            }, Difficulty.Master);

            var folder = await music.SaveDirectoryAsync(directory);
            var document = XDocument.Load(Path.Combine(folder, "Music.xml"));
            var root = Assert.IsType<XElement>(document.Root);
            Assert.True((bool)root.Element("enableUltima")!);
            var rows = root.Element("fumens")!.Elements("MusicFumenData")
                .ToDictionary(row => (int)row.Element("type")!.Element("id")!);
            Assert.Equal(master.Designer, (string?)rows[3].Element("notesDesigner"));
            Assert.Equal(master.MainBpm, (decimal)rows[3].Element("defaultBpm")!);
            Assert.Equal(ultima.Designer, (string?)rows[4].Element("notesDesigner"));
            Assert.Equal(ultima.MainBpm, (decimal)rows[4].Element("defaultBpm")!);
            Assert.True((bool)rows[4].Element("enable")!);
            Assert.False((bool)rows[0].Element("enable")!);
            Assert.Equal(string.Empty, (string?)rows[0].Element("notesDesigner"));
            Assert.Equal(0m, (decimal)rows[0].Element("defaultBpm")!);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(Difficulty.Master, false)]
    [InlineData(Difficulty.Ultima, true)]
    public void MusicXml_SingleDifficultyRetainsUltimaAvailability(Difficulty difficulty, bool enabled)
    {
        var meta = new Meta { Id = 4321, Difficulty = difficulty };
        var music = new MusicXml(new Dictionary<Difficulty, Meta> { [difficulty] = meta }, difficulty);

        Assert.Equal(enabled, music.EnableUltima);
    }

    [Fact]
    public void MusicXml_UsesInitialBpmWhenDisplayedBpmIsUnset()
    {
        var meta = new Meta { Id = 4321, Difficulty = Difficulty.Master, BgmInitialBpm = 149.5m };
        var music = new MusicXml(new Dictionary<Difficulty, Meta> { [meta.Difficulty] = meta }, meta.Difficulty);

        Assert.Equal(149.5m, music[Difficulty.Master].DefaultBpm);
        Assert.Equal(0m, meta.MainBpm);
        Assert.Equal(0m, music[Difficulty.Basic].DefaultBpm);
    }
}
