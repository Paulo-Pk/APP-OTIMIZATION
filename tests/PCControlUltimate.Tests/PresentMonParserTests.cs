using System.IO;
using PCControlUltimate.Gaming.PresentMon;
using Xunit;

namespace PCControlUltimate.Tests;

public class PresentMonParserTests
{
    [Fact]
    public void ParseCsvFile_NonExistentFile_ReturnsZeroes()
    {
        var summary = PresentMonSessionParser.ParseCsvFile("non_existent_file.csv");

        Assert.Equal(0, summary.AverageFps);
        Assert.Equal(0, summary.TotalFramesCount);
    }

    [Fact]
    public void ParseCsvFile_WithMsBetweenPresentsHeader_CalculatesAccurately()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            // Header with msBetweenPresents not in column 0
            string csvData = "Application,ProcessID,msBetweenPresents,msCPUBusy\n" +
                             "game.exe,1234,16.67,10.0\n" +
                             "game.exe,1234,16.67,11.2\n" +
                             "game.exe,1234,16.67,9.8\n" +
                             "game.exe,1234,33.33,15.0\n"; // one 30fps drop

            File.WriteAllText(tempFile, csvData);

            var summary = PresentMonSessionParser.ParseCsvFile(tempFile);

            Assert.Equal(4, summary.TotalFramesCount);
            Assert.True(summary.AverageFps > 40.0 && summary.AverageFps < 65.0);
            Assert.True(summary.OnePercentLowFps > 20.0);
            Assert.True(summary.AverageFrametimeMs > 15.0);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ParseCsvFile_WithFrameTimeHeader_DetectsColumnDynamically()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            string csvData = "TimeInSeconds,FrameTime,SyncInterval\n" +
                             "0.016,10.0,1\n" +  // 100 fps
                             "0.026,10.0,1\n" +
                             "0.036,10.0,1\n";

            File.WriteAllText(tempFile, csvData);

            var summary = PresentMonSessionParser.ParseCsvFile(tempFile);

            Assert.Equal(3, summary.TotalFramesCount);
            Assert.Equal(100.0, summary.AverageFps);
            Assert.Equal(10.0, summary.AverageFrametimeMs);
            Assert.NotNull(summary.RecentFrameTimesMs);
            Assert.Equal(3, summary.RecentFrameTimesMs.Count);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
