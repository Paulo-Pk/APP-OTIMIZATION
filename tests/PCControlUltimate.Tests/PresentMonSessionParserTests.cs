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


    [Fact]
    public void ParseCsvFile_HandlesCommasInsideQuotedApplicationNames()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pccontrol-presentmon-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllLines(path, new[]
            {
                "Application,ProcessID,msBetweenPresents",
                "\"Game, Inc.\",123,16.0",
                "\"Another, Studio\",456,20.0"
            });

            PresentMonSummary summary = PresentMonSessionParser.ParseCsvFile(path);

            Assert.Equal(2, summary.TotalFramesCount);
            Assert.Equal(55.6, summary.AverageFps, 1);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void ParseCsvFile_HandlesEscapedQuotesInsideQuotedFields()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pccontrol-presentmon-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllLines(path, new[]
            {
                "Application,ProcessID,msBetweenPresents",
                "\"Game \"\"Ultimate\"\", Inc.\",123,10.0"
            });

            PresentMonSummary summary = PresentMonSessionParser.ParseCsvFile(path);

            Assert.Equal(1, summary.TotalFramesCount);
            Assert.Equal(100.0, summary.AverageFps, 1);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Theory]
    [InlineData("msCPUBusy")]
    [InlineData("msGPUBusy")]
    public void ParseCsvFile_DoesNotTreatCpuOrGpuBusyAsFrametime(string columnName)
    {
        string path = Path.Combine(Path.GetTempPath(), $"pccontrol-presentmon-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllLines(path, new[] { columnName, "5", "10", "15" });

            PresentMonSummary summary = PresentMonSessionParser.ParseCsvFile(path);

            Assert.Equal(0, summary.TotalFramesCount);
            Assert.Empty(summary.RecentFrameTimesMs);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

}
