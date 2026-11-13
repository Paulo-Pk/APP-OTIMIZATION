using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PCControlUltimate.Gaming.PresentMon;

public record RunningProcessInfo(
    int ProcessId,
    string ProcessName,
    string MainWindowTitle
);

public record PresentMonSummary(
    double AverageFps,
    double MinFps,
    double OnePercentLowFps,
    double AverageFrametimeMs,
    double FrametimeStandardDeviationMs,
    int TotalFramesCount,
    IReadOnlyList<double>? RecentFrameTimesMs = null
);

public record PresentMonCaptureResponse(
    bool Success,
    string? ProcessName,
    string? PresentMonVersion,
    string? CsvFilePath,
    int ExitCode,
    string? StandardOutput,
    string? StandardError,
    PresentMonSummary? Summary,
    string? ErrorMessage,
    string TimestampUtc = ""
);

public static class PresentMonSessionParser
{
    // Strict process name regex: only alphanumeric, dash, underscore, dot, space (no shell metacharacters, quotes, or paths)
    private static readonly Regex SafeProcessNameRegex = new(@"^[a-zA-Z0-9_\-\.\s]{1,64}$", RegexOptions.Compiled);

    private static readonly ConcurrentQueue<PresentMonCaptureResponse> _sessionHistory = new();

    private static readonly string[] TargetColumnNames = new[]
    {
        "msBetweenPresents",
        "msBetweenDisplayChange",
        "FrameTime",
        "FrameTimeMs",
        "MsBetweenPresents",
        "msCPUBusy",
        "msGPUBusy"
    };

    public static IReadOnlyList<PresentMonCaptureResponse> GetSessionHistory() => _sessionHistory.ToArray();

    public static bool IsProcessNameValid(string? processName) =>
        IsProcessNameValid(processName, out _);

