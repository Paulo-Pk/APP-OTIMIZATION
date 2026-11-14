using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PCControlUltimate.App.Services;
using PCControlUltimate.App.ViewModels;
using PCControlUltimate.App.Views;
using PCControlUltimate.Core.Interfaces;
using PCControlUltimate.Gaming.Network;
using PCControlUltimate.Hardware;
using PCControlUltimate.Infrastructure.Data;
using PCControlUltimate.Infrastructure.Services;
using PCControlUltimate.Security;
using PCControlUltimate.Windows.Services;
using Serilog;

namespace PCControlUltimate.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private PrivilegedAgentServer? _agentServer;
    private IHardwareMonitorService? _hardwareMonitor;
    private AgentClientService? _agentClient;
    private DatabaseService? _databaseService;
    private Mutex? _agentMutex;
    private bool _ownsAgentMutex;
    private bool _ownsAgentServer;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PCControlUltimate",
            "Logs"
        );
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(logDir, "wpf_app.log"), rollingInterval: RollingInterval.Day)
            .CreateLogger();

        Log.Information("PC Control Ultimate v2.5.0 starting up...");

        try
        {
            _databaseService = new DatabaseService();
            await _databaseService.InitializeAndVerifyAsync();

            _hardwareMonitor = new LibreHardwareMonitorWrapper();
            await _hardwareMonitor.InitializeAsync();

            if (_hardwareMonitor.IsAvailable)
            {
                var startupSnapshot = await _hardwareMonitor.CaptureSnapshotAsync();
                string cpuTemperatureSensors = string.Join(" | ", startupSnapshot.Sensors
                    .Where(sensor =>
                        string.Equals(sensor.HardwareType, "Cpu", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(sensor.SensorType, "Temperature", StringComparison.OrdinalIgnoreCase))
                    .Take(20)
                    .Select(sensor => $"{sensor.Name}: {sensor.FormattedValue}"));

                Log.Information(
                    "Hardware monitor ready. Status={Status}; CPU={CpuModel}; CPU temperature={CpuTemperature}; CPU temperature sensors={CpuSensors}; Diagnostic={Diagnostic}",
                    startupSnapshot.Status,
                    startupSnapshot.CpuModelName ?? "N/D",
                    startupSnapshot.CpuPackageTemp?.ToString("F1") ?? "N/D",
                    string.IsNullOrWhiteSpace(cpuTemperatureSensors) ? "Nenhum sensor térmico de CPU exposto" : cpuTemperatureSensors,
                    startupSnapshot.DiagnosticReason
                );
            }
            else
            {
                Log.Warning(
                    "Hardware monitor unavailable. Status={Status}; Error={Error}",
                    _hardwareMonitor.CurrentStatus,
                    _hardwareMonitor.LastCommunicationError
                );
            }

            // Single-instance check for the local Privileged Agent
            _agentMutex = new Mutex(true, @"Local\PCControlUltimate_PrivilegedAgent_Mutex_v25", out _ownsAgentMutex);

            var networkDiagnosticsService = new NetworkDiagnosticsService();
            string? inMemoryToken = null;
            if (_ownsAgentMutex)
            {
                try
                {
                    _agentServer = new PrivilegedAgentServer(_hardwareMonitor, PrivilegedAgentServer.DefaultPort, networkDiagnosticsService);
                    _agentServer.Start();
                    _ownsAgentServer = true;
                    inMemoryToken = _agentServer.SessionToken;
                    Log.Information("PrivilegedAgentServer started on 127.0.0.1:{Port}", PrivilegedAgentServer.DefaultPort);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not bind PrivilegedAgentServer port; checking existing instance.");
                }
            }
            else
            {
                Log.Information("Existing PrivilegedAgentServer instance detected via Mutex; connecting via DPAPI token.");
            }

            // A secondary UI instance may reuse the existing agent's DPAPI-protected
            // session token. It must not issue native picker approvals because the
            // in-memory approval store belongs to the process that owns the server.
            if (string.IsNullOrWhiteSpace(inMemoryToken))
            {
                inMemoryToken = IntegrityVerifier.ReadProtectedSessionToken(PrivilegedAgentServer.ProtectedTokenFilePath);
            }

            _agentClient = new AgentClientService(inMemoryToken);

            var services = new ServiceCollection();
            services.AddSingleton(inMemoryToken ?? string.Empty);
            services.AddSingleton<LocalizationService>();
            services.AddSingleton<DatabaseService>(_databaseService);
            services.AddTransient<AppDbContext>(sp => sp.GetRequiredService<DatabaseService>().CreateDbContext());
            services.AddSingleton<IHardwareMonitorService>(_hardwareMonitor);
            services.AddSingleton<NetworkDiagnosticsService>(networkDiagnosticsService);
            services.AddSingleton<UniversalHardwareEngine>();
            services.AddSingleton<WindowsCompatibilityEngine>();
            services.AddSingleton<WingetSoftwareCenterService>();
            services.AddSingleton<PCControlUltimate.Diagnostics.SystemDoctorService>();
            services.AddSingleton<PCControlUltimate.Diagnostics.RecommendationEngine>();
            services.AddSingleton<AgentClientService>(_agentClient);
            services.AddSingleton<GameNetworkAcceleratorEngine>();
            services.AddSingleton<IAccelerationProvider>(sp => sp.GetRequiredService<GameNetworkAcceleratorEngine>());
            services.AddSingleton<IGameNetworkDetector>(sp => sp.GetRequiredService<GameNetworkAcceleratorEngine>());
            services.AddSingleton<IRouteProbe>(sp => sp.GetRequiredService<GameNetworkAcceleratorEngine>());
            services.AddSingleton<INetworkMeasurementService>(sp => sp.GetRequiredService<GameNetworkAcceleratorEngine>());
            services.AddSingleton<INodeHealthService>(sp => sp.GetRequiredService<GameNetworkAcceleratorEngine>());
            services.AddSingleton<ITunnelProvider>(sp => sp.GetRequiredService<GameNetworkAcceleratorEngine>());
            services.AddSingleton<ITrafficRoutingProvider>(sp => sp.GetRequiredService<GameNetworkAcceleratorEngine>());
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<MainViewModel>().Navigation);

            // Register all 17 Module ViewModels
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<HardwareMonitorViewModel>();
            services.AddTransient<GpuHealthLabViewModel>();
            services.AddTransient<BoostCenterViewModel>();
            services.AddTransient<GameCenterViewModel>();
            services.AddTransient<GamingPresentMonViewModel>();
            services.AddTransient<BenchmarkCenterViewModel>();
            services.AddTransient<DiagnosticsViewModel>();
            services.AddTransient<RegistryOptimizerViewModel>();
            services.AddTransient<DebloatCleanerViewModel>();
            services.AddTransient<NetworkLabViewModel>();
            services.AddTransient<GameNetworkAcceleratorViewModel>();
            services.AddTransient<PeripheralTestViewModel>();
            services.AddTransient<DriverCenterViewModel>();
            services.AddTransient<SoftwareHubViewModel>();
            services.AddTransient<RestoreCenterViewModel>();
            services.AddTransient<AiAssistantViewModel>();

            // Register all 17 Module UserControl Views
            services.AddTransient<DashboardView>();
            services.AddTransient<HardwareMonitorView>();
            services.AddTransient<GpuHealthLabView>();
            services.AddTransient<BoostCenterView>();
            services.AddTransient<GameCenterView>();
            services.AddTransient<GamingPresentMonView>();
            services.AddTransient<BenchmarkCenterView>();
            services.AddTransient<DiagnosticsView>();
            services.AddTransient<RegistryOptimizerView>();
            services.AddTransient<DebloatCleanerView>();
            services.AddTransient<NetworkLabView>();
            services.AddTransient<GameNetworkAcceleratorView>();
            services.AddTransient<PeripheralTestView>();
            services.AddTransient<DriverCenterView>();
            services.AddTransient<SoftwareHubView>();
            services.AddTransient<RestoreCenterView>();
            services.AddTransient<AiAssistantView>();

            services.AddSingleton<MainWindow>(_ => new MainWindow(inMemoryToken ?? string.Empty, _ownsAgentServer));

            _serviceProvider = services.BuildServiceProvider();

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Critical error during application startup.");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("PC Control Ultimate shutting down cleanly...");

        try
        {
            _hardwareMonitor?.StopPolling();
            _agentServer?.Stop();
            _agentServer?.Dispose();
            _agentClient?.Dispose();
            _hardwareMonitor?.Dispose();
            _serviceProvider?.Dispose();

            if (_ownsAgentMutex && _agentMutex != null)
            {
                _agentMutex.ReleaseMutex();
            }
            _agentMutex?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while releasing application resources on exit.");
        }
        finally
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}