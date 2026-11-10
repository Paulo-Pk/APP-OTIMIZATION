using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PCControlUltimate.Gaming.PresentMon;

public record PresentMonSummary(
    double AverageFps,
    double MinFps,
    double OnePercentLowFps,
    double AverageFrametimeMs,
    double FrametimeStandardDeviationMs,
    int TotalFramesCount
);

public static class PresentMonSessionParser
{
    private static readonly string[] TargetColumnNames = new[]
    {
        "msBetweenPresents",
        "msBetweenDisplayChange",
        "FrameTime",
        "msCPUBusy",
        "msGPUBusy"
    };

    public static PresentMonSummary ParseCsvFile(string csvFilePath)
    {
        if (!File.Exists(csvFilePath))
            return new PresentMonSummary(0, 0, 0, 0, 0, 0);

        using var reader = new StreamReader(csvFilePath);
        string? headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
            return new PresentMonSummary(0, 0, 0, 0, 0, 0);

        // Dynamically find column index
        var headers = headerLine.Split(',').Select(h => h.Trim().Trim('"')).ToArray();
        int frametimeColIdx = -1;

        foreach (var candidate in TargetColumnNames)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                if (string.Equals(headers[i], candidate, StringComparison.OrdinalIgnoreCase))
                {
                    frametimeColIdx = i;
                    break;
                }
            }
            if (frametimeColIdx != -1) break;
        }

        // If no matching header was found, return empty summary
        if (frametimeColIdx == -1)
            return new PresentMonSummary(0, 0, 0, 0, 0, 0);

        var frametimesMs = new List<double>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(',');
            if (parts.Length > frametimeColIdx)
            {
                string rawVal = parts[frametimeColIdx].Trim().Trim('"');
                if (double.TryParse(rawVal, NumberStyles.Any, CultureInfo.InvariantCulture, out double ftMs) &&
                    ftMs > 0.1 && ftMs < 1000.0) // Filter invalid anomalous outlier spikes
                {
                    frametimesMs.Add(ftMs);
                }
            }
        }

        if (frametimesMs.Count == 0)
            return new PresentMonSummary(0, 0, 0, 0, 0, 0);

        double avgFt = frametimesMs.Average();
        double avgFps = avgFt > 0 ? (1000.0 / avgFt) : 0;

        // Sort descending to find worst frametimes (which correspond to lowest FPS)
        var sorted = frametimesMs.OrderByDescending(f => f).ToList();
        
        // 1% Low is the 99th percentile highest frametime
        int onePctIndex = Math.Min(sorted.Count - 1, Math.Max(0, (int)(sorted.Count * 0.01)));
        double onePctFt = sorted[onePctIndex];
        double onePctLowFps = onePctFt > 0 ? (1000.0 / onePctFt) : 0;

        // Min FPS corresponds to absolute highest frametime in capture
        double maxFt = sorted.First();
        double minFps = maxFt > 0 ? (1000.0 / maxFt) : 0;

        // Standard deviation of frametimes
        double variance = frametimesMs.Select(v => Math.Pow(v - avgFt, 2)).Sum() / frametimesMs.Count;
        double stDev = Math.Sqrt(variance);

        return new PresentMonSummary(
            AverageFps: Math.Round(avgFps, 1),
            MinFps: Math.Round(minFps, 1),
            OnePercentLowFps: Math.Round(onePctLowFps, 1),
            AverageFrametimeMs: Math.Round(avgFt, 2),
            FrametimeStandardDeviationMs: Math.Round(stDev, 2),
            TotalFramesCount: frametimesMs.Count
        );
    }
}