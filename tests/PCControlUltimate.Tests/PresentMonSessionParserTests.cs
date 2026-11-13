using System;
using System.IO;
using System.Linq;
using PCControlUltimate.Gaming.PresentMon;
using Xunit;

namespace PCControlUltimate.Tests;

public sealed class PresentMonSessionParserTests
{
    [Fact]
    public void ParseCsvFile_OnePercentLowUsesSlowestOnePercentOfFrames()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pccontrol-presentmon-{Guid.NewGuid():N}.csv");
        try
        {
            string[] rows = new[] { "msBetweenPresents" }
                .Concat(Enumerable.Repeat("10", 99))
                .Append("40")
                .ToArray();
            File.WriteAllLines(path, rows);

            PresentMonSummary summary = PresentMonSessionParser.ParseCsvFile(path);

            Assert.Equal(100, summary.TotalFramesCount);
            Assert.Equal(25.0, summary.OnePercentLowFps, 1);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void ParseCsvFile_OnePercentLowAveragesAllFramesInSlowestPercent()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pccontrol-presentmon-{Guid.NewGuid():N}.csv");
        try
        {
            string[] rows = new[] { "msBetweenPresents" }
                .Concat(Enumerable.Repeat("10", 198))
                .Append("20")
                .Append("40")
                .ToArray();
            File.WriteAllLines(path, rows);

            PresentMonSummary summary = PresentMonSessionParser.ParseCsvFile(path);

            Assert.Equal(200, summary.TotalFramesCount);
            Assert.Equal(33.3, summary.OnePercentLowFps, 1);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
