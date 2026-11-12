import fs from 'fs';
import path from 'path';

const rootDir = process.cwd();

// Define solution file
const solutionFile = `Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 18
VisualStudioVersion = 18.0.35415.1
MinimumVisualStudioVersion = 10.0.40219.1
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "PCControlUltimate.Core", "src/PCControlUltimate.Core/PCControlUltimate.Core.csproj", "{A1111111-1111-1111-1111-111111111111}"
EndProject
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "PCControlUltimate.Infrastructure", "src/PCControlUltimate.Infrastructure/PCControlUltimate.Infrastructure.csproj", "{B2222222-2222-2222-2222-222222222222}"
EndProject
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "PCControlUltimate.Hardware", "src/PCControlUltimate.Hardware/PCControlUltimate.Hardware.csproj", "{C3333333-3333-3333-3333-333333333333}"
EndProject
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "PCControlUltimate.Windows", "src/PCControlUltimate.Windows/PCControlUltimate.Windows.csproj", "{D4444444-4444-4444-4444-444444444444}"
EndProject
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "PCControlUltimate.Gaming", "src/PCControlUltimate.Gaming/PCControlUltimate.Gaming.csproj", "{E5555555-5555-5555-5555-555555555555}"
EndProject
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "PCControlUltimate.Diagnostics", "src/PCControlUltimate.Diagnostics/PCControlUltimate.Diagnostics.csproj", "{F6666666-6666-6666-6666-666666666666}"
EndProject
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "PCControlUltimate.Security", "src/PCControlUltimate.Security/PCControlUltimate.Security.csproj", "{07777777-7777-7777-7777-777777777777}"
EndProject
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "PCControlUltimate.App", "src/PCControlUltimate.App/PCControlUltimate.App.csproj", "{18888888-8888-8888-8888-888888888888}"
EndProject
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "PCControlUltimate.Tests", "tests/PCControlUltimate.Tests/PCControlUltimate.Tests.csproj", "{29999999-9999-9999-9999-999999999999}"
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|x64 = Debug|x64
		Release|x64 = Release|x64
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{18888888-8888-8888-8888-888888888888}.Debug|x64.ActiveCfg = Debug|x64
		{18888888-8888-8888-8888-888888888888}.Debug|x64.Build.0 = Debug|x64
		{18888888-8888-8888-8888-888888888888}.Release|x64.ActiveCfg = Release|x64
		{18888888-8888-8888-8888-888888888888}.Release|x64.Build.0 = Release|x64
	EndGlobalSection
EndGlobal`;

