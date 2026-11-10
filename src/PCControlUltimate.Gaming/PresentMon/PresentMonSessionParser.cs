using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PCControlUltimate.Gaming.PresentMon;

public record PresentMonSummary(
    double AverageFps,
    double MinFps,
    double OnePercentLowFps,
    double AverageFrametimeMs,
    double FrametimeStandardDeviationMs,
    int TotalFramesCount
);

public record PresentMonCaptureResponse(
    bool Success,
    string? ProcessName,
    string? PresentMonVersion,
    string? CsvFilePath,
    PresentMonSummary? Summary,
    string? ErrorMessage
);

public static class PresentMonSessionParser
{
    private static readonly string[] TargetColumnNames = new[]
    {
        "msBetweenPresents",
        "msBetweenDisplayChange",
        "FrameTime",
        "FrameTimeMs",
        "msCPUBusy",
        "msGPUBusy"
    };

    public static (bool isInstalled, string? binaryPath, string? version) CheckPresentMonInstallation()
    {
        string[] candidatePaths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "PresentMon.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Intel", "PresentMon", "PresentMon.exe"),
            @"C:\Program Files\Intel\PresentMon\PresentMon.exe"
        };

        foreach (var path in candidatePaths)
        {
            if (File.Exists(path))
            {
                try
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(path);
                    return (true, path, versionInfo.FileVersion ?? "2.0.0");
                }
                catch
                {
                    return (true, path, "Detected");
                }
            }
        }

        return (false, null, null);
    }

    public static async Task<PresentMonCaptureResponse> CaptureProcessSessionAsync(
        string processName,
        int durationSeconds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return new PresentMonCaptureResponse(false, null, null, null, null, "Nome do processo do jogo não informado.");
        }

        var (isInstalled, binaryPath, version) = CheckPresentMonInstallation();
        if (!isInstalled || string.IsNullOrEmpty(binaryPath))
        {
            return new PresentMonCaptureResponse(
                false,
                processName,
                null,
                null,
                null,
                "Intel PresentMon não encontrado no sistema. Instale o PresentMon para capturar telemetria real de quadros."
            );
        }

        int clampedSeconds = Math.Clamp(durationSeconds, 3, 120);
        string cleanProcName = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();

        var runningProcs = Process.GetProcessesByName(cleanProcName);
        if (runningProcs.Length == 0)
        {
            return new PresentMonCaptureResponse(
                false,
                processName,
                version,
                null,
                null,
                $"O processo '{cleanProcName}.exe' não está em execução no momento."
            );
        }

        string outputDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PCControlUltimate",
            "PresentMonSessions"
        );
        Directory.CreateDirectory(outputDir);

        string csvPath = Path.Combine(outputDir, $"PresentMon_{cleanProcName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = $"-process_name \"{cleanProcName}.exe\" -timed {clampedSeconds} -output_file \"{csvPath}\" -terminate_after_timed -no_top",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return new PresentMonCaptureResponse(false, processName, version, null, null,
                    "Não foi possível iniciar o processo de captura do PresentMon.");
            }

            await proc.WaitForExitAsync(cancellationToken);

            var summary = ParseCsvFile(csvPath);
            if (summary.TotalFramesCount == 0)
            {
                return new PresentMonCaptureResponse(false, processName, version, csvPath, summary,
                    "Nenhum quadro renderizado foi capturado durante a janela de amostragem.");
            }

            return new PresentMonCaptureResponse(true, processName, version, csvPath, summary, null);
        }
        catch (Exception ex)
        {
            return new PresentMonCaptureResponse(false, processName, version, csvPath, null,
                $"Erro durante captura PresentMon: {ex.Message}");
        }
    }

    public static PresentMonSummary ParseCsvFile(string csvFilePath)
    {
        if (string.IsNullOrWhiteSpace(csvFilePath) || !File.Exists(csvFilePath))
            return new PresentMonSummary(0, 0, 0, 0, 0, 0);

        using var reader = new StreamReader(csvFilePath);
        string? headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
            return new PresentMonSummary(0, 0, 0, 0, 0, 0);

        // Dynamically find frametime column index from PresentMon v1 or v2 CSV headers
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
                    ftMs > 0.1 && ftMs < 1000.0)
                {
                    frametimesMs.Add(ftMs);
                }
            }
        }

        if (frametimesMs.Count == 0)
            return new PresentMonSummary(0, 0, 0, 0, 0, 0);

        double avgFt = frametimesMs.Average();
        double avgFps = avgFt > 0 ? (1000.0 / avgFt) : 0;

        // Sort descending to find highest frametimes (lowest FPS)
        var sortedDescending = frametimesMs.OrderByDescending(f => f).ToList();

        // 1% Low is the 99th percentile frametime
        int onePctIndex = Math.Min(sortedDescending.Count - 1, Math.Max(0, (int)(sortedDescending.Count * 0.01)));
        double onePctFt = sortedDescending[onePctIndex];
        double onePctLowFps = onePctFt > 0 ? (1000.0 / onePctFt) : 0;

        double maxFt = sortedDescending.First();
        double minFps = maxFt > 0 ? (1000.0 / maxFt) : 0;

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