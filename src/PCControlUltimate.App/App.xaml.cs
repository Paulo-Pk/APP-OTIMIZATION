using System;
using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PCControlUltimate.App.Services;
using PCControlUltimate.App.ViewModels;
using PCControlUltimate.App.Views;
using PCControlUltimate.Core.Interfaces;
using PCControlUltimate.Hardware;
using PCControlUltimate.Infrastructure.Services;
using PCControlUltimate.Windows.Services;
using Serilog;

namespace PCControlUltimate.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private PrivilegedAgentServer? _agentServer;
    private IHardwareMonitorService? _hardwareMonitor;
    private AgentClientService? _agentClient;
    private Mutex? _agentMutex;
    private bool _ownsAgentMutex;

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
            _hardwareMonitor = new LibreHardwareMonitorWrapper();
            await _hardwareMonitor.InitializeAsync();

            // Single-instance check for the local Privileged Agent
            _agentMutex = new Mutex(true, @"Local\PCControlUltimate_PrivilegedAgent_Mutex_v25", out _ownsAgentMutex);

            string? inMemoryToken = null;
            if (_ownsAgentMutex)
            {
                try
                {
                    _agentServer = new PrivilegedAgentServer(_hardwareMonitor);
                    _agentServer.Start();
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

            _agentClient = new AgentClientService(inMemoryToken);

            var services = new ServiceCollection();
            services.AddSingleton<LocalizationService>();
            services.AddSingleton<IHardwareMonitorService>(_hardwareMonitor);
            services.AddSingleton<AgentClientService>(_agentClient);
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<MainViewModel>().Navigation);

            // Register Module ViewModels
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<HardwareMonitorViewModel>();
            services.AddTransient<RegistryOptimizerViewModel>();
            services.AddTransient<RestoreCenterViewModel>();
            services.AddTransient<DebloatCleanerViewModel>();
            services.AddTransient<NetworkLabViewModel>();
            services.AddTransient<GamingPresentMonViewModel>();
            services.AddTransient<DiagnosticsViewModel>();
            services.AddTransient<DriverCenterViewModel>();

            // Register Module UserControl Views
            services.AddTransient<DashboardView>();
            services.AddTransient<HardwareMonitorView>();
            services.AddTransient<RegistryOptimizerView>();
            services.AddTransient<RestoreCenterView>();
            services.AddTransient<DebloatCleanerView>();
            services.AddTransient<NetworkLabView>();
            services.AddTransient<GamingPresentMonView>();
            services.AddTransient<DiagnosticsView>();
            services.AddTransient<DriverCenterView>();

            services.AddSingleton<MainWindow>();

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