    public static bool IsProcessNameValid(string? processName, out string sanitizedExecutableName)
    {
        sanitizedExecutableName = string.Empty;
        if (string.IsNullOrWhiteSpace(processName)) return false;

        string trimmed = processName.Trim();
        if (trimmed.Contains("..") || trimmed.Contains('/') || trimmed.Contains('\\') ||
            trimmed.Contains('"') || trimmed.Contains('\'') || trimmed.Contains(';') ||
            trimmed.Contains('&') || trimmed.Contains('|') || trimmed.Contains('`'))
        {
            return false;
        }

        if (!SafeProcessNameRegex.IsMatch(trimmed))
            return false;

        string withoutExe = trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed.Substring(0, trimmed.Length - 4).Trim()
            : trimmed;

        if (string.IsNullOrWhiteSpace(withoutExe))
            return false;

        sanitizedExecutableName = $"{withoutExe}.exe";
        return true;
    }

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
                    return (true, path, "Detectado");
                }
            }
        }

        return (false, null, null);
    }

    public static IReadOnlyList<RunningProcessInfo> ListAvailableProcesses()
    {
        var list = new List<RunningProcessInfo>();
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.Id <= 4 || string.IsNullOrWhiteSpace(proc.ProcessName))
                        continue;

                    string title = string.Empty;
                    try { title = proc.MainWindowTitle ?? string.Empty; } catch { }

                    list.Add(new RunningProcessInfo(proc.Id, proc.ProcessName, title));
                }
                catch { }
                finally
                {
                    try { proc.Dispose(); } catch { }
                }
            }
        }
        catch { }

        return list
            .OrderByDescending(p => !string.IsNullOrWhiteSpace(p.MainWindowTitle))
            .ThenBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static async Task<PresentMonCaptureResponse> CaptureProcessSessionAsync(
        string processName,
        int durationSeconds,
        CancellationToken cancellationToken = default)
    {
        string timestamp = DateTime.UtcNow.ToString("O");

        if (!IsProcessNameValid(processName, out string safeExeName))
        {
            return new PresentMonCaptureResponse(
                false, processName, null, null, -1, null, null, null,
                "Nome de processo inválido ou contém caracteres proibidos.",
                timestamp
            );
        }

        var (isInstalled, binaryPath, version) = CheckPresentMonInstallation();
        if (!isInstalled || string.IsNullOrEmpty(binaryPath))
        {
            return new PresentMonCaptureResponse(
                false, safeExeName, null, null, -1, null, null, null,
                "Intel PresentMon não encontrado no sistema. Instale o PresentMon para capturar telemetria real de quadros.",
                timestamp
            );
        }

        int clampedSeconds = Math.Clamp(durationSeconds, 3, 120);
        string procNameWithoutExt = Path.GetFileNameWithoutExtension(safeExeName);

        var runningProcs = Process.GetProcessesByName(procNameWithoutExt);
        int matchingProcessCount = runningProcs.Length;
        foreach (var rp in runningProcs)
        {
            try { rp.Dispose(); } catch { }
        }

        if (matchingProcessCount > 1)
        {
            return new PresentMonCaptureResponse(
                false, safeExeName, version, null, -1, null, null, null,
                $"Há {matchingProcessCount} instâncias de '{safeExeName}' em execução. A captura por nome seria ambígua; feche as instâncias extras e tente novamente.",
                timestamp
            );
        }

        if (matchingProcessCount == 0)
        {
            return new PresentMonCaptureResponse(
                false, safeExeName, version, null, -1, null, null, null,
                $"O processo '{safeExeName}' não está em execução no momento.",
                timestamp
            );
        }

        string outputDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PCControlUltimate",
            "PresentMonSessions"
        );
        Directory.CreateDirectory(outputDir);

        string csvPath = Path.Combine(outputDir, $"PresentMon_{procNameWithoutExt}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = binaryPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // Use safe ArgumentList instead of string concatenation to prevent argument injection
            psi.ArgumentList.Add("-process_name");
            psi.ArgumentList.Add(safeExeName);
            psi.ArgumentList.Add("-timed");
            psi.ArgumentList.Add(clampedSeconds.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-output_file");
            psi.ArgumentList.Add(csvPath);
            psi.ArgumentList.Add("-terminate_after_timed");
            psi.ArgumentList.Add("-no_top");

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return new PresentMonCaptureResponse(
                    false, safeExeName, version, null, -1, null, null, null,
                    "Não foi possível iniciar o processo de captura do PresentMon.",
                    timestamp
                );
            }

            // Timeout protection: duration + 10s grace period
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(clampedSeconds + 10));

            Task<string> stdOutTask = proc.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            Task<string> stdErrTask = proc.StandardError.ReadToEndAsync(timeoutCts.Token);

            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
                string cancelReason = cancellationToken.IsCancellationRequested
                    ? "Captura cancelada pelo usuário."
                    : "Tempo limite excedido aguardando encerramento do PresentMon.";

                return new PresentMonCaptureResponse(
                    false, safeExeName, version, csvPath, -1, null, null, null,
                    cancelReason,
                    timestamp
                );
            }

            string stdOut = await stdOutTask;
            string stdErr = await stdErrTask;
            int exitCode = proc.ExitCode;

            if (exitCode != 0)
            {
                return new PresentMonCaptureResponse(
                    false, safeExeName, version, csvPath, exitCode, stdOut, stdErr, null,
                    $"PresentMon encerrou com código de saída {exitCode}. {stdErr}",
                    timestamp
                );
            }

            if (!File.Exists(csvPath) || new FileInfo(csvPath).Length == 0)
            {
                return new PresentMonCaptureResponse(
                    false, safeExeName, version, csvPath, exitCode, stdOut, stdErr, null,
                    "O arquivo CSV de captura não foi gerado ou está vazio.",
                    timestamp
                );
            }

            var summary = ParseCsvFile(csvPath);
            if (summary.TotalFramesCount == 0)
            {
                return new PresentMonCaptureResponse(
                    false, safeExeName, version, csvPath, exitCode, stdOut, stdErr, summary,
                    "Dados insuficientes: nenhum quadro renderizado foi capturado durante a sessão.",
                    timestamp
                );
            }

            var successResponse = new PresentMonCaptureResponse(
                true, safeExeName, version, csvPath, exitCode, stdOut, stdErr, summary, null, timestamp
            );

            _sessionHistory.Enqueue(successResponse);
            while (_sessionHistory.Count > 50)
            {
                _sessionHistory.TryDequeue(out _);
            }

            return successResponse;
        }
        catch (Exception ex)
        {
            return new PresentMonCaptureResponse(
                false, safeExeName, version, csvPath, -1, null, null, null,
                $"Erro durante captura PresentMon: {ex.Message}",
                timestamp
            );
        }
    }

    public static PresentMonSummary ParseCsvFile(string csvFilePath)
    {
        if (string.IsNullOrWhiteSpace(csvFilePath) || !File.Exists(csvFilePath))
            return new PresentMonSummary(0, 0, 0, 0, 0, 0, Array.Empty<double>());

        using var reader = new StreamReader(csvFilePath);
        string? headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
            return new PresentMonSummary(0, 0, 0, 0, 0, 0, Array.Empty<double>());

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
            return new PresentMonSummary(0, 0, 0, 0, 0, 0, Array.Empty<double>());

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
            return new PresentMonSummary(0, 0, 0, 0, 0, 0, Array.Empty<double>());

        double avgFt = frametimesMs.Average();
        double avgFps = avgFt > 0 ? (1000.0 / avgFt) : 0;

        var sortedDescending = frametimesMs.OrderByDescending(f => f).ToList();

        int onePctIndex = Math.Min(sortedDescending.Count - 1, Math.Max(0, (int)(sortedDescending.Count * 0.01)));
        double onePctFt = sortedDescending[onePctIndex];
        double onePctLowFps = onePctFt > 0 ? (1000.0 / onePctFt) : 0;

        double maxFt = sortedDescending.First();
        double minFps = maxFt > 0 ? (1000.0 / maxFt) : 0;

        double variance = frametimesMs.Select(v => Math.Pow(v - avgFt, 2)).Sum() / frametimesMs.Count;
        double stDev = Math.Sqrt(variance);

        var recentSample = frametimesMs.TakeLast(120).Select(v => Math.Round(v, 2)).ToList();

        return new PresentMonSummary(
            AverageFps: Math.Round(avgFps, 1),
            MinFps: Math.Round(minFps, 1),
            OnePercentLowFps: Math.Round(onePctLowFps, 1),
            AverageFrametimeMs: Math.Round(avgFt, 2),
            FrametimeStandardDeviationMs: Math.Round(stDev, 2),
            TotalFramesCount: frametimesMs.Count,
            RecentFrameTimesMs: recentSample
        );
    }
}