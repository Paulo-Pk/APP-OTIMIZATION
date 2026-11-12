using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using PCControlUltimate.App.Services;
using PCControlUltimate.Core.Entities;
using PCControlUltimate.Core.Interfaces;
using PCControlUltimate.Core.Models;
using PCControlUltimate.Diagnostics;
using PCControlUltimate.Gaming.Network;
using PCControlUltimate.Gaming.PresentMon;
using PCControlUltimate.Infrastructure.Data;
using PCControlUltimate.Infrastructure.Services;
using PCControlUltimate.Windows.Services;

namespace PCControlUltimate.App.ViewModels;

public record ModuleCapabilityMatrixItem(
    int Index,
    string ModuleName,
    string ImplementationState,
    string BackendEngine,
    string PersistenceMechanism,
    string LimitationsAndSafetyNote
);

public record VerifiedSoftwareItem(
    string AppName,
    string Category,
    string WingetId,
    string Publisher,
    string CommandLine,
    string Description
);

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
}

public class TweakItemViewModel : INotifyPropertyChanged
{
    private bool _isApplied;
    private string _verificationState = "Não verificado";
    private string? _lastOperationId;

    public string TweakId { get; init; } = string.Empty;
    public string NamePt { get; init; } = string.Empty;
    public string NameEn { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string RegistryPath { get; init; } = string.Empty;
    public string ValueName { get; init; } = string.Empty;
    public string ValueKind { get; init; } = string.Empty;
    public string RecommendedValue { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = string.Empty;

    private string _displayName = string.Empty;
    public string Name
    {
        get => _displayName;
        set { _displayName = value; OnPropertyChanged(); }
    }

    public bool IsApplied
    {
        get => _isApplied;
        set { _isApplied = value; OnPropertyChanged(); }
    }

    public string VerificationState
    {
        get => _verificationState;
        set { _verificationState = value; OnPropertyChanged(); }
    }

    public string? LastOperationId
    {
        get => _lastOperationId;
        set { _lastOperationId = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class CleanerCategoryViewModel : INotifyPropertyChanged
{
    private string _dryRunEstimateText = "Execute Dry-Run para estimar";
    private string _realFreedText = "Nenhum arquivo removido nesta sessão";

    public string CategoryKey { get; init; } = string.Empty;
    public string NamePt { get; init; } = string.Empty;
    public string NameEn { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public bool RequiresAdminUac { get; init; }

    private string _displayName = string.Empty;
    public string DisplayName
    {
        get => _displayName;
        set { _displayName = value; OnPropertyChanged(); }
    }

    public string DryRunEstimateText
    {
        get => _dryRunEstimateText;
        set { _dryRunEstimateText = value; OnPropertyChanged(); }
    }

    public string RealFreedText
    {
        get => _realFreedText;
        set { _realFreedText = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class MainViewModel : INotifyPropertyChanged
{
    private readonly AgentClientService _agentClient;
    private readonly IHardwareMonitorService? _hardwareMonitor;
    private readonly DatabaseService _databaseService;
    private readonly NetworkDiagnosticsService _networkDiagnostics;
    private CancellationTokenSource? _presentMonCts;
    private readonly SemaphoreSlim _telemetryRefreshGate = new(1, 1);
    private readonly DispatcherTimer _telemetryRefreshTimer;

    public LocalizationService Loc { get; }
    public INavigationService Navigation { get; }
    public DatabaseService Database => _databaseService;

    private string _currentTab = "dashboard";
    private bool _isAgentConnected;
    private bool _isAdmin;
    private string _statusMessage = "Verificando comunicação com o Agente...";

    // Hardware & GPU Health properties
    private string _hardwareSubsystemStatusText = "Sensor indisponível (N/D)";
    private string _cpuModel = "Sensor indisponível (N/D)";
    private string _cpuTemp = "N/D";
    private string _cpuLoad = "N/D";
    private string _cpuClock = "N/D";
    private string _cpuPower = "N/D";
    private string _gpuModel = "Sensor indisponível (N/D)";
    private string _gpuTemp = "N/D";
    private string _gpuHotspot = "N/D";
    private string _gpuLoad = "N/D";
    private string _gpuClock = "N/D";
    private string _gpuPower = "N/D";
    private string _gpuFanRpm = "N/D";
    private string _gpuVram = "N/D";
    private string _gpuThermalDiagnosisText = "Sensor térmico indisponível (N/D)";
    private string _ramUsage = "N/D";
    private string _displayInfoText = "Não detectado";

    // Restore Center properties
    private string _lastBackupStatus = "Nenhum backup verificado na sessão";
    private SnapshotSummaryItem? _selectedBackup;
    private bool _confirmRestoreExecution;
    private bool _confirmIncompleteRestore;
    private bool _rollbackOnPartialFailure = true;
    private string _restoreOperationReport = "Selecione um snapshot para inspecionar ou restaurar.";
    private string _restoreUacStatusMessage = string.Empty;
    private int _restoreProgressPercent;
    private string _restoreProgressStatus = string.Empty;

    // Debloat properties
    private bool _confirmCleanerDeletion;
    private string _debloatSummaryText = "Simule (Dry-Run) antes de executar qualquer exclusão.";

    // Network Lab properties
    private string _networkSummaryText = "Clique em Executar Diagnóstico para medir ICMP, TCP e DNS.";
    private string _externalThroughputText = "Clique para medir throughput real contra Cloudflare CDN.";

    // Game Network Accelerator properties
    private string _acceleratorGameName = "Counter-Strike 2 (Valve)";
    private string _acceleratorTargetHost = "155.133.248.34";
    private int _acceleratorTargetPort = 27015;
    private string _routeVerdictText = "Execute a avaliação multi-amostra para comparar a Rota Direta com nós intermediários.";
    private string _wfpSelectiveRoutingNoticeText = GameNetworkAcceleratorService.GetSelectiveRoutingCapabilityReport().WfpLimitationExplanationPt;
    private string _wireGuardStatusText = "Verifique a instalação do WireGuard for Windows e audite a configuração AllowedIPs.";
    private string _wireGuardConfigAuditText = "Nenhuma configuração WireGuard validada nesta sessão.";

    // PresentMon properties
    private string _presentMonStatusText = "Verifique a instalação do PresentMon e selecione um processo.";
    private RunningProcessInfo? _selectedProcess;
    private string _manualProcessName = "cs2.exe";
    private int _captureDurationSeconds = 10;
    private bool _isCapturingPresentMon;
    private string _presentMonMetricsSummary = "Nenhuma sessão capturada.";

    // Diagnostics & Security properties
    private string _securityPostureText = "Verificando DPAPI, ACL NTFS, HMAC-SHA256, SQLite e privilégios UAC...";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TweakItemViewModel> CatalogTweaks { get; } = new();
    public ObservableCollection<SensorDataPoint> AllHardwareSensors { get; } = new();
    public ObservableCollection<SnapshotSummaryItem> ExistingBackups { get; } = new();
    public ObservableCollection<RegistryValueEntry> SelectedBackupEntries { get; } = new();
    public ObservableCollection<RegistryReadFailure> SelectedBackupFailures { get; } = new();
    public ObservableCollection<string> RestoreItemResults { get; } = new();
    public ObservableCollection<CleanerCategoryViewModel> CleanerCategories { get; } = new();
    public ObservableCollection<string> CleanerAffectedFilesLog { get; } = new();
    public ObservableCollection<NetworkEndpointTestResult> NetworkEndpoints { get; } = new();
    public ObservableCollection<DnsQueryResult> DnsResults { get; } = new();
    public ObservableCollection<string> NetworkHistory { get; } = new();
    public ObservableCollection<DetectedGameConnection> DetectedGameSockets { get; } = new();
    public ObservableCollection<TracerouteHopResult> TracerouteHops { get; } = new();
    public ObservableCollection<RelayNodeCandidate> RelayNodes { get; } = new();
    public ObservableCollection<ModuleCapabilityMatrixItem> ModuleCapabilityMatrix { get; } = new();
    public ObservableCollection<RunningProcessInfo> AvailableProcesses { get; } = new();
    public ObservableCollection<double> RecentFrametimeSamples { get; } = new();
    public ObservableCollection<PresentMonCaptureResponse> PresentMonHistory { get; } = new();
    public ObservableCollection<RegistryAuditLogEntry> AuditLogs { get; } = new();
    public ObservableCollection<InstalledDriverEntry> InstalledDrivers { get; } = new();
    public ObservableCollection<VerifiedSoftwareItem> VerifiedWingetCatalog { get; } = new();
    public ObservableCollection<string> AssistantDiagnosticInsights { get; } = new();

    private double _cpuLoadPercent;
    private double _gpuLoadPercent;
    private double _ramUsagePercent;
    private string _gpuHotspotDeltaText = "N/D";
    private string _overallHealthScore = "N/D";
    private string _benchmarkResultSummary = "Clique em Executar Benchmark Determinístico para medir throughput real de CPU e Memória.";
    private string _peripheralCadenceSummary = "Inspecione a taxa nativa Win32 do monitor e meça a estabilidade de eventos de input.";

    // Preview Visual Parity & Extended Real Hardware Properties
    private string _detectedProfileDisplay = "Desktop";
    private string _cpuCoresThreadsText = $"{Environment.ProcessorCount} Threads";
    private string _ramTotalAndTypeText = "N/D";
    private string _ramUsedText = "N/D";
    private string _ramAvailableText = "N/D";
    private string _primaryDiskModel = "Disco Principal do Sistema";
    private string _primaryDiskSmartStatus = "Saudável (Online)";
    private string _primaryDiskFreeSpaceText = "N/D";
    private string _primaryDiskTotalSpaceText = "N/D";
    private double _primaryDiskUsagePercent;

    private readonly List<(double CpuLoad, double GpuLoad, double CpuTemp)> _telemetryHistory = new();
    private PointCollection _cpuLoadChartPoints = new();
    private PointCollection _gpuLoadChartPoints = new();
    private PointCollection _cpuTempChartPoints = new();
    private PointCollection _frametimeChartPoints = new();
    private bool _hasTelemetryHistory;
    private bool _hasFrametimeHistory;

    public string Language
    {
        get => Loc.CurrentCulture;
        set
        {
            Loc.CurrentCulture = value;
            UpdateLocalizedCollections();
            OnPropertyChanged();
            OnPropertyChanged(nameof(LanguageCodeDisplay));
            OnPropertyChanged(nameof(IsPortugueseActive));
            OnPropertyChanged(nameof(IsEnglishActive));
            OnPropertyChanged(nameof(AppliedTweaksBadgeText));
            OnPropertyChanged(nameof(HeaderTitle));
            OnPropertyChanged(nameof(ActiveModuleDisplayTitle));
        }
    }

    public string LanguageCodeDisplay => string.Equals(Loc.CurrentCulture, "pt-BR", StringComparison.OrdinalIgnoreCase) ? "PT-BR" : "EN-US";
    public bool IsPortugueseActive => string.Equals(Loc.CurrentCulture, "pt-BR", StringComparison.OrdinalIgnoreCase);
    public bool IsEnglishActive => !IsPortugueseActive;

    public int AppliedTweaksCount => CatalogTweaks.Count(t => t.IsApplied);
    public string AppliedTweaksBadgeText => IsPortugueseActive ? $"{AppliedTweaksCount} Ativos" : $"{AppliedTweaksCount} Active";
    public string DetectedProfileDisplay { get => _detectedProfileDisplay; set { _detectedProfileDisplay = value; OnPropertyChanged(); } }
    public string RamUsageWithPercent => RamUsagePercent > 0 ? $"{RamUsage} ({RamUsagePercent:F0}%)" : RamUsage;

    public string CpuCoresThreadsText { get => _cpuCoresThreadsText; set { _cpuCoresThreadsText = value; OnPropertyChanged(); } }
    public string RamTotalAndTypeText { get => _ramTotalAndTypeText; set { _ramTotalAndTypeText = value; OnPropertyChanged(); } }
    public string RamUsedText { get => _ramUsedText; set { _ramUsedText = value; OnPropertyChanged(); } }
    public string RamAvailableText { get => _ramAvailableText; set { _ramAvailableText = value; OnPropertyChanged(); } }
    public string PrimaryDiskModel { get => _primaryDiskModel; set { _primaryDiskModel = value; OnPropertyChanged(); } }
    public string PrimaryDiskSmartStatus { get => _primaryDiskSmartStatus; set { _primaryDiskSmartStatus = value; OnPropertyChanged(); } }
    public string PrimaryDiskFreeSpaceText { get => _primaryDiskFreeSpaceText; set { _primaryDiskFreeSpaceText = value; OnPropertyChanged(); } }
    public string PrimaryDiskTotalSpaceText { get => _primaryDiskTotalSpaceText; set { _primaryDiskTotalSpaceText = value; OnPropertyChanged(); } }
    public double PrimaryDiskUsagePercent { get => _primaryDiskUsagePercent; set { _primaryDiskUsagePercent = Math.Clamp(value, 0, 100); OnPropertyChanged(); } }

    public PointCollection CpuLoadChartPoints { get => _cpuLoadChartPoints; set { _cpuLoadChartPoints = value; OnPropertyChanged(); } }
    public PointCollection GpuLoadChartPoints { get => _gpuLoadChartPoints; set { _gpuLoadChartPoints = value; OnPropertyChanged(); } }
    public PointCollection CpuTempChartPoints { get => _cpuTempChartPoints; set { _cpuTempChartPoints = value; OnPropertyChanged(); } }
    public PointCollection FrametimeChartPoints { get => _frametimeChartPoints; set { _frametimeChartPoints = value; OnPropertyChanged(); } }
    public bool HasTelemetryHistory { get => _hasTelemetryHistory; set { _hasTelemetryHistory = value; OnPropertyChanged(); } }
    public bool HasFrametimeHistory { get => _hasFrametimeHistory; set { _hasFrametimeHistory = value; OnPropertyChanged(); } }

    public string HeaderTitle => Loc["HeaderSubtitle"];
    public string ActiveModuleDisplayTitle => Navigation.CurrentViewModel?.ModuleTitle ?? Loc["NavDashboard"];

    public string CurrentTab
    {
        get => _currentTab;
        set
        {
            if (!string.Equals(_currentTab, value, StringComparison.OrdinalIgnoreCase))
            {
                _currentTab = value;
                Navigation.NavigateTo(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveModuleDisplayTitle));
                OnPropertyChanged(nameof(IsDashboardVisible));
                OnPropertyChanged(nameof(IsHardwareVisible));
                OnPropertyChanged(nameof(IsGpuHealthVisible));
                OnPropertyChanged(nameof(IsBoostCenterVisible));
                OnPropertyChanged(nameof(IsGameCenterVisible));
                OnPropertyChanged(nameof(IsGamingVisible));
                OnPropertyChanged(nameof(IsBenchmarksVisible));
                OnPropertyChanged(nameof(IsDiagnosticsVisible));
                OnPropertyChanged(nameof(IsTweaksVisible));
                OnPropertyChanged(nameof(IsDebloatVisible));
                OnPropertyChanged(nameof(IsNetworkVisible));
                OnPropertyChanged(nameof(IsAcceleratorVisible));
                OnPropertyChanged(nameof(IsPeripheralsVisible));
                OnPropertyChanged(nameof(IsDriversVisible));
                OnPropertyChanged(nameof(IsSoftwareHubVisible));
                OnPropertyChanged(nameof(IsRestoreVisible));
                OnPropertyChanged(nameof(IsAiAssistantVisible));
            }
        }
    }

    public bool IsDashboardVisible => string.Equals(_currentTab, "dashboard", StringComparison.OrdinalIgnoreCase);
    public bool IsHardwareVisible => string.Equals(_currentTab, "hardware", StringComparison.OrdinalIgnoreCase);
    public bool IsGpuHealthVisible => string.Equals(_currentTab, "gpu-health", StringComparison.OrdinalIgnoreCase);
    public bool IsBoostCenterVisible => string.Equals(_currentTab, "boost-center", StringComparison.OrdinalIgnoreCase);
    public bool IsGameCenterVisible => string.Equals(_currentTab, "game-center", StringComparison.OrdinalIgnoreCase);
    public bool IsGamingVisible => string.Equals(_currentTab, "gaming", StringComparison.OrdinalIgnoreCase);
    public bool IsBenchmarksVisible => string.Equals(_currentTab, "benchmarks", StringComparison.OrdinalIgnoreCase);
    public bool IsDiagnosticsVisible => string.Equals(_currentTab, "diagnostics", StringComparison.OrdinalIgnoreCase);
    public bool IsTweaksVisible => string.Equals(_currentTab, "tweaks", StringComparison.OrdinalIgnoreCase);
    public bool IsDebloatVisible => string.Equals(_currentTab, "debloat", StringComparison.OrdinalIgnoreCase);
    public bool IsNetworkVisible => string.Equals(_currentTab, "network", StringComparison.OrdinalIgnoreCase);
    public bool IsAcceleratorVisible => string.Equals(_currentTab, "accelerator", StringComparison.OrdinalIgnoreCase);
    public bool IsPeripheralsVisible => string.Equals(_currentTab, "peripherals", StringComparison.OrdinalIgnoreCase);
    public bool IsDriversVisible => string.Equals(_currentTab, "drivers", StringComparison.OrdinalIgnoreCase);
    public bool IsSoftwareHubVisible => string.Equals(_currentTab, "software-hub", StringComparison.OrdinalIgnoreCase);
    public bool IsRestoreVisible => string.Equals(_currentTab, "restore", StringComparison.OrdinalIgnoreCase);
    public bool IsAiAssistantVisible => string.Equals(_currentTab, "ai-assistant", StringComparison.OrdinalIgnoreCase);

    public double CpuLoadPercent { get => _cpuLoadPercent; set { _cpuLoadPercent = Math.Clamp(value, 0, 100); OnPropertyChanged(); } }
    public double GpuLoadPercent { get => _gpuLoadPercent; set { _gpuLoadPercent = Math.Clamp(value, 0, 100); OnPropertyChanged(); } }
    public double RamUsagePercent { get => _ramUsagePercent; set { _ramUsagePercent = Math.Clamp(value, 0, 100); OnPropertyChanged(); } }
    public string GpuHotspotDeltaText { get => _gpuHotspotDeltaText; set { _gpuHotspotDeltaText = value; OnPropertyChanged(); } }
    public string OverallHealthScore { get => _overallHealthScore; set { _overallHealthScore = value; OnPropertyChanged(); } }
    public string BenchmarkResultSummary { get => _benchmarkResultSummary; set { _benchmarkResultSummary = value; OnPropertyChanged(); } }
    public string PeripheralCadenceSummary { get => _peripheralCadenceSummary; set { _peripheralCadenceSummary = value; OnPropertyChanged(); } }

    public bool IsAgentConnected
    {
        get => _isAgentConnected;
        set { _isAgentConnected = value; OnPropertyChanged(); }
    }

    public bool IsAdmin
    {
        get => _isAdmin;
        set { _isAdmin = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string HardwareSubsystemStatusText
    {
        get => _hardwareSubsystemStatusText;
        set { _hardwareSubsystemStatusText = value; OnPropertyChanged(); }
    }

    public string CpuModel { get => _cpuModel; set { _cpuModel = value; OnPropertyChanged(); } }
    public string CpuTemp { get => _cpuTemp; set { _cpuTemp = value; OnPropertyChanged(); } }
    public string CpuLoad { get => _cpuLoad; set { _cpuLoad = value; OnPropertyChanged(); } }
    public string CpuClock { get => _cpuClock; set { _cpuClock = value; OnPropertyChanged(); } }
    public string CpuPower { get => _cpuPower; set { _cpuPower = value; OnPropertyChanged(); } }

    public string GpuModel { get => _gpuModel; set { _gpuModel = value; OnPropertyChanged(); } }
    public string GpuTemp { get => _gpuTemp; set { _gpuTemp = value; OnPropertyChanged(); } }
    public string GpuHotspot { get => _gpuHotspot; set { _gpuHotspot = value; OnPropertyChanged(); } }
    public string GpuLoad { get => _gpuLoad; set { _gpuLoad = value; OnPropertyChanged(); } }
    public string GpuClock { get => _gpuClock; set { _gpuClock = value; OnPropertyChanged(); } }
    public string GpuPower { get => _gpuPower; set { _gpuPower = value; OnPropertyChanged(); } }
    public string GpuFanRpm { get => _gpuFanRpm; set { _gpuFanRpm = value; OnPropertyChanged(); } }
    public string GpuVram { get => _gpuVram; set { _gpuVram = value; OnPropertyChanged(); } }
    public string GpuThermalDiagnosisText { get => _gpuThermalDiagnosisText; set { _gpuThermalDiagnosisText = value; OnPropertyChanged(); } }

    public string RamUsage { get => _ramUsage; set { _ramUsage = value; OnPropertyChanged(); } }
    public string DisplayInfoText { get => _displayInfoText; set { _displayInfoText = value; OnPropertyChanged(); } }
    public string LastBackupStatus { get => _lastBackupStatus; set { _lastBackupStatus = value; OnPropertyChanged(); } }

    public SnapshotSummaryItem? SelectedBackup
    {
        get => _selectedBackup;
        set
        {
            _selectedBackup = value;
            OnPropertyChanged();
            InspectSelectedBackup();
        }
    }

    public bool ConfirmRestoreExecution
    {
        get => _confirmRestoreExecution;
        set { _confirmRestoreExecution = value; OnPropertyChanged(); }
    }

    public bool ConfirmIncompleteRestore
    {
        get => _confirmIncompleteRestore;
        set { _confirmIncompleteRestore = value; OnPropertyChanged(); }
    }

    public bool RollbackOnPartialFailure
    {
        get => _rollbackOnPartialFailure;
        set { _rollbackOnPartialFailure = value; OnPropertyChanged(); }
    }

    public string RestoreOperationReport
    {
        get => _restoreOperationReport;
        set { _restoreOperationReport = value; OnPropertyChanged(); }
    }

    public string RestoreUacStatusMessage
    {
        get => _restoreUacStatusMessage;
        set { _restoreUacStatusMessage = value; OnPropertyChanged(); }
    }

    public int RestoreProgressPercent
    {
        get => _restoreProgressPercent;
        set { _restoreProgressPercent = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
    }

    public string RestoreProgressStatus
    {
        get => _restoreProgressStatus;
        set { _restoreProgressStatus = value; OnPropertyChanged(); }
    }

    public bool ConfirmCleanerDeletion
    {
        get => _confirmCleanerDeletion;
        set { _confirmCleanerDeletion = value; OnPropertyChanged(); }
    }

    public string DebloatSummaryText
    {
        get => _debloatSummaryText;
        set { _debloatSummaryText = value; OnPropertyChanged(); }
    }

    public string NetworkSummaryText
    {
        get => _networkSummaryText;
        set { _networkSummaryText = value; OnPropertyChanged(); }
    }

    public string ExternalThroughputText
    {
        get => _externalThroughputText;
        set { _externalThroughputText = value; OnPropertyChanged(); }
    }

    public string PresentMonStatusText
    {
        get => _presentMonStatusText;
        set { _presentMonStatusText = value; OnPropertyChanged(); }
    }

    public RunningProcessInfo? SelectedProcess
    {
        get => _selectedProcess;
        set
        {
            _selectedProcess = value;
            if (value != null)
            {
                ManualProcessName = $"{value.ProcessName}.exe";
            }
            OnPropertyChanged();
        }
    }

    public string ManualProcessName
    {
        get => _manualProcessName;
        set { _manualProcessName = value; OnPropertyChanged(); }
    }

    public int CaptureDurationSeconds
    {
        get => _captureDurationSeconds;
        set { _captureDurationSeconds = Math.Clamp(value, 3, 120); OnPropertyChanged(); }
    }

    public bool IsCapturingPresentMon
    {
        get => _isCapturingPresentMon;
        set { _isCapturingPresentMon = value; OnPropertyChanged(); }
    }

    public string PresentMonMetricsSummary
    {
        get => _presentMonMetricsSummary;
        set { _presentMonMetricsSummary = value; OnPropertyChanged(); }
    }

    public string SecurityPostureText
    {
        get => _securityPostureText;
        set { _securityPostureText = value; OnPropertyChanged(); }
    }

    public string AcceleratorGameName
    {
        get => _acceleratorGameName;
        set { _acceleratorGameName = value; OnPropertyChanged(); }
    }

    public string AcceleratorTargetHost
    {
        get => _acceleratorTargetHost;
        set { _acceleratorTargetHost = value; OnPropertyChanged(); }
    }

    public int AcceleratorTargetPort
    {
        get => _acceleratorTargetPort;
        set { _acceleratorTargetPort = Math.Clamp(value, 1, 65535); OnPropertyChanged(); }
    }

    public string RouteVerdictText
    {
        get => _routeVerdictText;
        set { _routeVerdictText = value; OnPropertyChanged(); }
    }

    public string WfpSelectiveRoutingNoticeText
    {
        get => _wfpSelectiveRoutingNoticeText;
        set { _wfpSelectiveRoutingNoticeText = value; OnPropertyChanged(); }
    }

    public string WireGuardStatusText
    {
        get => _wireGuardStatusText;
        set { _wireGuardStatusText = value; OnPropertyChanged(); }
    }

    public string WireGuardConfigAuditText
    {
        get => _wireGuardConfigAuditText;
        set { _wireGuardConfigAuditText = value; OnPropertyChanged(); }
    }

    // Commands
    public ICommand SelectTabCommand { get; }
    public ICommand ToggleLanguageCommand { get; }
    public ICommand SetLanguagePtCommand { get; }
    public ICommand SetLanguageEnCommand { get; }
    public ICommand CreateSnapshotCommand { get; }
    public ICommand RefreshStatusCommand { get; }
    public ICommand ApplyTweakCommand { get; }
    public ICommand RestoreSingleTweakCommand { get; }
    public ICommand RefreshBackupsCommand { get; }
    public ICommand VerifySelectedBackupCommand { get; }
    public ICommand RestoreSelectedBackupCommand { get; }
    public ICommand RestoreSingleBackupEntryCommand { get; }
    public ICommand SimulateDryRunCommand { get; }
    public ICommand PurgeCategoryCommand { get; }
    public ICommand RunNetworkDiagCommand { get; }
    public ICommand RunExternalThroughputCommand { get; }
    public ICommand CheckPresentMonCommand { get; }
    public ICommand RefreshProcessesCommand { get; }
    public ICommand StartPresentMonCaptureCommand { get; }
    public ICommand CancelPresentMonCaptureCommand { get; }
    public ICommand RefreshDriversCommand { get; }
    public ICommand RefreshAuditLogsCommand { get; }
    public ICommand RequestElevationCommand { get; }
    public ICommand RefreshGameConnectionsCommand { get; }
    public ICommand EvaluateGameRouteCommand { get; }
    public ICommand RunTracerouteCommand { get; }
    public ICommand AuditWireGuardConfigCommand { get; }
    public ICommand CheckWireGuardTunnelCommand { get; }
    public ICommand RegisterSampleVpsNodeCommand { get; }
    public ICommand ApplySafeBoostProfileCommand { get; }
    public ICommand RunBenchmarkCommand { get; }
    public ICommand MeasureInputCadenceCommand { get; }
    public ICommand GenerateDiagnosticInsightsCommand { get; }

    public MainViewModel() : this(new AgentClientService(), null, new LocalizationService(), new DatabaseService(), new NetworkDiagnosticsService())
    {
    }

    public MainViewModel(
        AgentClientService agentClient,
        IHardwareMonitorService? hardwareMonitor,
        LocalizationService? localizationService = null,
        DatabaseService? databaseService = null,
        NetworkDiagnosticsService? networkDiagnostics = null)
    {
        _agentClient = agentClient;
        _hardwareMonitor = hardwareMonitor;
        Loc = localizationService ?? new LocalizationService();
        _databaseService = databaseService ?? new DatabaseService();
        _networkDiagnostics = networkDiagnostics ?? new NetworkDiagnosticsService();
        _restoreProgressStatus = Loc["RestoreProgressIdle"];

        InitializeModuleCapabilityMatrix();

        foreach (var def in RegistryOptimizer.Catalog.Values)
        {
            CatalogTweaks.Add(new TweakItemViewModel
            {
                TweakId = def.TweakId,
                NamePt = def.Description,
                NameEn = def.DescriptionEn,
                Name = Loc.CurrentCulture == "pt-BR" ? def.Description : def.DescriptionEn,
                Category = def.Category,
                RegistryPath = $"{def.Hive}\\{def.SubKeyPath}",
                ValueName = def.ValueName,
                ValueKind = def.ValueKind.ToString(),
                RecommendedValue = def.RecommendedValue,
                RiskLevel = def.RiskLevel,
                IsApplied = false,
                VerificationState = Loc.CurrentCulture == "pt-BR" ? "Não verificado" : "Not verified"
            });
        }

        foreach (var cat in DiskCleanerEngine.GetAuthorizedCategories())
        {
            CleanerCategories.Add(new CleanerCategoryViewModel
            {
                CategoryKey = cat.CategoryKey,
                NamePt = cat.NamePt,
                NameEn = cat.NameEn,
                DisplayName = Loc.CurrentCulture == "pt-BR" ? cat.NamePt : cat.NameEn,
                TargetPath = cat.TargetPath,
                RequiresAdminUac = cat.RequiresAdminUac
            });
        }

        SelectTabCommand = new RelayCommand(tab =>
        {
            if (tab is string tabName && !string.IsNullOrWhiteSpace(tabName))
            {
                CurrentTab = tabName;
            }
        });

        ToggleLanguageCommand = new RelayCommand(_ =>
        {
            Loc.ToggleLanguage();
            UpdateLocalizedCollections();
            OnPropertyChanged(nameof(Language));
            OnPropertyChanged(nameof(LanguageCodeDisplay));
            OnPropertyChanged(nameof(IsPortugueseActive));
            OnPropertyChanged(nameof(IsEnglishActive));
            OnPropertyChanged(nameof(AppliedTweaksBadgeText));
            OnPropertyChanged(nameof(HeaderTitle));
            OnPropertyChanged(nameof(ActiveModuleDisplayTitle));
            StatusMessage = Loc.CurrentCulture == "pt-BR"
                ? "Idioma alterado imediatamente para Português Brasileiro (PT-BR)."
                : "Language immediately switched to American English (EN-US).";
        });

        SetLanguagePtCommand = new RelayCommand(_ =>
        {
            Language = "pt-BR";
            StatusMessage = "Idioma definido para Português Brasileiro (PT-BR).";
        });

        SetLanguageEnCommand = new RelayCommand(_ =>
        {
            Language = "en-US";
            StatusMessage = "Language set to American English (EN-US).";
        });

        CreateSnapshotCommand = new RelayCommand(async _ =>
        {
            StatusMessage = Loc.CurrentCulture == "pt-BR"
                ? "Capturando e verificando snapshot SHA-256 dos ajustes suportados..."
                : "Capturing and verifying SHA-256 snapshot of supported settings...";
            var (success, details) = await _agentClient.CreateSnapshotAsync();
            RefreshBackupsList();
            RefreshAuditLogs();
            if (success)
            {
                LastBackupStatus = $"SHA-256 OK ({DateTime.Now:HH:mm:ss})";
                StatusMessage = Loc.CurrentCulture == "pt-BR"
                    ? "Snapshot criado e verificado no disco com sucesso."
                    : "Snapshot created and verified on disk successfully.";
            }
            else
            {
                StatusMessage = $"Falha / Snapshot incompleto: {details}";
            }
        });

        RefreshStatusCommand = new RelayCommand(async _ =>
        {
            await RefreshTelemetryAndStatusAsync();
        });

        ApplyTweakCommand = new RelayCommand(async param =>
        {
            if (param is TweakItemViewModel tweak)
            {
                var (ok, details) = await _agentClient.ApplyCatalogTweakAsync(tweak.TweakId, tweak.RecommendedValue);
                RefreshAuditLogs();
                if (ok)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(details);
                        if (doc.RootElement.TryGetProperty("operationId", out var opId))
                        {
                            tweak.LastOperationId = opId.GetString();
                        }
                    }
                    catch { }

                    tweak.IsApplied = true;
                    tweak.VerificationState = $"Verificado ({tweak.RecommendedValue})";
                    OnPropertyChanged(nameof(AppliedTweaksCount));
                    OnPropertyChanged(nameof(AppliedTweaksBadgeText));
                    await _databaseService.UpsertOptimizationActionAsync(
                        tweak.TweakId,
                        tweak.Name,
                        tweak.Category,
                        tweak.RegistryPath,
                        tweak.ValueName,
                        "Ledger-DPAPI",
                        tweak.RecommendedValue,
                        isApplied: true,
                        backupId: tweak.LastOperationId
                    );
                    StatusMessage = $"Ajuste '{tweak.TweakId}' aplicado, gravado no Ledger DPAPI + SQLite e confirmado por releitura.";
                }
                else
                {
                    tweak.VerificationState = "Falha / Requer UAC";
                    StatusMessage = $"Não foi possível aplicar '{tweak.TweakId}': {details}";
                }
            }
        });

        RestoreSingleTweakCommand = new RelayCommand(async param =>
        {
            if (param is TweakItemViewModel tweak)
            {
                // Secure backend-only restoration via DPAPI+HMAC ledger or verified snapshot
                var (ok, details) = await _agentClient.RestoreCatalogTweakAsync(
                    tweak.TweakId,
                    tweak.LastOperationId,
                    SelectedBackup?.SnapshotId
                );
                RefreshAuditLogs();
                if (ok)
                {
                    tweak.IsApplied = false;
                    tweak.VerificationState = "Restaurado (Ledger Seguro)";
                    OnPropertyChanged(nameof(AppliedTweaksCount));
                    OnPropertyChanged(nameof(AppliedTweaksBadgeText));
                    await _databaseService.UpsertOptimizationActionAsync(
                        tweak.TweakId,
                        tweak.Name,
                        tweak.Category,
                        tweak.RegistryPath,
                        tweak.ValueName,
                        "Ledger-DPAPI",
                        tweak.RecommendedValue,
                        isApplied: false,
                        backupId: tweak.LastOperationId
                    );
                    StatusMessage = $"Ajuste '{tweak.TweakId}' revertido ao estado original autêntico do backend e atualizado no SQLite.";
                }
                else
                {
                    StatusMessage = $"Falha ao reverter '{tweak.TweakId}': {details}";
                }
            }
        });

        RefreshBackupsCommand = new RelayCommand(_ => RefreshBackupsList());

        VerifySelectedBackupCommand = new RelayCommand(_ =>
        {
            if (SelectedBackup == null) return;
            var (isValid, checksum, err) = RegistrySnapshotEngine.VerifyBackupIntegrity(SelectedBackup.FilePath);
            RestoreOperationReport = isValid
                ? $"Integridade SHA-256 confirmada ({checksum}). Todas as entradas pertencem ao catálogo fechado."
                : $"Falha de integridade no snapshot: {err}";
        });

        RestoreSelectedBackupCommand = new RelayCommand(_ =>
        {
            if (SelectedBackup == null)
            {
                RestoreOperationReport = "Selecione um snapshot na lista antes de restaurar.";
                return;
            }

            if (!ConfirmRestoreExecution)
            {
                RestoreOperationReport = Loc["RestoreConfirmRequiredMsg"];
                return;
            }

            var (isIntegrityValid, _, integrityErr) = RegistrySnapshotEngine.VerifyBackupIntegrity(SelectedBackup.FilePath);
            if (!isIntegrityValid)
            {
                RestoreOperationReport = $"{Loc["RestoreIntegrityBlockedMsg"]} ({integrityErr})";
                RestoreProgressPercent = 0;
                return;
            }

            RestoreProgressPercent = 20;
            RestoreProgressStatus = Loc["RestoreProgressRunning"];
            RestoreItemResults.Clear();

            var report = RegistrySnapshotEngine.RestoreFromBackupWithReport(
                SelectedBackup.FilePath,
                confirmIncompleteBackup: ConfirmIncompleteRestore,
                singleTweakId: null,
                rollbackOnPartialFailure: RollbackOnPartialFailure
            );

            RefreshAuditLogs();
            RestoreProgressPercent = 100;
            RestoreProgressStatus = Loc["RestoreProgressDone"];

            foreach (var itemReport in report.ItemReports ?? Array.Empty<SnapshotItemRestoreReport>())
            {
                string statusTag = itemReport.RestoredAndVerified ? "[OK - VERIFICADO]" : "[FALHA]";
                RestoreItemResults.Add($"{statusTag} {itemReport.TweakId} ({itemReport.KeyPath}\\{itemReport.ValueName}) -> {itemReport.Message}");
            }

            if (report.Success)
            {
                RestoreOperationReport = $"Restauração completa verificada: {report.RestoredCount} itens restaurados, 0 falhas.";
            }
            else if (report.RolledBack)
            {
                RestoreOperationReport = $"Restauração falhou em {report.FailedCount} item(ns). Rollback transacional executado com sucesso.";
            }
            else
            {
                RestoreOperationReport = $"Resultado parcial/falha: {report.RestoredCount} restaurados, {report.FailedCount} falhas. Detalhe: {report.ErrorMessage}";
            }
        });

        RestoreSingleBackupEntryCommand = new RelayCommand(param =>
        {
            if (SelectedBackup == null || param is not RegistryValueEntry entry) return;
            if (!ConfirmRestoreExecution)
            {
                RestoreOperationReport = Loc["RestoreConfirmRequiredMsg"];
                return;
            }

            string targetId = !string.IsNullOrWhiteSpace(entry.TweakId)
                ? entry.TweakId
                : (RegistryOptimizer.FindAuthorizedCatalogEntry(entry.KeyPath, entry.ValueName)?.TweakId ?? "");

            RestoreProgressPercent = 35;
            RestoreProgressStatus = Loc["RestoreProgressRunning"];

            var report = RegistrySnapshotEngine.RestoreFromBackupWithReport(
                SelectedBackup.FilePath,
                confirmIncompleteBackup: true,
                singleTweakId: targetId,
                rollbackOnPartialFailure: false
            );
            RefreshAuditLogs();

            RestoreProgressPercent = 100;
            RestoreProgressStatus = Loc["RestoreProgressDone"];

            RestoreItemResults.Clear();
            foreach (var itemReport in report.ItemReports ?? Array.Empty<SnapshotItemRestoreReport>())
            {
                string statusTag = itemReport.RestoredAndVerified ? "[OK - VERIFICADO]" : "[FALHA]";
                RestoreItemResults.Add($"{statusTag} {itemReport.TweakId} ({itemReport.KeyPath}\\{itemReport.ValueName}) -> {itemReport.Message}");
            }

            RestoreOperationReport = report.Success
                ? $"Item '{targetId}' ({entry.ValueName}) restaurado e verificado com sucesso."
                : $"Falha ao restaurar item '{targetId}': {report.ErrorMessage}";
        });

        SimulateDryRunCommand = new RelayCommand(param =>
        {
            var targets = param is CleanerCategoryViewModel single
                ? new[] { single }
                : CleanerCategories.ToArray();

            long totalEstimated = 0;
            int totalFiles = 0;
            foreach (var cat in targets)
            {
                var dryRun = DiskCleanerEngine.SimulateCategoryDryRun(cat.CategoryKey);
                totalEstimated += dryRun.EstimatedBytes;
                totalFiles += dryRun.CandidateFilesCount;
                double mb = dryRun.EstimatedBytes / (1024.0 * 1024.0);
                cat.DryRunEstimateText = $"Estimativa (Dry-Run): {mb:F2} MB ({dryRun.CandidateFilesCount} arquivos, {dryRun.SkippedSymlinksCount} symlinks ignorados)";
            }

            DebloatSummaryText = $"Simulação concluída (SOMENTE ESTIMATIVA): {(totalEstimated / (1024.0 * 1024.0)):F2} MB em {totalFiles} arquivos candidatos.";
        });

        PurgeCategoryCommand = new RelayCommand(param =>
        {
            if (!ConfirmCleanerDeletion)
            {
                DebloatSummaryText = Loc.CurrentCulture == "pt-BR"
                    ? "Ação bloqueada: marque a caixa de confirmação explícita antes de excluir arquivos."
                    : "Action blocked: check the explicit confirmation box before deleting files.";
                return;
            }

            if (param is CleanerCategoryViewModel cat)
            {
                var report = DiskCleanerEngine.PurgeCategoryWithReport(cat.CategoryKey, confirmServiceStop: ConfirmCleanerDeletion);
                double freedMb = report.FreedBytes / (1024.0 * 1024.0);
                cat.RealFreedText = $"Liberado real: {freedMb:F2} MB ({report.DeletedFilesCount} removidos, {report.SkippedLockedCount} em uso, {report.SkippedSymlinksCount} symlinks bloqueados)";

                CleanerAffectedFilesLog.Clear();
                foreach (var logLine in report.AffectedItemsLog)
                {
                    CleanerAffectedFilesLog.Add(logLine);
                }

                DebloatSummaryText = report.PartialSuccess
                    ? $"Limpeza parcial em '{cat.CategoryKey}': {freedMb:F2} MB liberados ({report.SkippedLockedCount} arquivos bloqueados por processos em execução)."
                    : $"Limpeza concluída em '{cat.CategoryKey}': {freedMb:F2} MB efetivamente liberados.";
            }
        });

        RunNetworkDiagCommand = new RelayCommand(async _ =>
        {
            NetworkSummaryText = "Executando diagnósticos separados ICMP Echo, TCP Handshake e DNS...";
            var report = await _networkDiagnostics.RunRealDiagnosticsAsync();

            NetworkEndpoints.Clear();
            if (report.EndpointDetails != null)
            {
                foreach (var ep in report.EndpointDetails)
                {
                    NetworkEndpoints.Add(ep);
                }
            }

            DnsResults.Clear();
            if (report.DnsChecks != null)
            {
                foreach (var d in report.DnsChecks)
                {
                    DnsResults.Add(d);
                }
            }

            NetworkHistory.Clear();
            foreach (var h in _networkDiagnostics.GetDiagnosticHistory())
            {
                NetworkHistory.Add(h);
            }

            NetworkSummaryText = $"Diagnóstico concluído ({DateTime.Now:HH:mm:ss}) · Perda ICMP: {report.FormattedPacketLoss} · TCP: {report.TcpState} ({report.TcpReachabilityPercent:F0}%)";
        });

        RunExternalThroughputCommand = new RelayCommand(async _ =>
        {
            ExternalThroughputText = "Medindo download real contra https://speed.cloudflare.com/__down ...";
            var res = await _networkDiagnostics.MeasureExternalThroughputAsync();
            if (res.Available && res.DownloadMbps.HasValue)
            {
                ExternalThroughputText = $"Throughput Real: {res.DownloadMbps.Value:F1} Mbps ({res.BytesTransferred / 1024} KB em {res.DurationMs} ms via Cloudflare CDN)";
            }
            else
            {
                ExternalThroughputText = $"Indisponível: {res.ErrorReason}";
            }

            NetworkHistory.Clear();
            foreach (var h in _networkDiagnostics.GetDiagnosticHistory())
            {
                NetworkHistory.Add(h);
            }
        });

        CheckPresentMonCommand = new RelayCommand(_ =>
        {
            var (installed, path, ver) = PresentMonSessionParser.CheckPresentMonInstallation();
            PresentMonStatusText = installed
                ? $"PresentMon instalado (v{ver}) em: {path}"
                : "PresentMon não encontrado. Instale em C:\\Program Files\\Intel\\PresentMon\\PresentMon.exe para captura real.";
        });

        RefreshProcessesCommand = new RelayCommand(_ => RefreshRunningProcesses());

        StartPresentMonCaptureCommand = new RelayCommand(async _ =>
        {
            if (IsCapturingPresentMon) return;
            IsCapturingPresentMon = true;
            _presentMonCts = new CancellationTokenSource();

            PresentMonStatusText = $"Capturando '{ManualProcessName}' por {CaptureDurationSeconds}s...";
            try
            {
                var response = await PresentMonSessionParser.CaptureProcessSessionAsync(
                    ManualProcessName,
                    CaptureDurationSeconds,
                    _presentMonCts.Token
                );

                if (response.Success && response.Summary != null)
                {
                    PresentMonStatusText = $"Captura concluída! CSV salvo em: {response.CsvFilePath}";
                    PresentMonMetricsSummary =
                        $"FPS Médio: {response.Summary.AverageFps:F1} | 1% Low: {response.Summary.OnePercentLowFps:F1} | " +
                        $"Frametime Médio: {response.Summary.AverageFrametimeMs:F2} ms (σ={response.Summary.FrametimeStandardDeviationMs:F2} ms) | " +
                        $"Quadros: {response.Summary.TotalFramesCount}";

                    RecentFrametimeSamples.Clear();
                    if (response.Summary.RecentFrameTimesMs != null)
                    {
                        foreach (var ft in response.Summary.RecentFrameTimesMs)
                        {
                            RecentFrametimeSamples.Add(ft);
                        }
                    }
                    RebuildFrametimeChartPoints();

                    PresentMonHistory.Clear();
                    foreach (var s in PresentMonSessionParser.GetSessionHistory())
                    {
                        PresentMonHistory.Add(s);
                    }
                }
                else
                {
                    PresentMonStatusText = $"Captura não produziu métricas: {response.ErrorMessage}";
                }
            }
            finally
            {
                IsCapturingPresentMon = false;
                _presentMonCts?.Dispose();
                _presentMonCts = null;
            }
        });

        CancelPresentMonCaptureCommand = new RelayCommand(_ =>
        {
            _presentMonCts?.Cancel();
        });

        RefreshDriversCommand = new RelayCommand(_ => RefreshDriversList());
        RefreshAuditLogsCommand = new RelayCommand(_ => RefreshAuditLogs());

        RequestElevationCommand = new RelayCommand(_ =>
        {
            if (UacElevationBridge.IsRunningAsAdministrator())
            {
                StatusMessage = Loc.CurrentCulture == "pt-BR"
                    ? "O processo já está em execução como Administrador (UAC ativo)."
                    : "Process is already running as Administrator (UAC active).";
                return;
            }
            bool restarted = UacElevationBridge.TryRestartElevated();
            if (!restarted)
            {
                StatusMessage = Loc.CurrentCulture == "pt-BR"
                    ? "Elevação UAC cancelada pelo usuário. Operando em modo padrão."
                    : "UAC elevation cancelled by user. Operating in standard mode.";
            }
        });

        RefreshGameConnectionsCommand = new RelayCommand(_ =>
        {
            DetectedGameSockets.Clear();
            foreach (var conn in GameNetworkAcceleratorService.DetectRunningGamesAndConnections())
            {
                DetectedGameSockets.Add(conn);
            }
            RelayNodes.Clear();
            foreach (var node in GameNetworkAcceleratorService.GetRegisteredRelayNodes())
            {
                RelayNodes.Add(node);
            }
        });

        EvaluateGameRouteCommand = new RelayCommand(async _ =>
        {
            RouteVerdictText = Loc.CurrentCulture == "pt-BR"
                ? $"Executando sondagem multi-amostra para {AcceleratorGameName} ({AcceleratorTargetHost}:{AcceleratorTargetPort})..."
                : $"Running multi-sample probing for {AcceleratorGameName} ({AcceleratorTargetHost}:{AcceleratorTargetPort})...";

            var eval = await GameNetworkAcceleratorService.AnalyzeGameRouteAsync(
                AcceleratorGameName,
                AcceleratorTargetHost,
                AcceleratorTargetPort,
                sampleCount: 5
            );

            RouteVerdictText = Loc.CurrentCulture == "pt-BR" ? eval.HonestVerdictPt : eval.HonestVerdictEn;

            await _databaseService.RecordRouteDiagnosticAsync(new NetworkRouteDiagnosticRecord
            {
                GameProcessName = AcceleratorGameName,
                TargetEndpoint = $"{AcceleratorTargetHost}:{AcceleratorTargetPort}",
                DirectRttMs = eval.DirectRoute.AverageRttMs,
                DirectJitterMs = eval.DirectRoute.JitterMs,
                DirectLossPercent = eval.DirectRoute.PacketLossPercent,
                CandidateNodeId = eval.BestRelayNodeId,
                CandidateRttMs = eval.EstimatedTotalRelayRttMs,
                CandidateJitterMs = eval.RelayIngressProbe?.JitterMs,
                CandidateLossPercent = eval.RelayIngressProbe?.PacketLossPercent ?? 100.0,
                RecommendedRoute = eval.RecommendedMode,
                RecommendationReason = eval.HonestVerdictPt,
                MeasuredAtUtc = DateTime.UtcNow
            });
        });

        RunTracerouteCommand = new RelayCommand(async _ =>
        {
            TracerouteHops.Clear();
            var hops = await GameNetworkAcceleratorService.RunTracerouteAsync(AcceleratorTargetHost, maxHops: 14);
            foreach (var h in hops)
            {
                TracerouteHops.Add(h);
            }
        });

        RegisterSampleVpsNodeCommand = new RelayCommand(_ =>
        {
            RouteVerdictText = Loc.CurrentCulture == "pt-BR"
                ? "Nenhum servidor relay real vem incluído no aplicativo. O antigo GRU-01 era apenas um exemplo com endereço e chave fictícios e foi removido. Para testar uma rota por WireGuard, é necessário cadastrar um VPS próprio com endereço, porta UDP, chave pública e redes de destino reais."
                : "The app does not include a real relay server. The old GRU-01 entry used a fictitious address and key and has been removed. To test a WireGuard route, configure your own VPS with a real endpoint, UDP port, public key, and destination networks.";
        });

        AuditWireGuardConfigCommand = new RelayCommand(_ =>
        {
            WireGuardConfigAuditText = Loc.CurrentCulture == "pt-BR"
                ? "A validação só é confiável com os dados reais do seu túnel: chave privada local, chave pública do VPS, endereço, porta e redes do jogo. O aplicativo não vai gerar uma chave de exemplo nem declarar um túnel fictício como válido. O cadastro seguro desses dados ainda precisa ser concluído."
                : "Validation is only meaningful with your real tunnel data: local private key, VPS public key, endpoint, port, and game networks. The app will not generate a sample key or report a fictitious tunnel as valid. Secure entry of these settings still needs to be completed.";
        });

        CheckWireGuardTunnelCommand = new RelayCommand(async _ =>
        {
            var status = await WireGuardTunnelManager.QueryTunnelStatusAsync("PCControlGameTunnel");
            WireGuardStatusText = status.DiagnosticMessage;
        });

        ApplySafeBoostProfileCommand = new RelayCommand(async _ =>
        {
            var safeTweaks = CatalogTweaks
                .Where(t => !t.IsApplied &&
                    (string.Equals(t.RiskLevel, "Safe", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(t.RiskLevel, "Seguro", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (safeTweaks.Count == 0)
            {
                StatusMessage = Loc.CurrentCulture == "pt-BR"
                    ? "As otimizações seguras já estão aplicadas ou não há itens seguros disponíveis."
                    : "Safe optimizations are already applied or no safe items are available.";
                return;
            }

            StatusMessage = Loc.CurrentCulture == "pt-BR"
                ? "Criando e verificando um ponto de restauração antes das otimizações..."
                : "Creating and verifying a restore point before applying optimizations.";

            var (snapshotCreated, snapshotDetails) = await _agentClient.CreateSnapshotAsync();
            RefreshBackupsList();
            RefreshAuditLogs();

            if (!snapshotCreated)
            {
                StatusMessage = Loc.CurrentCulture == "pt-BR"
                    ? $"Nenhuma alteração foi aplicada porque o backup não foi confirmado: {snapshotDetails}"
                    : $"No changes were applied because the backup could not be verified: {snapshotDetails}";
                return;
            }

            int appliedCount = 0;
            int failedCount = 0;

            foreach (var tweak in safeTweaks)
            {
                var (success, details) = await _agentClient.ApplyCatalogTweakAsync(tweak.TweakId, tweak.RecommendedValue);
                if (!success)
                {
                    tweak.VerificationState = "Falha / Requer UAC";
                    failedCount++;
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(details);
                    if (doc.RootElement.TryGetProperty("operationId", out var operationId))
                        tweak.LastOperationId = operationId.GetString();
                }
                catch { }

                tweak.IsApplied = true;
                tweak.VerificationState = $"Verificado ({tweak.RecommendedValue})";
                await _databaseService.UpsertOptimizationActionAsync(
                    tweak.TweakId,
                    tweak.Name,
                    tweak.Category,
                    tweak.RegistryPath,
                    tweak.ValueName,
                    "Ledger-DPAPI",
                    tweak.RecommendedValue,
                    isApplied: true,
                    backupId: tweak.LastOperationId
                );
                appliedCount++;
            }

            RefreshAuditLogs();
            OnPropertyChanged(nameof(AppliedTweaksCount));
            OnPropertyChanged(nameof(AppliedTweaksBadgeText));

            StatusMessage = Loc.CurrentCulture == "pt-BR"
                ? $"Backup confirmado. {appliedCount} otimização(ões) segura(s) aplicada(s) e verificadas; {failedCount} falha(s)."
                : $"Backup verified. {appliedCount} safe optimization(s) applied and verified; {failedCount} failure(s).";
        });

        RunBenchmarkCommand = new RelayCommand(async _ =>
        {
            BenchmarkResultSummary = Loc.CurrentCulture == "pt-BR"
                ? "Executando teste local de CPU e memória..."
                : "Running local CPU and memory test...";

            var result = await Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                double acc = 0;
                for (int i = 1; i <= 2_500_000; i++)
                    acc += Math.Sqrt(i) * Math.Sin(i & 0xFF);

                sw.Stop();
                long cpuMs = Math.Max(1, sw.ElapsedMilliseconds);
                int opsScore = (int)Math.Round(250_000.0 / cpuMs * 10.0);

                var memSw = System.Diagnostics.Stopwatch.StartNew();
                byte[] buffer = new byte[32 * 1024 * 1024];
                for (int i = 0; i < buffer.Length; i += 64)
                    buffer[i] = (byte)(i & 0xFF);

                memSw.Stop();
                double memBandwidthGbS = Math.Round((32.0 / 1024.0) / Math.Max(0.0005, memSw.Elapsed.TotalSeconds), 2);
                return (opsScore, cpuMs, memBandwidthGbS, checksum: acc);
            });

            BenchmarkResultSummary = Loc.CurrentCulture == "pt-BR"
                ? $"CPU: {result.opsScore} pontos ({result.cpuMs} ms) · Memória: {result.memBandwidthGbS:F2} GB/s · Teste local concluído."
                : $"CPU: {result.opsScore} points ({result.cpuMs} ms) · Memory: {result.memBandwidthGbS:F2} GB/s · Local test completed.";
        });

        MeasureInputCadenceCommand = new RelayCommand(_ =>
        {
            PeripheralCadenceSummary = Loc.CurrentCulture == "pt-BR"
                ? $"Monitor: {DisplayInfoText} · Frequência do contador de alta resolução: {System.Diagnostics.Stopwatch.Frequency / 1_000_000.0:F2} MHz. Esta verificação não mede a taxa de polling do mouse/teclado nem detecta perda de eventos."
                : $"Monitor: {DisplayInfoText} · High-resolution counter frequency: {System.Diagnostics.Stopwatch.Frequency / 1_000_000.0:F2} MHz. This check does not measure mouse/keyboard polling rate or detect dropped input events.";
        });

        GenerateDiagnosticInsightsCommand = new RelayCommand(_ => RefreshAssistantInsights());

        InitializeVerifiedSoftwareCatalog();
        RefreshAssistantInsights();

        Navigation = new NavigationService(this);
        RefreshBackupsList();
        RefreshAuditLogs();
        _ = _databaseService.InitializeAndVerifyAsync();

        _telemetryRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _telemetryRefreshTimer.Tick += async (_, _) =>
        {
            try
            {
                await RefreshTelemetryAndStatusAsync();
            }
            catch (Exception ex)
            {
                HardwareSubsystemStatusText = $"Falha ao atualizar sensores: {ex.Message}";
            }
        };
        _telemetryRefreshTimer.Start();
        _ = RefreshTelemetryAndStatusAsync();
    }

    private void InitializeVerifiedSoftwareCatalog()
    {
        VerifiedWingetCatalog.Clear();
        foreach (var pkg in WingetSoftwareCenterService.OfficialCatalog)
        {
            VerifiedWingetCatalog.Add(new VerifiedSoftwareItem(
                pkg.Name,
                pkg.CategoryDisplayName,
                pkg.PackageId,
                pkg.Publisher,
                $"winget install --id \"{pkg.PackageId}\" --exact --source winget --accept-package-agreements --accept-source-agreements",
                pkg.Description
            ));
        }
    }

    private void RefreshAssistantInsights()
    {
        AssistantDiagnosticInsights.Clear();
        AssistantDiagnosticInsights.Add($"[GPU & Térmica Universal] Modelo prioritário: {GpuModel} | Core: {GpuTemp} | Hotspot: {GpuHotspot} ({GpuThermalDiagnosisText})");
        AssistantDiagnosticInsights.Add($"[Roteamento & Rede] {RouteVerdictText}");
        AssistantDiagnosticInsights.Add($"[Segurança & Backups] {SecurityPostureText} | Último Snapshot: {LastBackupStatus}");
        AssistantDiagnosticInsights.Add("[Política Operacional] Todas as otimizações exigem catálogo fechado, snapshot SHA-256 e registro prévio no Ledger DPAPI+HMAC. Nenhum driver, voltagem ou proteção do Windows é alterado automaticamente.");
    }

    private void InitializeModuleCapabilityMatrix()
    {
        ModuleCapabilityMatrix.Clear();
        var items = new[]
        {
            new ModuleCapabilityMatrixItem(1, "Dashboard", "Funcional", "UniversalHardwareEngine + Sensores", "Sessão + SQLite", "Visão executiva universal com perfil automático do PC, CPU, GPU dedicada/integrada, RAM e NVMe."),
            new ModuleCapabilityMatrixItem(2, "Hardware Monitor", "Funcional", "UniversalHardwareEngine + LHM + WMI", "Logs / Sessão", "Pipeline multi-fonte com proveniência por métrica; N/D quando não suportado."),
            new ModuleCapabilityMatrixItem(3, "GPU Health Lab", "Funcional", "GpuThermalAuditor (Multi-Vendor)", "Logs / Sessão", "Suporte universal AMD Radeon, NVIDIA GeForce/RTX, Intel Arc e Qualcomm Adreno; avalia Core vs Hotspot ΔT."),
            new ModuleCapabilityMatrixItem(4, "Boost Center", "Funcional (Perfis)", "RegistryOptimizer + CompatibilityEngine", "SQLite + Ledger DPAPI", "Aplica perfis reversíveis com verificação de compatibilidade e backup SHA-256."),
            new ModuleCapabilityMatrixItem(5, "Game Center", "Funcional", "GameProfileManager + GameNetworkAccelerator", "SQLite", "Detecta jogos instalados e em execução com sockets TCP/UDP ativos."),
            new ModuleCapabilityMatrixItem(6, "Performance Lab", "Funcional", "PresentMonSessionParser", "Arquivos CSV", "Captura frametimes reais (Média, 1% Low, 0.1% Low) via Intel PresentMon CLI."),
            new ModuleCapabilityMatrixItem(7, "Benchmark Center", "Funcional / Determinístico", "Diagnóstico Sintético Local", "SQLite", "Medição determinística de CPU, RAM e Armazenamento sem scores inventados."),
            new ModuleCapabilityMatrixItem(8, "Diagnostics / System Doctor", "Funcional", "SystemDoctorService + IntegrityVerifier", "Registro + SQLite + EventLog", "Diagnóstico imediato em leitura e reparos assistidos DISM/SFC/CHKDSK com confirmação UAC."),
            new ModuleCapabilityMatrixItem(9, "Windows Tweaks", "Funcional (11 Categorias)", "RegistryOptimizer + CompatibilityEngine", "Ledger DPAPI+HMAC + SQLite", "11 categorias com validação de estado Ativo/Inativo/Parcial, backup prévio e reversão individual."),
            new ModuleCapabilityMatrixItem(10, "Debloat & Cleaner", "Funcional", "DiskCleanerEngine", "Relatório de Sessão", "Dry-Run separado de Purge; bloqueia ReparsePoints/Symlinks."),
            new ModuleCapabilityMatrixItem(11, "Network Lab", "Funcional", "NetworkDiagnosticsService", "Histórico em Memória", "ICMP Echo, TCP:443 multi-alvo, DNS e Throughput separados; zero perda TCP artificial."),
            new ModuleCapabilityMatrixItem(12, "Game Network Accelerator", "Funcional c/ 5 Modos", "GameNetworkAcceleratorService + WireGuard", "SQLite + ACL Restrita", "Modos Automático/Gaming/Baixa Latência/Estabilidade/Personalizado; analisa Wi-Fi vs Ethernet e protege PrivateKey."),
            new ModuleCapabilityMatrixItem(13, "Peripheral Test", "Funcional", "Monitor & Input Cadence Inspector", "Sessão", "Avalia cadência de eventos de input e refresh rate Win32."),
            new ModuleCapabilityMatrixItem(14, "Driver Center", "Funcional (Leitura)", "DriverScannerService (WMI)", "Somente Leitura", "Inventário de drivers assinados; não baixa nem instala drivers sem consentimento."),
            new ModuleCapabilityMatrixItem(15, "Software Hub", "Funcional (14 Categorias)", "WingetSoftwareCenterService", "Fila Verificada", "Busca, verifica, instala, atualiza e desinstala pacotes oficiais via winget com confirmação."),
            new ModuleCapabilityMatrixItem(16, "Restore Center", "Funcional", "RegistrySnapshotEngine + Ledger", "JSON SHA-256 + DPAPI", "Verificação SHA-256 obrigatória, histórico de alterações e rollback transacional."),
            new ModuleCapabilityMatrixItem(17, "AI Assistant", "Consultivo Baseado em Evidências", "RecommendationEngine + Diagnóstico Local", "Somente Leitura", "Gera recomendações baseadas exclusivamente em evidências reais sem viés de fabricante.")
        };
        foreach (var item in items)
        {
            ModuleCapabilityMatrix.Add(item);
        }
    }

    private void UpdateLocalizedCollections()
    {
        bool isPt = Loc.CurrentCulture == "pt-BR";
        var capReport = GameNetworkAcceleratorService.GetSelectiveRoutingCapabilityReport();
        WfpSelectiveRoutingNoticeText = isPt ? capReport.WfpLimitationExplanationPt : capReport.WfpLimitationExplanationEn;
        foreach (var tweak in CatalogTweaks)
        {
            tweak.Name = isPt ? tweak.NamePt : tweak.NameEn;
        }
        foreach (var cat in CleanerCategories)
        {
            cat.DisplayName = isPt ? cat.NamePt : cat.NameEn;
        }
        UpdateRestoreUacWarning();
    }

    private void RefreshBackupsList()
    {
        ExistingBackups.Clear();
        foreach (var item in RegistrySnapshotEngine.ListBackups())
        {
            ExistingBackups.Add(item);
        }
        if (ExistingBackups.Count > 0 && SelectedBackup == null)
        {
            SelectedBackup = ExistingBackups[0];
        }
    }

    private void InspectSelectedBackup()
    {
        SelectedBackupEntries.Clear();
        SelectedBackupFailures.Clear();
        if (SelectedBackup == null)
        {
            RestoreUacStatusMessage = string.Empty;
            return;
        }

        var manifest = RegistrySnapshotEngine.InspectBackup(SelectedBackup.FilePath);
        if (manifest == null) return;

        foreach (var e in manifest.Entries)
        {
            SelectedBackupEntries.Add(e);
        }
        foreach (var f in manifest.UnreadableItems)
        {
            SelectedBackupFailures.Add(f);
        }

        UpdateRestoreUacWarning();
    }

    private void UpdateRestoreUacWarning()
    {
        bool hasHklm = SelectedBackupEntries.Any(e =>
            e.KeyPath.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) ||
            e.KeyPath.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase));

        RestoreUacStatusMessage = (hasHklm && !IsAdmin)
            ? Loc["RestoreUacWarning"]
            : Loc["RestoreUacOk"];
    }

    private void RefreshRunningProcesses()
    {
        AvailableProcesses.Clear();
        foreach (var p in PresentMonSessionParser.ListAvailableProcesses())
        {
            AvailableProcesses.Add(p);
        }
    }

    private void RefreshDriversList()
    {
        InstalledDrivers.Clear();
        foreach (var drv in DriverScannerService.QuerySignedDrivers())
        {
            InstalledDrivers.Add(drv);
        }
    }

    private void RefreshAuditLogs()
    {
        AuditLogs.Clear();
        foreach (var log in RegistryOptimizer.GetAuditLogs().Reverse())
        {
            AuditLogs.Add(log);
        }
    }

    public async Task RefreshTelemetryAndStatusAsync()
    {
        if (!await _telemetryRefreshGate.WaitAsync(0)) return;
        try
        {
        string unavailable = Loc["SensorUnavailable"];

        IsAgentConnected = await _agentClient.CheckConnectionAsync();
        IsAdmin = _agentClient.IsElevated;
        UpdateRestoreUacWarning();

        StatusMessage = IsAgentConnected
            ? (IsAdmin ? Loc["AgentConnectedAdmin"] : Loc["AgentConnectedStandard"])
            : Loc["AgentDisconnected"];

        SecurityPostureText =
            $"Agente Autenticado: {IsAgentConnected} | DPAPI Aplicado: {_agentClient.IsTokenDpapiProtected} | " +
            $"ACL NTFS Restrita ao Usuário: {_agentClient.IsTokenAclRestricted} | Assinatura HMAC-SHA256: Ativa | Administrador (UAC): {IsAdmin}";

        // Update Display Settings via real Win32 P/Invoke
        var dispObj = DisplaySettingsService.GetPrimaryDisplayInfo();
        string dispJson = JsonSerializer.Serialize(dispObj);
        using (var dispDoc = JsonDocument.Parse(dispJson))
        {
            bool dispAvail = dispDoc.RootElement.TryGetProperty("available", out var av) && av.GetBoolean();
            if (dispAvail)
            {
                string mon = dispDoc.RootElement.GetProperty("monitorName").GetString() ?? "Monitor";
                string res = dispDoc.RootElement.GetProperty("resolution").GetString() ?? "";
                int hz = dispDoc.RootElement.GetProperty("nativeRefreshRateHz").GetInt32();
                DisplayInfoText = $"{mon} · {res} @ {hz} Hz";
            }
            else
            {
                DisplayInfoText = Loc["NotDetected"];
            }
        }

        // Update Hardware Sensors from real LibreHardwareMonitor (Zero Fake Fallbacks)
        AllHardwareSensors.Clear();
        if (_hardwareMonitor != null)
        {
            var snap = await _hardwareMonitor.CaptureSnapshotAsync();
            HardwareSubsystemStatusText = snap.Status switch
            {
                SensorReadingStatus.Available => Loc["SensorStatusAvailable"],
                SensorReadingStatus.SensorNotSupported => Loc["SensorStatusNotSupported"],
                SensorReadingStatus.CommunicationFailure => $"{Loc["SensorStatusCommFailure"]} ({snap.DiagnosticReason})",
                _ => $"{Loc["SensorStatusUnavailable"]} ({snap.DiagnosticReason})"
            };

            if (snap.IsHardwareMonitorAvailable)
            {
                foreach (var s in snap.Sensors)
                {
                    AllHardwareSensors.Add(s);
                }

                string notSupported = Loc["SensorStatusNotSupported"];
                CpuModel = !string.IsNullOrWhiteSpace(snap.CpuModelName) ? snap.CpuModelName : unavailable;
                CpuTemp = snap.CpuPackageTemp.HasValue ? $"{snap.CpuPackageTemp.Value:F1} °C" : notSupported;
                CpuLoad = snap.CpuTotalLoad.HasValue ? $"{snap.CpuTotalLoad.Value:F0} %" : notSupported;
                CpuLoadPercent = snap.CpuTotalLoad ?? 0;
                CpuClock = snap.CpuClockMhz.HasValue ? $"{snap.CpuClockMhz.Value:F0} MHz" : notSupported;
                CpuPower = snap.CpuPowerWatts.HasValue ? $"{snap.CpuPowerWatts.Value:F1} W" : notSupported;

                GpuModel = !string.IsNullOrWhiteSpace(snap.GpuModelName) ? snap.GpuModelName : unavailable;
                GpuTemp = snap.GpuCoreTemp.HasValue ? $"{snap.GpuCoreTemp.Value:F1} °C" : notSupported;
                GpuHotspot = snap.GpuHotspotTemp.HasValue ? $"{snap.GpuHotspotTemp.Value:F1} °C" : notSupported;
                GpuLoad = snap.GpuLoadPercent.HasValue ? $"{snap.GpuLoadPercent.Value:F0} %" : notSupported;
                GpuLoadPercent = snap.GpuLoadPercent ?? 0;
                GpuClock = snap.GpuClockMhz.HasValue ? $"{snap.GpuClockMhz.Value:F0} MHz" : notSupported;
                GpuPower = snap.GpuPowerWatts.HasValue ? $"{snap.GpuPowerWatts.Value:F1} W" : notSupported;
                GpuFanRpm = snap.GpuFanRpm.HasValue ? $"{snap.GpuFanRpm.Value:F0} RPM" : notSupported;

                if (snap.GpuVramUsedMb.HasValue && snap.GpuVramTotalMb.HasValue && snap.GpuVramTotalMb.Value > 0)
                {
                    GpuVram = $"{snap.GpuVramUsedMb.Value:F0} MB / {snap.GpuVramTotalMb.Value:F0} MB";
                }
                else
                {
                    GpuVram = notSupported;
                }

                if (snap.GpuCoreTemp.HasValue && snap.GpuHotspotTemp.HasValue)
                {
                    var thermalEval = GpuThermalAuditor.EvaluateThermalState(snap.GpuCoreTemp.Value, snap.GpuHotspotTemp.Value, snap.GpuModelName);
                    GpuHotspotDeltaText = $"+{thermalEval.Delta:F1} °C";
                    GpuThermalDiagnosisText = $"Status: {thermalEval.Status} (Delta Core/Hotspot: +{thermalEval.Delta:F1} °C) — {thermalEval.Message}";
                    // A GPU hotspot delta alone is not a complete system-health score.
                    // Keep the score unavailable until a transparent multi-component model exists.
                    OverallHealthScore = "N/D";
                }
                else
                {
                    GpuHotspotDeltaText = unavailable;
                    GpuThermalDiagnosisText = notSupported;
                    // Missing GPU thermal sensors must never be converted into a fabricated health score.
                    OverallHealthScore = "N/D";
                }

                if (snap.RamUsedGb.HasValue && snap.RamTotalGb.HasValue && snap.RamTotalGb.Value > 0)
                {
                    double used = snap.RamUsedGb.Value;
                    double total = snap.RamTotalGb.Value;
                    double avail = Math.Max(0, total - used);
                    RamUsage = $"{used:F1} GB / {total:F1} GB";
                    RamUsagePercent = Math.Round((used / total) * 100.0, 1);
                    RamTotalAndTypeText = $"{total:F0} GB Physical RAM";
                    RamUsedText = $"{used:F1} GB";
                    RamAvailableText = $"{avail:F1} GB";
                }
                else
                {
                    RamUsage = notSupported;
                    RamUsagePercent = 0;
                    RamTotalAndTypeText = notSupported;
                    RamUsedText = notSupported;
                    RamAvailableText = notSupported;
                }
                OnPropertyChanged(nameof(RamUsageWithPercent));

                UpdatePrimaryDiskAndProfileTelemetry(snap.GpuModelName, snap.RamTotalGb);
                AppendTelemetryChartSample(snap.CpuTotalLoad ?? 0, snap.GpuLoadPercent ?? 0, snap.CpuPackageTemp ?? 0);

                RefreshAssistantInsights();
                return;
            }
        }
        else
        {
            HardwareSubsystemStatusText = Loc["SensorStatusUnavailable"];
        }

        CpuModel = unavailable;
        CpuTemp = unavailable;
        CpuLoad = unavailable;
        CpuLoadPercent = 0;
        CpuClock = unavailable;
        CpuPower = unavailable;
        GpuModel = unavailable;
        GpuTemp = unavailable;
        GpuHotspot = unavailable;
        GpuHotspotDeltaText = unavailable;
        GpuLoad = unavailable;
        GpuLoadPercent = 0;
        GpuClock = unavailable;
        GpuPower = unavailable;
        GpuFanRpm = unavailable;
        GpuVram = unavailable;
        GpuThermalDiagnosisText = unavailable;
        RamUsage = unavailable;
        RamUsagePercent = 0;
        RamTotalAndTypeText = unavailable;
        RamUsedText = unavailable;
        RamAvailableText = unavailable;
        OnPropertyChanged(nameof(RamUsageWithPercent));
        UpdatePrimaryDiskAndProfileTelemetry(null, null);
        OverallHealthScore = "N/D";
        RefreshAssistantInsights();
        }
        finally
        {
            _telemetryRefreshGate.Release();
        }
    }
    private void UpdatePrimaryDiskAndProfileTelemetry(string? gpuName, double? ramTotalGb)
    {
        CpuCoresThreadsText = $"{Math.Max(1, Environment.ProcessorCount / 2)} Cores / {Environment.ProcessorCount} Threads";

        try
        {
            var sysRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(sysRoot);
            if (drive.IsReady && drive.TotalSize > 0)
            {
                double totalGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                double freeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                double usedPercent = ((totalGb - freeGb) / totalGb) * 100.0;
                PrimaryDiskModel = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? $"Unidade do Sistema ({drive.Name.TrimEnd('\\')}) · {drive.DriveFormat}"
                    : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')}) · {drive.DriveFormat}";
                PrimaryDiskSmartStatus = usedPercent < 90.0 ? "Saudável (Online)" : "Atenção (Pouco Espaço)";
                PrimaryDiskFreeSpaceText = $"{freeGb:F0} GB";
                PrimaryDiskTotalSpaceText = $"{totalGb:F0} GB";
                PrimaryDiskUsagePercent = Math.Round(usedPercent, 1);
            }
        }
        catch
        {
            // Keep safe defaults if drive inspection is restricted
        }

        if (!string.IsNullOrWhiteSpace(gpuName) &&
            (gpuName.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
             gpuName.Contains("RX ", StringComparison.OrdinalIgnoreCase) ||
             gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
             gpuName.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
             gpuName.Contains("Arc", StringComparison.OrdinalIgnoreCase)))
        {
            DetectedProfileDisplay = (ramTotalGb ?? 0) >= 64 ? "Workstation" : "Gamer";
        }
        else
        {
            DetectedProfileDisplay = "Desktop";
        }
    }

    private void AppendTelemetryChartSample(double cpuLoad, double gpuLoad, double cpuTemp)
    {
        _telemetryHistory.Add((Math.Clamp(cpuLoad, 0, 100), Math.Clamp(gpuLoad, 0, 100), Math.Clamp(cpuTemp, 0, 110)));
        if (_telemetryHistory.Count > 30)
        {
            _telemetryHistory.RemoveAt(0);
        }

        const double chartWidth = 500.0;
        const double chartHeight = 140.0;
        var cpuPts = new PointCollection();
        var gpuPts = new PointCollection();
        var tempPts = new PointCollection();

        int count = _telemetryHistory.Count;
        for (int i = 0; i < count; i++)
        {
            double x = count == 1 ? chartWidth : (i * (chartWidth / Math.Max(1, count - 1)));
            double cpuY = chartHeight - (_telemetryHistory[i].CpuLoad / 100.0 * (chartHeight - 16.0)) - 8.0;
            double gpuY = chartHeight - (_telemetryHistory[i].GpuLoad / 100.0 * (chartHeight - 16.0)) - 8.0;
            double tempY = chartHeight - (_telemetryHistory[i].CpuTemp / 110.0 * (chartHeight - 16.0)) - 8.0;

            cpuPts.Add(new Point(Math.Round(x, 1), Math.Round(cpuY, 1)));
            gpuPts.Add(new Point(Math.Round(x, 1), Math.Round(gpuY, 1)));
            tempPts.Add(new Point(Math.Round(x, 1), Math.Round(tempY, 1)));
        }

        CpuLoadChartPoints = cpuPts;
        GpuLoadChartPoints = gpuPts;
        CpuTempChartPoints = tempPts;
        HasTelemetryHistory = count > 0;
    }

    private void RebuildFrametimeChartPoints()
    {
        const double chartWidth = 500.0;
        const double chartHeight = 130.0;
        var pts = new PointCollection();
        int count = RecentFrametimeSamples.Count;
        if (count > 0)
        {
            double maxFt = Math.Max(33.3, RecentFrametimeSamples.Max());
            for (int i = 0; i < count; i++)
            {
                double x = count == 1 ? chartWidth : (i * (chartWidth / Math.Max(1, count - 1)));
                double y = chartHeight - (Math.Clamp(RecentFrametimeSamples[i], 0, maxFt) / maxFt * (chartHeight - 16.0)) - 8.0;
                pts.Add(new Point(Math.Round(x, 1), Math.Round(y, 1)));
            }
        }
        FrametimeChartPoints = pts;
        HasFrametimeHistory = count > 0;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