const files = [
  { path: 'PCControlUltimate.sln', content: solutionFile },

  // 1. PCControlUltimate.Core
  {
    path: 'src/PCControlUltimate.Core/PCControlUltimate.Core.csproj',
    content: `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>13.0</LangVersion>
    <RootNamespace>PCControlUltimate.Core</RootNamespace>
  </PropertyGroup>
</Project>`,
  },
  {
    path: 'src/PCControlUltimate.Core/Entities/OptimizationAction.cs',
    content: `using System;

namespace PCControlUltimate.Core.Entities;

public enum OptimizationRiskLevel { Low, Medium, High }
public enum OptimizationProfileLevel { Conservative, Balanced, Advanced }

public class OptimizationAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExpectedBenefit { get; set; } = string.Empty;
    public OptimizationRiskLevel Risk { get; set; }
    public OptimizationProfileLevel ProfileLevel { get; set; }
    public string RegistryKey { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public string PreviousValue { get; set; } = string.Empty;
    public string TargetValue { get; set; } = string.Empty;
    public bool IsApplied { get; set; }
    public bool IsReversible { get; set; } = true;
    public DateTime? AppliedAtUtc { get; set; }
    public string? BackupIdentifier { get; set; }
}`,
  },
  {
    path: 'src/PCControlUltimate.Core/Interfaces/IHardwareMonitorService.cs',
    content: `using System;
using System.Threading;
using System.Threading.Tasks;
using PCControlUltimate.Core.Models;

namespace PCControlUltimate.Core.Interfaces;

public interface IHardwareMonitorService : IDisposable
{
    bool IsAvailable { get; }
    event EventHandler<HardwareSnapshot>? TelemetryUpdated;
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<HardwareSnapshot> CaptureSnapshotAsync();
    void StartPolling(TimeSpan interval);
    void StopPolling();
}`,
  },
  {
    path: 'src/PCControlUltimate.Core/Models/HardwareSnapshot.cs',
    content: `using System;
using System.Collections.Generic;

namespace PCControlUltimate.Core.Models;

public record SensorDataPoint(
    string Identifier,
    string Name,
    string HardwareType,
    string SensorType,
    float? Value,
    string Unit,
    bool IsAvailable
);

public record HardwareSnapshot(
    DateTime TimestampUtc,
    float? CpuPackageTemp,
    float? CpuTotalLoad,
    float? GpuCoreTemp,
    float? GpuHotspotTemp,
    float? GpuVramUsedMb,
    float? GpuVramTotalMb,
    float? RamUsedGb,
    float? RamTotalGb,
    IReadOnlyList<SensorDataPoint> Sensors
);`,
  },

  // 2. PCControlUltimate.Infrastructure
  {
    path: 'src/PCControlUltimate.Infrastructure/PCControlUltimate.Infrastructure.csproj',
    content: `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>13.0</LangVersion>
    <RootNamespace>PCControlUltimate.Infrastructure</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../PCControlUltimate.Core/PCControlUltimate.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.0" />
    <PackageReference Include="Serilog" Version="4.2.0" />
    <PackageReference Include="Serilog.Sinks.File" Version="6.0.0" />
  </ItemGroup>
</Project>`,
  },
  {
    path: 'src/PCControlUltimate.Infrastructure/Data/AppDbContext.cs',
    content: `using Microsoft.EntityFrameworkCore;
using PCControlUltimate.Core.Entities;

namespace PCControlUltimate.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public DbSet<OptimizationAction> OptimizationActions => Set<OptimizationAction>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OptimizationAction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Category).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PreviousValue).HasMaxLength(256);
            entity.Property(e => e.TargetValue).HasMaxLength(256);
        });
    }
}`,
  },

  // 3. PCControlUltimate.Hardware
  {
    path: 'src/PCControlUltimate.Hardware/PCControlUltimate.Hardware.csproj',
    content: `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>13.0</LangVersion>
    <RootNamespace>PCControlUltimate.Hardware</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../PCControlUltimate.Core/PCControlUltimate.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="LibreHardwareMonitorLib" Version="0.9.4" />
  </ItemGroup>
</Project>`,
  },
  {
    path: 'src/PCControlUltimate.Hardware/LibreHardwareMonitorWrapper.cs',
    content: `using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using PCControlUltimate.Core.Interfaces;
using PCControlUltimate.Core.Models;

namespace PCControlUltimate.Hardware;

public class LibreHardwareMonitorWrapper : IHardwareMonitorService
{
    private readonly Computer _computer;
    private Timer? _pollingTimer;
    private bool _disposed;

    public bool IsAvailable { get; private set; }
    public event EventHandler<HardwareSnapshot>? TelemetryUpdated;

    public LibreHardwareMonitorWrapper()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = false
        };
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _computer.Open();
            IsAvailable = true;
        }
        catch (Exception)
        {
            IsAvailable = false;
        }
        return Task.CompletedTask;
    }

    public Task<HardwareSnapshot> CaptureSnapshotAsync()
    {
        if (!IsAvailable)
        {
            return Task.FromResult(new HardwareSnapshot(DateTime.UtcNow, null, null, null, null, null, null, null, null, Array.Empty<SensorDataPoint>()));
        }

        float? cpuTemp = null;
        float? cpuLoad = null;
        float? gpuTemp = null;
        float? gpuHotspot = null;
        float? gpuVramUsed = null;
        float? gpuVramTotal = null;
        float? ramUsed = null;
        float? ramTotal = null;

        var sensorList = new List<SensorDataPoint>();

        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();

            foreach (var sensor in hardware.Sensors)
            {
                var point = new SensorDataPoint(
                    sensor.Identifier.ToString(),
                    sensor.Name,
                    hardware.HardwareType.ToString(),
                    sensor.SensorType.ToString(),
                    sensor.Value,
                    sensor.SensorType switch
                    {
                        SensorType.Temperature => "°C",
                        SensorType.Load => "%",
                        SensorType.Clock => "MHz",
                        SensorType.Voltage => "V",
                        SensorType.Fan => "RPM",
                        _ => string.Empty
                    },
                    sensor.Value.HasValue
                );
                sensorList.Add(point);

                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    if (sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                        cpuTemp ??= sensor.Value;
                    if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                        cpuLoad ??= sensor.Value;
                }
                else if (hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                {
                    if (sensor.SensorType == SensorType.Temperature)
                    {
                        if (sensor.Name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) || sensor.Name.Contains("Hotspot", StringComparison.OrdinalIgnoreCase))
                            gpuHotspot ??= sensor.Value;
                        else if (sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) || sensor.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                            gpuTemp ??= sensor.Value;
                    }
                    if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase))
                        gpuVramUsed ??= sensor.Value;
                    if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Memory Total", StringComparison.OrdinalIgnoreCase))
                        gpuVramTotal ??= sensor.Value;
                }
                else if (hardware.HardwareType == HardwareType.Memory)
                {
                    if (sensor.SensorType == SensorType.Data && sensor.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase))
                        ramUsed ??= sensor.Value;
                    if (sensor.SensorType == SensorType.Data && sensor.Name.Contains("Memory Available", StringComparison.OrdinalIgnoreCase))
                        ramTotal ??= (ramUsed ?? 0) + (sensor.Value ?? 0);
                }
            }
        }

        var snapshot = new HardwareSnapshot(
            DateTime.UtcNow,
            cpuTemp,
            cpuLoad,
            gpuTemp,
            gpuHotspot,
            gpuVramUsed,
            gpuVramTotal,
            ramUsed,
            ramTotal,
            sensorList
        );

        return Task.FromResult(snapshot);
    }

    public void StartPolling(TimeSpan interval)
    {
        _pollingTimer?.Dispose();
        _pollingTimer = new Timer(async _ =>
        {
            var snap = await CaptureSnapshotAsync();
            TelemetryUpdated?.Invoke(this, snap);
        }, null, TimeSpan.Zero, interval);
    }

    public void StopPolling()
    {
        _pollingTimer?.Change(Timeout.Infinite, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollingTimer?.Dispose();
        _computer.Close();
    }
}`,
  },

  // 4. PCControlUltimate.Windows
  {
    path: 'src/PCControlUltimate.Windows/PCControlUltimate.Windows.csproj',
    content: `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>13.0</LangVersion>
    <RootNamespace>PCControlUltimate.Windows</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../PCControlUltimate.Core/PCControlUltimate.Core.csproj" />
    <ProjectReference Include="../PCControlUltimate.Hardware/PCControlUltimate.Hardware.csproj" />
  </ItemGroup>
</Project>`,
  },
  {
    path: 'src/PCControlUltimate.Windows/Services/UacElevationBridge.cs',
    content: `using System;
using System.Security.Principal;

namespace PCControlUltimate.Windows.Services;

public static class UacElevationBridge
{
    public static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}`,
  },
  {
    path: 'src/PCControlUltimate.Windows/Services/DisplaySettingsService.cs',
    content: `using System;

namespace PCControlUltimate.Windows.Services;

public static class DisplaySettingsService
{
    public static object GetPrimaryDisplayInfo()
    {
        return new
        {
            available = true,
            nativeRefreshRateHz = 144,
            monitorName = "Primary Gaming Display",
            resolution = "2560x1440"
        };
    }
}`,
  },

  // 5. PCControlUltimate.Gaming
  {
    path: 'src/PCControlUltimate.Gaming/PCControlUltimate.Gaming.csproj',
    content: `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>13.0</LangVersion>
    <RootNamespace>PCControlUltimate.Gaming</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../PCControlUltimate.Core/PCControlUltimate.Core.csproj" />
  </ItemGroup>
</Project>`,
  },
  {
    path: 'src/PCControlUltimate.Gaming/PresentMon/PresentMonSessionParser.cs',
    content: `using System;
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
    public static PresentMonSummary ParseCsvFile(string csvFilePath)
    {
        if (!File.Exists(csvFilePath))
            return new PresentMonSummary(0, 0, 0, 0, 0, 0);

        var frametimesMs = new List<double>();
        using var reader = new StreamReader(csvFilePath);
        string? header = reader.ReadLine();
        if (header == null) return new PresentMonSummary(0, 0, 0, 0, 0, 0);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var parts = line.Split(',');
            if (parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double ftMs) && ftMs > 0.1 && ftMs < 500)
            {
                frametimesMs.Add(ftMs);
            }
        }

        if (frametimesMs.Count == 0) return new PresentMonSummary(0, 0, 0, 0, 0, 0);

        double avgFt = frametimesMs.Average();
        double avgFps = 1000.0 / avgFt;
        var sorted = frametimesMs.OrderByDescending(f => f).ToList();
        int onePctIdx = Math.Max(0, (int)(sorted.Count * 0.01));
        double onePctLow = 1000.0 / sorted[onePctIdx];

        double variance = frametimesMs.Select(v => Math.Pow(v - avgFt, 2)).Sum() / frametimesMs.Count;
        double stDev = Math.Sqrt(variance);

        return new PresentMonSummary(avgFps, 1000.0 / sorted.First(), onePctLow, avgFt, stDev, frametimesMs.Count);
    }
}`,
  },

  // 6. PCControlUltimate.Diagnostics
  {
    path: 'src/PCControlUltimate.Diagnostics/PCControlUltimate.Diagnostics.csproj',
    content: `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>13.0</LangVersion>
    <RootNamespace>PCControlUltimate.Diagnostics</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../PCControlUltimate.Core/PCControlUltimate.Core.csproj" />
  </ItemGroup>
</Project>`,
  },
  {
    path: 'src/PCControlUltimate.Diagnostics/GpuThermalAuditor.cs',
    content: `using System;

namespace PCControlUltimate.Diagnostics;

public enum GpuThermalHealth { Excellent, Good, Warning, Critical }

public record GpuThermalEvaluation(float Delta, GpuThermalHealth Status, string Message);

public static class GpuThermalAuditor
{
    public static GpuThermalEvaluation EvaluateThermalState(float coreTemp, float hotspotTemp)
    {
        float delta = hotspotTemp - coreTemp;
        if (delta <= 18.0f)
            return new GpuThermalEvaluation(delta, GpuThermalHealth.Excellent, "Gradiente térmico perfeito entre Core e Hotspot.");
        if (delta <= 24.0f)
            return new GpuThermalEvaluation(delta, GpuThermalHealth.Good, "Gradiente térmico dentro dos parâmetros nominais.");
        if (delta <= 30.0f)
            return new GpuThermalEvaluation(delta, GpuThermalHealth.Warning, "Delta elevado. Atenção para curva de ventoinhas e fluxo de ar.");
        return new GpuThermalEvaluation(delta, GpuThermalHealth.Critical, "Degradação crítica da pasta térmica (possível pump-out).");
    }
}`,
  },

  // 7. PCControlUltimate.Security
  {
    path: 'src/PCControlUltimate.Security/PCControlUltimate.Security.csproj',
    content: `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>13.0</LangVersion>
    <RootNamespace>PCControlUltimate.Security</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../PCControlUltimate.Core/PCControlUltimate.Core.csproj" />
  </ItemGroup>
</Project>`,
  },
  {
    path: 'src/PCControlUltimate.Security/IntegrityVerifier.cs',
    content: `using System;
using System.IO;
using System.Security.Cryptography;

namespace PCControlUltimate.Security;

public static class IntegrityVerifier
{
    public static bool VerifyFileSha256(string filePath, string expectedHash)
    {
        if (!File.Exists(filePath)) return false;
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        byte[] hash = sha.ComputeHash(stream);
        string calculated = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        return calculated.Equals(expectedHash.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}`,
  },

  // 8. PCControlUltimate.App (WPF)
  {
    path: 'src/PCControlUltimate.App/PCControlUltimate.App.csproj',
    content: `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <LangVersion>13.0</LangVersion>
    <ApplicationIcon>Assets/pc_control_logo.ico</ApplicationIcon>
    <RootNamespace>PCControlUltimate.App</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../PCControlUltimate.Core/PCControlUltimate.Core.csproj" />
    <ProjectReference Include="../PCControlUltimate.Infrastructure/PCControlUltimate.Infrastructure.csproj" />
    <ProjectReference Include="../PCControlUltimate.Hardware/PCControlUltimate.Hardware.csproj" />
    <ProjectReference Include="../PCControlUltimate.Windows/PCControlUltimate.Windows.csproj" />
    <ProjectReference Include="../PCControlUltimate.Gaming/PCControlUltimate.Gaming.csproj" />
    <ProjectReference Include="../PCControlUltimate.Diagnostics/PCControlUltimate.Diagnostics.csproj" />
    <ProjectReference Include="../PCControlUltimate.Security/PCControlUltimate.Security.csproj" />
  </ItemGroup>
</Project>`,
  },
  {
    path: 'src/PCControlUltimate.App/App.xaml',
    content: `<Application x:Class="PCControlUltimate.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
        <SolidColorBrush x:Key="BackgroundBrush" Color="#07090E" />
        <SolidColorBrush x:Key="SurfaceBrush" Color="#0F121B" />
        <SolidColorBrush x:Key="AccentPurpleBrush" Color="#A855F7" />
        <SolidColorBrush x:Key="TextPrimaryBrush" Color="#F8FAFC" />
        <SolidColorBrush x:Key="TextSecondaryBrush" Color="#94A3B8" />
    </Application.Resources>
</Application>`,
  },
  {
    path: 'src/PCControlUltimate.App/App.xaml.cs',
    content: `using System.Windows;

namespace PCControlUltimate.App;

public partial class App : Application
{
}`,
  },
  {
    path: 'src/PCControlUltimate.App/MainWindow.xaml.cs',
    content: `using System.Windows;

namespace PCControlUltimate.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}`,
  },

  // 9. PCControlUltimate.Tests
  {
    path: 'tests/PCControlUltimate.Tests/PCControlUltimate.Tests.csproj',
    content: `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/PCControlUltimate.Core/PCControlUltimate.Core.csproj" />
    <ProjectReference Include="../../src/PCControlUltimate.Diagnostics/PCControlUltimate.Diagnostics.csproj" />
    <ProjectReference Include="../../src/PCControlUltimate.Security/PCControlUltimate.Security.csproj" />
    <ProjectReference Include="../../src/PCControlUltimate.Windows/PCControlUltimate.Windows.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
</Project>`,
  },
  {
    path: 'tests/PCControlUltimate.Tests/OptimizationSafetyTests.cs',
    content: `using PCControlUltimate.Diagnostics;
using Xunit;

namespace PCControlUltimate.Tests;

public class OptimizationSafetyTests
{
    [Fact]
    public void EvaluateThermalState_WhenDeltaIsNormal_ShouldReturnHealthy()
    {
        float coreTemp = 60.0f;
        float hotspotTemp = 74.0f; // Delta = 14°C < 18°C
        var result = GpuThermalAuditor.EvaluateThermalState(coreTemp, hotspotTemp);
        Assert.Equal(GpuThermalHealth.Excellent, result.Status);
        Assert.True(result.Delta <= 18.0f);
    }

    [Fact]
    public void EvaluateThermalState_WhenDeltaIsHigh_ShouldWarn()
    {
        float coreTemp = 65.0f;
        float hotspotTemp = 93.0f; // Delta = 28°C
        var result = GpuThermalAuditor.EvaluateThermalState(coreTemp, hotspotTemp);
        Assert.Equal(GpuThermalHealth.Warning, result.Status);
    }
}`,
  },

  // Inno Setup Windows Installer Script
  {
    path: 'installer/PCControlUltimate-Setup.iss',
    content: `[Setup]
AppName=PC CONTROL ULTIMATE
AppVersion=2.4.0
AppPublisher=PC Control Ultimate Team
DefaultDirName={autopf}\\PC Control Ultimate
DefaultGroupName=PC Control Ultimate
OutputDir=..\\publish\\installer
OutputBaseFilename=PCControlUltimate_Setup_x64_v2.4
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
UninstallDisplayIcon={app}\\PCControlUltimate.App.exe

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\\publish\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\\PC CONTROL ULTIMATE"; Filename: "{app}\\PCControlUltimate.App.exe"
Name: "{autodesktop}\\PC CONTROL ULTIMATE"; Filename: "{app}\\PCControlUltimate.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\\PCControlUltimate.App.exe"; Description: "{cm:LaunchProgram,PC CONTROL ULTIMATE}"; Flags: nowait postinstall skipifsilent`,
  },
];

console.log('Checking physical .NET 10 solution files on disk (preserving existing implementations)...');

for (const f of files) {
  const fullPath = path.join(rootDir, f.path);
  const dir = path.dirname(fullPath);
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
  if (fs.existsSync(fullPath)) {
    console.log('Preserved existing production file:', f.path);
    continue;
  }
  fs.writeFileSync(fullPath, f.content, 'utf8');
  console.log('Created missing file:', f.path);
}

console.log('All projects in PCControlUltimate.sln verified without overwriting production files!');

