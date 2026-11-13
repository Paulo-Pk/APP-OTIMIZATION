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

public record GameTargetPreset(string DisplayName, string GameName, string Host, int Port);

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
    string Description,
    string IconUrl = "",
    string Source = "winget",
    bool RequiresAdminUac = false
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
    private string _inspectionState = "Não Verificado";
    private string _currentValueText = "N/D";
    private string _registryTypeText = "N/D";
    private string _diagnosticDetailText = "Aguardando leitura do Registro.";
    private bool _canRestore;

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
        set
        {
            _isApplied = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public string VerificationState
    {
        get => _verificationState;
        set
        {
            _verificationState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanApply));
        }
    }

    // Applying over a custom, unknown, unreadable, or edition-dependent value could
    // overwrite a user's configuration. Only allow application after a confirmed
    // read that the current value is the known inactive/default value.
    public bool CanApply =>
        !IsApplied &&
        (VerificationState.StartsWith("Padrão/inativo — leitura confirmada", StringComparison.OrdinalIgnoreCase) ||
         VerificationState.StartsWith("Default/inactive — read-back confirmed", StringComparison.OrdinalIgnoreCase));

    public string InspectionState
    {
        get => _inspectionState;
        set { _inspectionState = value; OnPropertyChanged(); }
    }

    public bool CanRestore
    {
        get => _canRestore;
        set { _canRestore = value; OnPropertyChanged(); }
    }

    public string CurrentValueText
    {
        get => _currentValueText;
        set { _currentValueText = value; OnPropertyChanged(); }
    }

    public string RegistryTypeText
    {
        get => _registryTypeText;
        set { _registryTypeText = value; OnPropertyChanged(); }
    }

    public string DiagnosticDetailText
    {
        get => _diagnosticDetailText;
        set { _diagnosticDetailText = value; OnPropertyChanged(); }
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
    private string _realFreedText = "Nenhuma limpeza executada nesta sessão";
    private int _scannedFilesCount;
    private string _estimatedBytesFormatted = "Não simulado";
    private string _lastOperationStatus = "Aguardando simulação";

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

    public int ScannedFilesCount
    {
        get => _scannedFilesCount;
        set { _scannedFilesCount = value; OnPropertyChanged(); }
    }

    public string EstimatedBytesFormatted
    {
        get => _estimatedBytesFormatted;
        set { _estimatedBytesFormatted = value; OnPropertyChanged(); }
    }

    public string LastOperationStatus
    {
        get => _lastOperationStatus;
        set { _lastOperationStatus = value; OnPropertyChanged(); }
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
    private readonly SystemDoctorService _systemDoctor = new();
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
    private bool _isRefreshingTweakStates;
    private bool _isApplyingSafeBoostProfile;
    private string _tweakStatesSummaryText = "Estados do Registro ainda não atualizados nesta sessão.";
    private bool _isRefreshingGameConnections;
    private string _gameConnectionsStatusText = "A biblioteca ainda não foi atualizada nesta sessão.";

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
    private bool _isCleanerBusy;

    // Network Lab properties
    private string _networkSummaryText = "Clique em Testar conexão para verificar a resposta de serviços na internet e a resolução de endereços.";
    private string _externalThroughputText = "Faça o teste de download para medir a velocidade da sua conexão.";

    // Game Network Accelerator properties
    private string _acceleratorGameName = "Counter-Strike 2";
    private string _acceleratorTargetHost = "steampowered.com";
    private int _acceleratorTargetPort = 443;
    private GameTargetPreset? _selectedGameTarget = new("Counter-Strike 2 — Steam", "Counter-Strike 2", "steampowered.com", 443);
    private string _routeVerdictText = "Execute a avaliação multi-amostra para comparar a Rota Direta com nós intermediários.";
    private string _wfpSelectiveRoutingNoticeText = GameNetworkAcceleratorService.GetSelectiveRoutingCapabilityReport().WfpLimitationExplanationPt;
    private string _wireGuardStatusText = "Verifique a instalação do WireGuard for Windows e audite a configuração AllowedIPs.";
    private string _wireGuardConfigAuditText = "Nenhuma configuração WireGuard validada nesta sessão.";

    // PresentMon properties
    private string _presentMonStatusText = "Verifique a instalação do PresentMon e selecione um processo.";
    private RunningProcessInfo? _selectedProcess;
    private string _manualProcessName = string.Empty;
    private int _captureDurationSeconds = 10;
    private bool _isCapturingPresentMon;
    private string _presentMonMetricsSummary = "Nenhuma sessão capturada.";

    // Diagnostics & Security properties
    private string _securityPostureText = "Verificando DPAPI, ACL NTFS, HMAC-SHA256, SQLite e privilégios UAC...";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TweakItemViewModel> CatalogTweaks { get; } = new();
    public ObservableCollection<SensorDataPoint> AllHardwareSensors { get; } = new();
    public ObservableCollection<SensorDataPoint> GpuHardwareSensors { get; } = new();
    public ObservableCollection<StorageTelemetryPoint> StorageDrives { get; } = new();
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
    public ObservableCollection<InstalledGameEntry> InstalledGames { get; } = new();
    public ObservableCollection<GameTargetPreset> GameTargetPresets { get; } = new()
    {
        new("Counter-Strike 2 — Steam", "Counter-Strike 2", "steampowered.com", 443),
        new("VALORANT — Riot Games", "VALORANT", "riotgames.com", 443),
        new("Fortnite — Epic Games", "Fortnite", "epicgames.com", 443),
        new("Apex Legends — EA", "Apex Legends", "ea.com", 443),
        new("League of Legends — Riot Games", "League of Legends", "leagueoflegends.com", 443),
        new("Rainbow Six Siege — Ubisoft", "Rainbow Six Siege", "ubisoft.com", 443),
        new("Rocket League — Epic Games", "Rocket League", "rocketleague.com", 443),
        new("Minecraft — Mojang/Microsoft", "Minecraft", "minecraft.net", 443)
    };
    public ObservableCollection<TracerouteHopResult> TracerouteHops { get; } = new();
    public ObservableCollection<RelayNodeCandidate> RelayNodes { get; } = new();
    public ObservableCollection<ModuleCapabilityMatrixItem> ModuleCapabilityMatrix { get; } = new();
    public ObservableCollection<SystemDoctorFinding> SystemDoctorFindings { get; } = new();
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
    private string _benchmarkResultSummary = "Execute os testes de CPU, memória e placa de vídeo separadamente.";
    private string _benchmarkCpuScoreText = "—";
    private string _benchmarkCpuTimeText = "—";
    private string _benchmarkMemoryBandwidthText = "—";
    private string _benchmarkGpuScoreText = "Ainda não testada";
    private string _benchmarkGpuStatusText = "Teste Direct3D do Windows disponível quando o WinSAT estiver instalado.";
    private string _softwareInstallStatusText = "Escolha um aplicativo e confirme antes de instalar.";
    private string _driverScanStatusText = "Ainda não foi realizada uma leitura de drivers.";
    private string _peripheralCadenceSummary = "Clique na área de teste para conferir as teclas e os botões do mouse.";
    private int _peripheralKeyPressCount;
    private string _peripheralLastKey = "—";
    private int _peripheralMouseClickCount;
    private int _peripheralMouseMoveCount;
    private int _peripheralMouseWheelCount;
    private string _peripheralLastMouseButton = "—";
    private DateTime _lastPeripheralMouseMoveUtc = DateTime.MinValue;

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
    public string BenchmarkCpuScoreText { get => _benchmarkCpuScoreText; set { _benchmarkCpuScoreText = value; OnPropertyChanged(); } }
    public string BenchmarkCpuTimeText { get => _benchmarkCpuTimeText; set { _benchmarkCpuTimeText = value; OnPropertyChanged(); } }
    public string BenchmarkMemoryBandwidthText { get => _benchmarkMemoryBandwidthText; set { _benchmarkMemoryBandwidthText = value; OnPropertyChanged(); } }
    public string BenchmarkGpuScoreText { get => _benchmarkGpuScoreText; set { _benchmarkGpuScoreText = value; OnPropertyChanged(); } }
    public string BenchmarkGpuStatusText { get => _benchmarkGpuStatusText; set { _benchmarkGpuStatusText = value; OnPropertyChanged(); } }
    public string SoftwareInstallStatusText { get => _softwareInstallStatusText; set { _softwareInstallStatusText = value; OnPropertyChanged(); } }
    public string DriverScanStatusText { get => _driverScanStatusText; set { _driverScanStatusText = value; OnPropertyChanged(); } }
    public string PeripheralCadenceSummary { get => _peripheralCadenceSummary; set { _peripheralCadenceSummary = value; OnPropertyChanged(); } }
    public int PeripheralKeyPressCount { get => _peripheralKeyPressCount; private set { _peripheralKeyPressCount = value; OnPropertyChanged(); } }
    public string PeripheralLastKey { get => _peripheralLastKey; private set { _peripheralLastKey = value; OnPropertyChanged(); } }
    public int PeripheralMouseClickCount { get => _peripheralMouseClickCount; private set { _peripheralMouseClickCount = value; OnPropertyChanged(); } }
    public int PeripheralMouseMoveCount { get => _peripheralMouseMoveCount; private set { _peripheralMouseMoveCount = value; OnPropertyChanged(); } }
    public int PeripheralMouseWheelCount { get => _peripheralMouseWheelCount; private set { _peripheralMouseWheelCount = value; OnPropertyChanged(); } }
    public string PeripheralLastMouseButton { get => _peripheralLastMouseButton; private set { _peripheralLastMouseButton = value; OnPropertyChanged(); } }

    public void RecordPeripheralKey(string key)
    {
        PeripheralKeyPressCount++;
        PeripheralLastKey = key;
        PeripheralCadenceSummary = $"Teclado respondendo: {PeripheralKeyPressCount} tecla(s) detectada(s). Última tecla: {key}.";
    }

    public void RecordPeripheralMouseButton(string button)
    {
        PeripheralMouseClickCount++;
        PeripheralLastMouseButton = button;
    }

    public void RecordPeripheralMouseMove()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastPeripheralMouseMoveUtc).TotalMilliseconds < 80) return;
        _lastPeripheralMouseMoveUtc = now;
        PeripheralMouseMoveCount++;
    }

    public void RecordPeripheralMouseWheel() => PeripheralMouseWheelCount++;

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

    public bool CanRefreshTweakStates => !_isRefreshingTweakStates;
    public string RefreshTweakStatesButtonText => _isRefreshingTweakStates ? "CONSULTANDO REGISTRO..." : "ATUALIZAR ESTADOS";
    public string TweakStatesSummaryText
    {
        get => _tweakStatesSummaryText;
        set { _tweakStatesSummaryText = value; OnPropertyChanged(); }
    }

    public bool CanRefreshGameConnections => !_isRefreshingGameConnections;
    public string RefreshGameConnectionsButtonText =>
        _isRefreshingGameConnections ? "ESCANEANDO JOGOS..." : "ESCANEAR JOGOS E SOCKETS";

    public string GameConnectionsStatusText
    {
        get => _gameConnectionsStatusText;
        set { _gameConnectionsStatusText = value; OnPropertyChanged(); }
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

    public bool CanRunCleanerOperation => !_isCleanerBusy;

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

    private string _systemDoctorSummaryText = "Clique em Executar Diagnóstico para verificar temperaturas, memória, armazenamento e eventos recentes do Windows.";
    public string SystemDoctorSummaryText
    {
        get => _systemDoctorSummaryText;
        set { _systemDoctorSummaryText = value; OnPropertyChanged(); }
    }

    public string SecurityPostureText
    {
        get => _securityPostureText;
        set { _securityPostureText = value; OnPropertyChanged(); }
    }

    public GameTargetPreset? SelectedGameTarget
    {
        get => _selectedGameTarget;
        set
        {
            _selectedGameTarget = value;
            if (value is not null)
            {
                AcceleratorGameName = value.GameName;
                AcceleratorTargetHost = value.Host;
                AcceleratorTargetPort = value.Port;
            }
            OnPropertyChanged();
        }
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
    public ICommand RefreshTweakStatesCommand { get; }
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
    public ICommand RunGpuBenchmarkCommand { get; }
    public ICommand InstallSoftwareCommand { get; }
    public ICommand MeasureInputCadenceCommand { get; }
    public ICommand GenerateDiagnosticInsightsCommand { get; }

    private static string? GetFriendlyTweakName(string tweakId)
    {
        return tweakId switch
        {
            "perf-mmcss" => "Ajustar prioridade de tarefas de áudio e vídeo",
            "perf-game-dvr" => "Desligar gravação automática de jogos",
            "perf-hpet" => "Ajustar resposta do processador",
            "perf-tcp-nagle" => "Ajustar envio de pequenos dados pela rede",
            "start-fast-boot" => "Desligar inicialização rápida do Windows",
            "start-edge-preload" => "Evitar que o Edge carregue antes de abrir",
            "bg-telemetry" => "Reduzir dados de diagnóstico enviados",
            "bg-cortana-news" => "Ocultar notícias da barra de tarefas",
            "power-ultimate-plan" => "Usar plano de energia de alto desempenho",
            "power-pcie-link" => "Ajustar economia de energia da placa PCIe",
            "serv-sysmain" => "Mudar o serviço de pré-carregamento para manual",
            "serv-spooler" => "Mudar o serviço de impressão para manual",
            "mem-standby-clean" => "Ajustar o uso da memória em espera",
            "mem-lock-executive" => "Manter partes do Windows na memória",
            "game-hags" => "Agendamento de tarefas da placa de vídeo",
            "game-fso-override" => "Ajustar compatibilidade de tela cheia",
            "sys-delivery-p2p" => "Impedir compartilhamento de atualizações",
            "sys-transparency" => "Desligar efeitos de transparência",
            "perf-fso-flags" => "Ajustar compatibilidade de tela cheia",
            "storage-ntfs-lastaccess" => "Reduzir registros de último acesso a arquivos",
            "net-tcp-autotuning" => "Ajustar controle automático da conexão",
            "priv-advertising-id" => "Desativar identificador de anúncios",
            "maint-startup-delay" => "Reduzir atraso ao abrir programas",
            _ => null
        };
    }

    private async Task RefreshAllTweakStatesAsync()
    {
        if (_isRefreshingTweakStates)
            return;

        _isRefreshingTweakStates = true;
        OnPropertyChanged(nameof(CanRefreshTweakStates));
        OnPropertyChanged(nameof(RefreshTweakStatesButtonText));
        TweakStatesSummaryText = "Consultando o Registro em modo somente leitura...";
        StatusMessage = "CONSULTANDO REGISTRO... Nenhuma alteração será gravada.";

        try
        {
            var states = await Task.Run(() =>
                RegistryOptimizer.Catalog.Keys
                    .Select(id =>
                    {
                        RegistryTweakInspectionResult inspection = RegistryOptimizer.InspectCatalogTweak(id);
                        bool canRestore = string.Equals(inspection.State, "Ativo", StringComparison.OrdinalIgnoreCase) &&
                            RegistryStateLedger.ResolveTrustedRestoreEntry(id).IsValid;
                        return (TweakId: id, Inspection: inspection, CanRestore: canRestore);
                    })
                    .ToArray());

            var byId = states.ToDictionary(x => x.TweakId, x => x, StringComparer.OrdinalIgnoreCase);
            foreach (var tweak in CatalogTweaks)
            {
                if (!byId.TryGetValue(tweak.TweakId, out var stateResult))
                    continue;

                RegistryTweakInspectionResult inspection = stateResult.Inspection;
                tweak.CanRestore = stateResult.CanRestore;
                tweak.IsApplied = string.Equals(inspection.State, "Ativo", StringComparison.OrdinalIgnoreCase);
                tweak.InspectionState = inspection.State;
                tweak.VerificationState = GetTweakStateDisplay(inspection.State, Loc.CurrentCulture == "pt-BR");
                tweak.CurrentValueText = inspection.CurrentValue ?? "—";
                tweak.RegistryTypeText = inspection.ActualValueKind?.ToString() ?? "N/D";
                tweak.DiagnosticDetailText = inspection.DiagnosticDetail ??
                    (inspection.AbsenceKind ?? (inspection.IsUndeterminable ? "Estado não determinável." : "Leitura confirmada."));
            }

            int active = states.Count(x => x.Inspection.State == "Ativo");
            int inactive = states.Count(x => x.Inspection.State == "Inativo");
            int noChange = states.Count(x => x.Inspection.State == "Sem Alteração Necessária");
            int custom = states.Count(x => x.Inspection.State == "Personalizado");
            int denied = states.Count(x => x.Inspection.State == "Acesso Negado");
            int readErrors = states.Count(x => x.Inspection.State == "Erro de Leitura");
            int wrongType = states.Count(x => x.Inspection.State == "Tipo Incorreto");
            int undetermined = states.Count(x => x.Inspection.State == "Estado Não Determinável" || x.Inspection.State == "Padrão Dependente");
            int missingKeys = states.Count(x => x.Inspection.AbsenceKind == "ChaveAusente");
            int missingValuesInEmptyKeys = states.Count(x => x.Inspection.AbsenceKind == "ValorAusenteChaveVazia");
            int missingValuesInPopulatedKeys = states.Count(x => x.Inspection.AbsenceKind == "ValorAusenteChaveComConteudo");

            TweakStatesSummaryText = Loc.CurrentCulture == "pt-BR"
                ? $"Leitura concluída: {states.Length} ajustes — {active} ativos, {inactive} inativos, {noChange} sem alteração necessária, {custom} personalizados, {wrongType} com tipo incorreto, {denied} sem acesso, {readErrors} erros de leitura e {undetermined} não determináveis. Ausências: {missingKeys} chaves inexistentes, {missingValuesInEmptyKeys} valores ausentes em chaves vazias e {missingValuesInPopulatedKeys} valores ausentes em chaves com outros dados."
                : $"Read complete: {states.Length} tweaks — {active} active, {inactive} inactive, {noChange} no change needed, {custom} custom, {wrongType} wrong type, {denied} access denied, {readErrors} read errors and {undetermined} undetermined. Missing entries: {missingKeys} keys, {missingValuesInEmptyKeys} values in empty keys and {missingValuesInPopulatedKeys} values in populated keys.";
            OnPropertyChanged(nameof(AppliedTweaksCount));
            OnPropertyChanged(nameof(AppliedTweaksBadgeText));
        }
        catch (Exception ex)
        {
            TweakStatesSummaryText = $"Não foi possível atualizar os estados: {ex.Message}";
            StatusMessage = TweakStatesSummaryText;
        }
        finally
        {
            _isRefreshingTweakStates = false;
            OnPropertyChanged(nameof(CanRefreshTweakStates));
            OnPropertyChanged(nameof(RefreshTweakStatesButtonText));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private static string BuildGpuThermalUserMessage(GpuThermalEvaluationResult evaluation, bool isPortuguese)
    {
        if (!isPortuguese)
            return evaluation.Message;

        string status = evaluation.Status switch
        {
            GpuThermalHealth.SensorUnavailable => "Temperatura não disponível",
            GpuThermalHealth.Excellent => "Temperatura muito boa",
            GpuThermalHealth.Normal => "Temperatura dentro do esperado",
            GpuThermalHealth.Warning => "Atenção: a placa está aquecendo bastante",
            GpuThermalHealth.Critical => "Alerta: temperatura muito alta",
            _ => "Não foi possível avaliar a temperatura"
        };

        string readings = evaluation.HotspotTemp > 0
            ? $"Temperatura principal: {evaluation.CoreTemp:F1} °C. Ponto mais quente: {evaluation.HotspotTemp:F1} °C. Diferença: {evaluation.Delta:F1} °C."
            : $"Temperatura principal: {evaluation.CoreTemp:F1} °C.";

        string limitation = evaluation.IsEvaluationLimited
            ? " O Windows/driver não disponibilizou todos os sensores; a avaliação está incompleta."
            : string.Empty;

        return $"{status}. {readings}{limitation}";
    }

    private static void UpdateTweakInspection(
        TweakItemViewModel tweak,
        RegistryTweakInspectionResult inspection,
        bool isPortuguese)
    {
        tweak.InspectionState = inspection.State;
        tweak.IsApplied = string.Equals(inspection.State, "Ativo", StringComparison.OrdinalIgnoreCase);
        tweak.CanRestore = tweak.IsApplied && RegistryStateLedger.ResolveTrustedRestoreEntry(tweak.TweakId).IsValid;
        tweak.VerificationState = GetTweakStateDisplay(inspection.State, isPortuguese);
        tweak.CurrentValueText = inspection.CurrentValue ?? "—";
        tweak.RegistryTypeText = inspection.ActualValueKind?.ToString() ?? "N/D";
        tweak.DiagnosticDetailText = inspection.DiagnosticDetail ??
            (inspection.AbsenceKind ?? (inspection.IsUndeterminable ? "Estado não determinável." : "Leitura confirmada."));
    }

    private static string GetTweakStateDisplay(string state, bool isPortuguese)
    {
        return state switch
        {
            "Ativo" => isPortuguese ? "Ativo — leitura confirmada" : "Active — read-back confirmed",
            "Sem Alteração Necessária" => isPortuguese ? "Sem alteração necessária — valor recomendado igual ao padrão" : "No change needed — recommended value matches the default",
            "Inativo" => isPortuguese ? "Padrão/inativo — leitura confirmada" : "Default/inactive — read-back confirmed",
            "Personalizado" or "Parcial" => isPortuguese ? "Personalizado — diferente do recomendado" : "Custom — differs from recommendation",
            "Chave Ausente" or "Valor Ausente" => isPortuguese ? "Valor ausente no Registro" : "Registry value is missing",
            "Acesso Negado" => isPortuguese ? "Acesso negado — requer permissão" : "Access denied — permission required",
            "Tipo Incorreto" => isPortuguese ? "Tipo incorreto — esperado diferente do encontrado" : "Wrong Registry type — expected type mismatch",
            "Estado Não Determinável" => isPortuguese ? "Estado não determinável — valor ausente sem padrão confirmado" : "Undetermined — absent value has no confirmed default",
            "Não Suportado" => isPortuguese ? "Ajuste não reconhecido pelo catálogo" : "Tweak is not in the supported catalog",
            "Não Verificado" => isPortuguese ? "Aguardando primeira leitura do Registro..." : "Waiting for initial Registry inspection...",
            "Erro de Aplicação" => isPortuguese ? "Falha ao aplicar — confira o diagnóstico" : "Apply failed — inspect the diagnostic",
            "Erro de Restauração" => isPortuguese ? "Falha ao restaurar — confira o diagnóstico" : "Restore failed — inspect the diagnostic",
            "Padrão Dependente" => isPortuguese ? "Padrão depende da edição/configuração do Windows" : "Default depends on Windows edition/configuration",
            "Erro de Leitura" => isPortuguese ? "Erro de leitura — estado não determinável" : "Read error — state undetermined",
            "Não Disponível Neste Sistema" => isPortuguese ? "Consulta disponível somente no Windows" : "Inspection is available on Windows only",
            _ => isPortuguese ? "Não foi possível consultar" : "Unable to inspect"
        };
    }

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
            // Keep startup responsive: create the rows first and perform one read-only
            // catalog inspection asynchronously after the view model is fully initialized.
            CatalogTweaks.Add(new TweakItemViewModel
            {
                TweakId = def.TweakId,
                NamePt = GetFriendlyTweakName(def.TweakId) ?? def.Description,
                NameEn = def.DescriptionEn,
                Name = Loc.CurrentCulture == "pt-BR" ? (GetFriendlyTweakName(def.TweakId) ?? def.Description) : def.DescriptionEn,
                Category = def.Category,
                RegistryPath = $"{def.Hive}\\{def.SubKeyPath}",
                ValueName = def.ValueName,
                ValueKind = def.ValueKind.ToString(),
                RecommendedValue = def.RecommendedValue,
                RiskLevel = def.RiskLevel,
                IsApplied = false,
                CanRestore = false,
                InspectionState = "Não Verificado",
                VerificationState = GetTweakStateDisplay("Não Verificado", Loc.CurrentCulture == "pt-BR"),
                CurrentValueText = "N/D",
                RegistryTypeText = "N/D",
                DiagnosticDetailText = Loc.CurrentCulture == "pt-BR"
                    ? "A consulta inicial será executada em segundo plano. Nenhuma alteração será gravada."
                    : "Initial inspection will run in the background. No changes will be written."
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

        RefreshTweakStatesCommand = new RelayCommand(async _ => await RefreshAllTweakStatesAsync(), _ => CanRefreshTweakStates);

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
            await RunSystemDoctorAuditAsync();
            await RefreshTelemetryAndStatusAsync();
        });

        ApplyTweakCommand = new RelayCommand(async param =>
        {
            // Do not create another ledger entry when the recommended value is already active.
            if (param is TweakItemViewModel tweak && !tweak.IsApplied)
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

                    var readBack = RegistryOptimizer.InspectCatalogTweak(tweak.TweakId);
                    UpdateTweakInspection(tweak, readBack, Loc.CurrentCulture == "pt-BR");
                    OnPropertyChanged(nameof(AppliedTweaksCount));
                    OnPropertyChanged(nameof(AppliedTweaksBadgeText));
                    CommandManager.InvalidateRequerySuggested();
                    await _databaseService.UpsertOptimizationActionAsync(
                        tweak.TweakId,
                        tweak.Name,
                        tweak.Category,
                        tweak.RegistryPath,
                        tweak.ValueName,
                        "Ledger-DPAPI",
                        tweak.RecommendedValue,
                        isApplied: tweak.IsApplied,
                        backupId: tweak.LastOperationId
                    );
                    StatusMessage = tweak.IsApplied
                        ? $"Ajuste '{tweak.TweakId}' aplicado e confirmado por nova leitura do Registro."
                        : $"O agente informou que aplicou '{tweak.TweakId}', mas a releitura não confirmou o valor recomendado. Estado: {tweak.VerificationState}.";
                }
                else
                {
                    tweak.InspectionState = "Erro de Aplicação";
                    tweak.VerificationState = GetTweakStateDisplay(tweak.InspectionState, Loc.CurrentCulture == "pt-BR");
                    tweak.DiagnosticDetailText = details;
                    StatusMessage = $"Não foi possível aplicar '{tweak.TweakId}': {details}";
                }
            }
        }, param => param is TweakItemViewModel item && item.CanApply);

        RestoreSingleTweakCommand = new RelayCommand(async param =>
        {
            if (param is TweakItemViewModel tweak)
            {
                // Secure backend-only restoration via DPAPI+HMAC ledger or verified snapshot
                // Individual reversal must use the trusted operation ledger for this tweak.
                // A previously selected snapshot is unrelated and must never silently override that history.
                var (ok, details) = await _agentClient.RestoreCatalogTweakAsync(
                    tweak.TweakId,
                    tweak.LastOperationId,
                    snapshotId: null
                );
                RefreshAuditLogs();
                if (ok)
                {
                    var restoredInspection = RegistryOptimizer.InspectCatalogTweak(tweak.TweakId);
                    UpdateTweakInspection(tweak, restoredInspection, Loc.CurrentCulture == "pt-BR");
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
                        isApplied: tweak.IsApplied,
                        backupId: tweak.LastOperationId
                    );
                    StatusMessage = tweak.IsApplied
                        ? $"A restauração de '{tweak.TweakId}' foi executada, mas o valor recomendado ainda aparece no Registro. Verifique o estado original e as permissões."
                        : $"Restauração de '{tweak.TweakId}' executada; releitura do Registro indica que o valor recomendado não está ativo.";
                }
                else
                {
                    tweak.CanRestore = false;
                    tweak.InspectionState = "Erro de Restauração";
                    tweak.VerificationState = GetTweakStateDisplay(tweak.InspectionState, Loc.CurrentCulture == "pt-BR");
                    tweak.DiagnosticDetailText = details;
                    StatusMessage = $"Falha ao reverter '{tweak.TweakId}': {details}";
                }
            }
        }, param => param is TweakItemViewModel item && item.CanRestore);

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

        SimulateDryRunCommand = new RelayCommand(async param =>
        {
            if (_isCleanerBusy) return;

            var targets = param is CleanerCategoryViewModel single
                ? new[] { single }
                : CleanerCategories.ToArray();

            _isCleanerBusy = true;
            OnPropertyChanged(nameof(CanRunCleanerOperation));
            CommandManager.InvalidateRequerySuggested();
            DebloatSummaryText = "Consultando o agente privilegiado e analisando os diretórios autorizados...";

            try
            {
                var (scanOk, scanDetails) = await _agentClient.ScanCleanerCategoriesAsync();
                if (!scanOk)
                    throw new InvalidOperationException(scanDetails);

                using var scanDocument = JsonDocument.Parse(scanDetails);
                if (!scanDocument.RootElement.TryGetProperty("categories", out var categoriesElement) ||
                    categoriesElement.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException("O agente não retornou a lista de categorias da simulação.");

                long totalEstimated = 0;
                int totalFiles = 0;
                int failedCategories = 0;
                foreach (var cat in targets)
                {
                    var report = categoriesElement.EnumerateArray().FirstOrDefault(item =>
                        item.TryGetProperty("categoryKey", out var key) &&
                        string.Equals(key.GetString(), cat.CategoryKey, StringComparison.OrdinalIgnoreCase));

                    if (report.ValueKind != JsonValueKind.Object)
                    {
                        cat.LastOperationStatus = "Sem resultado do agente";
                        failedCategories++;
                        continue;
                    }

                    long estimatedBytes = report.TryGetProperty("estimatedBytes", out var bytesEl) && bytesEl.TryGetInt64(out var bytes)
                        ? bytes : 0;
                    int candidateFiles = report.TryGetProperty("candidateFilesCount", out var filesEl) && filesEl.TryGetInt32(out var files)
                        ? files : 0;
                    int skippedLinks = report.TryGetProperty("skippedSymlinksCount", out var linksEl) && linksEl.TryGetInt32(out var links)
                        ? links : 0;
                    bool success = report.TryGetProperty("success", out var successEl) && successEl.ValueKind == JsonValueKind.True;
                    totalEstimated += estimatedBytes;
                    totalFiles += candidateFiles;

                    double mb = estimatedBytes / (1024.0 * 1024.0);
                    cat.ScannedFilesCount = candidateFiles;
                    cat.EstimatedBytesFormatted = $"{mb:F2} MB";
                    cat.DryRunEstimateText = $"Estimativa: {mb:F2} MB ({candidateFiles} arquivos; {skippedLinks} links ignorados)";
                    cat.LastOperationStatus = success ? "Simulação concluída pelo agente" : "Leitura parcial / falha";
                    if (!success)
                    {
                        failedCategories++;
                        if (report.TryGetProperty("errors", out var errorsEl) && errorsEl.ValueKind == JsonValueKind.Array &&
                            errorsEl.GetArrayLength() > 0)
                            cat.LastOperationStatus += $": {errorsEl[0].GetString()}";
                    }
                }

                DebloatSummaryText = $"Simulação concluída pelo agente — SOMENTE ESTIMATIVA: {(totalEstimated / (1024.0 * 1024.0)):F2} MB em {totalFiles} arquivos candidatos. " +
                    $"{failedCategories} categoria(s) com leitura parcial/falha. Nenhum arquivo foi excluído.";
            }
            catch (Exception ex)
            {
                DebloatSummaryText = $"Falha ao analisar pelo agente privilegiado: {ex.Message}";
            }
            finally
            {
                _isCleanerBusy = false;
                OnPropertyChanged(nameof(CanRunCleanerOperation));
                CommandManager.InvalidateRequerySuggested();
            }
        }, _ => CanRunCleanerOperation);

        PurgeCategoryCommand = new RelayCommand(async param =>
        {
            if (_isCleanerBusy) return;

            if (!ConfirmCleanerDeletion)
            {
                DebloatSummaryText = "Ação bloqueada: marque a confirmação explícita antes de excluir arquivos.";
                return;
            }

            if (param is not CleanerCategoryViewModel cat) return;

            _isCleanerBusy = true;
            OnPropertyChanged(nameof(CanRunCleanerOperation));
            CommandManager.InvalidateRequerySuggested();
            DebloatSummaryText = $"Solicitando ao agente privilegiado a limpeza autorizada de '{cat.CategoryKey}'...";

            try
            {
                var (requestOk, responseText) = await _agentClient.PurgeCleanerCategoryAsync(
                    cat.CategoryKey,
                    confirmUpdateCache: string.Equals(cat.CategoryKey, "update-cache", StringComparison.OrdinalIgnoreCase) &&
                        ConfirmCleanerDeletion);
                if (!requestOk)
                    throw new InvalidOperationException(responseText);

                using var report = JsonDocument.Parse(responseText);
                var root = report.RootElement;
                bool success = root.TryGetProperty("success", out var successEl) && successEl.ValueKind == JsonValueKind.True;
                bool partial = root.TryGetProperty("partialSuccess", out var partialEl) && partialEl.ValueKind == JsonValueKind.True;
                long freedBytes = root.TryGetProperty("freedBytes", out var bytesEl) && bytesEl.TryGetInt64(out var bytes) ? bytes : 0;
                int deletedFiles = root.TryGetProperty("deletedFilesCount", out var deletedEl) && deletedEl.TryGetInt32(out var deleted) ? deleted : 0;
                int skippedLocked = root.TryGetProperty("skippedLockedCount", out var lockedEl) && lockedEl.TryGetInt32(out var locked) ? locked : 0;
                int skippedLinks = root.TryGetProperty("skippedSymlinksCount", out var linksEl) && linksEl.TryGetInt32(out var links) ? links : 0;
                string status = root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? "Failed" : "Failed";
                double freedMb = freedBytes / (1024.0 * 1024.0);

                cat.RealFreedText = $"Liberado real: {freedMb:F2} MB ({deletedFiles} arquivos removidos; {skippedLocked} em uso; {skippedLinks} links preservados)";
                cat.LastOperationStatus = status switch
                {
                    "Completed" => "Limpeza concluída pelo agente",
                    "NothingToClean" => "Nada para limpar",
                    "Partial" => "Limpeza parcial pelo agente",
                    "DirectoryMissing" => "Diretório inexistente",
                    _ => "Falha na limpeza pelo agente"
                };

                CleanerAffectedFilesLog.Clear();
                if (root.TryGetProperty("affectedItemsLog", out var affectedEl) && affectedEl.ValueKind == JsonValueKind.Array)
                    foreach (var item in affectedEl.EnumerateArray())
                        CleanerAffectedFilesLog.Add(item.GetString() ?? item.ToString());
                if (root.TryGetProperty("errors", out var errorsEl) && errorsEl.ValueKind == JsonValueKind.Array)
                    foreach (var error in errorsEl.EnumerateArray())
                        CleanerAffectedFilesLog.Add($"[ERRO] {error.GetString() ?? error.ToString()}");
                if (root.TryGetProperty("warnings", out var warningsEl) && warningsEl.ValueKind == JsonValueKind.Array)
                    foreach (var warning in warningsEl.EnumerateArray())
                        CleanerAffectedFilesLog.Add($"[AVISO] {warning.GetString() ?? warning.ToString()}");

                DebloatSummaryText = status switch
                {
                    "Completed" => $"Limpeza concluída pelo agente em '{cat.CategoryKey}': {freedMb:F2} MB efetivamente liberados em {deletedFiles} arquivos.",
                    "NothingToClean" => $"Nada para limpar em '{cat.CategoryKey}': a pasta existe, mas não há arquivos elegíveis.",
                    "Partial" => $"Limpeza parcial em '{cat.CategoryKey}': {freedMb:F2} MB liberados; {deletedFiles} arquivos removidos, {skippedLocked} em uso e {skippedLinks} links preservados.",
                    "DirectoryMissing" => $"Limpeza não executada em '{cat.CategoryKey}': o diretório autorizado não existe.",
                    _ => $"Falha na limpeza de '{cat.CategoryKey}'. {(root.TryGetProperty("errors", out var err) && err.ValueKind == JsonValueKind.Array && err.GetArrayLength() > 0 ? err[0].GetString() : "Nenhum resultado completo foi confirmado.")}"
                };

                if (!success && !partial && status == "Failed")
                    cat.RealFreedText = "Nenhuma limpeza confirmada pelo agente.";
            }
            catch (Exception ex)
            {
                cat.LastOperationStatus = "Falha na limpeza";
                cat.RealFreedText = "Nenhum resultado confirmado.";
                DebloatSummaryText = $"Falha ao limpar '{cat.CategoryKey}' pelo agente privilegiado: {ex.Message}";
                CleanerAffectedFilesLog.Clear();
                CleanerAffectedFilesLog.Add($"[ERRO] {ex.Message}");
            }
            finally
            {
                _isCleanerBusy = false;
                OnPropertyChanged(nameof(CanRunCleanerOperation));
                CommandManager.InvalidateRequerySuggested();
            }
        }, _ => CanRunCleanerOperation);

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
        RefreshRunningProcesses();

        StartPresentMonCaptureCommand = new RelayCommand(async _ =>
        {
            if (IsCapturingPresentMon) return;
            if (string.IsNullOrWhiteSpace(ManualProcessName))
            {
                PresentMonStatusText = "Selecione um jogo aberto na lista antes de iniciar a captura.";
                return;
            }
            IsCapturingPresentMon = true;
            _presentMonCts = new CancellationTokenSource();

            PresentMonStatusText = $"Capturando '{ManualProcessName}' por {CaptureDurationSeconds}s...";
            try
            {
                var telemetrySamples = new List<HardwareSnapshot>();
                Task<PresentMonCaptureResponse> captureTask = PresentMonSessionParser.CaptureProcessSessionAsync(
                    ManualProcessName,
                    CaptureDurationSeconds,
                    _presentMonCts.Token
                );

                // Sample real hardware telemetry while PresentMon records frame times.
                while (!captureTask.IsCompleted)
                {
                    if (_hardwareMonitor != null)
                    {
                        try { telemetrySamples.Add(await _hardwareMonitor.CaptureSnapshotAsync()); }
                        catch { /* FPS capture remains valid even if a sensor read is temporarily unavailable. */ }
                    }
                    await Task.Delay(1000, _presentMonCts.Token);
                }

                var response = await captureTask;

                if (response.Success && response.Summary != null)
                {
                    double? AverageSensorValue(IEnumerable<float?> values)
                    {
                        var valid = values.Where(value => value.HasValue && !float.IsNaN(value.Value) && !float.IsInfinity(value.Value))
                            .Select(value => (double)value!.Value)
                            .ToArray();
                        return valid.Length > 0 ? valid.Average() : null;
                    }

                    double? avgCpuLoad = AverageSensorValue(telemetrySamples.Select(sample => sample.CpuTotalLoad));
                    double? avgGpuLoad = AverageSensorValue(telemetrySamples.Select(sample => sample.GpuLoadPercent));
                    double? avgCpuTemp = AverageSensorValue(telemetrySamples.Select(sample => sample.CpuPackageTemp));
                    double? avgGpuTemp = AverageSensorValue(telemetrySamples.Select(sample => sample.GpuCoreTemp));

                    PresentMonStatusText = $"Captura concluída! CSV salvo em: {response.CsvFilePath}";
                    PresentMonMetricsSummary =
                        $"FPS Médio: {response.Summary.AverageFps:F1} | 1% Low: {response.Summary.OnePercentLowFps:F1} | " +
                        $"Frametime Médio: {response.Summary.AverageFrametimeMs:F2} ms (σ={response.Summary.FrametimeStandardDeviationMs:F2} ms) | " +
                        $"CPU: {(avgCpuLoad.HasValue ? avgCpuLoad.Value.ToString("F0") + "%" : "N/D")} / {(avgCpuTemp.HasValue ? avgCpuTemp.Value.ToString("F1") + "°C" : "N/D")} | " +
                        $"GPU: {(avgGpuLoad.HasValue ? avgGpuLoad.Value.ToString("F0") + "%" : "N/D")} / {(avgGpuTemp.HasValue ? avgGpuTemp.Value.ToString("F1") + "°C" : "N/D")} | " +
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

        RefreshDriversCommand = new RelayCommand(async _ => await RefreshDriversListAsync());
        _ = RefreshDriversListAsync();
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

        RefreshGameConnectionsCommand = new RelayCommand(async _ =>
        {
            if (_isRefreshingGameConnections) return;

            _isRefreshingGameConnections = true;
            OnPropertyChanged(nameof(CanRefreshGameConnections));
            OnPropertyChanged(nameof(RefreshGameConnectionsButtonText));
            GameConnectionsStatusText = "Lendo catálogos dos launchers e conexões ativas em segundo plano...";

            try
            {
                var scan = await Task.Run(() => (
                    Games: GameNetworkAcceleratorService.DiscoverInstalledGames(),
                    Connections: GameNetworkAcceleratorService.DetectRunningGamesAndConnections(),
                    Nodes: GameNetworkAcceleratorService.GetRegisteredRelayNodes()
                ));

                InstalledGames.Clear();
                foreach (var game in scan.Games)
                    InstalledGames.Add(game);

                DetectedGameSockets.Clear();
                foreach (var connection in scan.Connections)
                    DetectedGameSockets.Add(connection);

                RelayNodes.Clear();
                foreach (var node in scan.Nodes)
                    RelayNodes.Add(node);

                GameConnectionsStatusText =
                    $"Varredura concluída: {InstalledGames.Count} jogo(s) instalado(s), " +
                    $"{DetectedGameSockets.Count} conexão(ões) de jogos e {RelayNodes.Count} nó(s) de rota.";
                StatusMessage = GameConnectionsStatusText;
            }
            catch (Exception ex)
            {
                GameConnectionsStatusText = $"Falha ao atualizar a biblioteca de jogos: {ex.Message}";
                StatusMessage = GameConnectionsStatusText;
            }
            finally
            {
                _isRefreshingGameConnections = false;
                OnPropertyChanged(nameof(CanRefreshGameConnections));
                OnPropertyChanged(nameof(RefreshGameConnectionsButtonText));
                CommandManager.InvalidateRequerySuggested();
            }
        }, _ => CanRefreshGameConnections);

        // Discover installed games automatically in the background at startup.
        RefreshGameConnectionsCommand.Execute(null);

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
            if (_isApplyingSafeBoostProfile)
            {
                StatusMessage = Loc.CurrentCulture == "pt-BR"
                    ? "O perfil seguro já está em execução. Aguarde a conclusão antes de iniciar novamente."
                    : "The safe profile is already running. Wait for it to finish before starting it again.";
                return;
            }

            _isApplyingSafeBoostProfile = true;
            try
            {
                if (_isRefreshingTweakStates)
                {
                    StatusMessage = Loc.CurrentCulture == "pt-BR"
                        ? "Aguarde a consulta do Registro terminar antes de iniciar o perfil em lote."
                        : "Wait for the Registry inspection to finish before starting the batch profile.";
                    return;
                }
    
                // Refresh the read-only Registry baseline immediately before selecting
                // tweaks; never rely on a potentially stale screen state for a batch write.
                await RefreshAllTweakStatesAsync();
    
                var safeTweaks = CatalogTweaks
                    .Where(t => t.CanApply &&
                        (string.Equals(t.RiskLevel, "Safe", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(t.RiskLevel, "Seguro", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(t.RiskLevel, "Low", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(t.RiskLevel, "Baixo", StringComparison.OrdinalIgnoreCase)))
                    .ToList();
    
                if (safeTweaks.Count == 0)
                {
                    StatusMessage = Loc.CurrentCulture == "pt-BR"
                        ? "Nenhum ajuste seguro está elegível agora. Os itens já aplicados ou com estado do Registro não confirmado ficam bloqueados."
                        : "No safe tweak is currently eligible. Already-applied items and items without a confirmed Registry baseline remain blocked.";
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
    
                    // A successful agent response confirms the write request, not the
                    // effective Registry state. Re-read the value before reporting success.
                    var readBack = RegistryOptimizer.InspectCatalogTweak(tweak.TweakId);
                    UpdateTweakInspection(tweak, readBack, Loc.CurrentCulture == "pt-BR");
    
                    if (!tweak.IsApplied)
                    {
                        failedCount++;
                        StatusMessage = Loc.CurrentCulture == "pt-BR"
                            ? $"O ajuste '{tweak.Name}' foi enviado ao agente, mas a releitura não confirmou o valor recomendado. Estado: {tweak.VerificationState}."
                            : $"The agent accepted '{tweak.Name}', but read-back did not confirm the recommended value. State: {tweak.VerificationState}.";
                        continue;
                    }
    
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
                CommandManager.InvalidateRequerySuggested();
    
                StatusMessage = Loc.CurrentCulture == "pt-BR"
                    ? $"Backup confirmado. {appliedCount} otimização(ões) segura(s) aplicada(s) e verificadas; {failedCount} falha(s)."
                    : $"Backup verified. {appliedCount} safe optimization(s) applied and verified; {failedCount} failure(s).";
            }
            finally
            {
                _isApplyingSafeBoostProfile = false;
                CommandManager.InvalidateRequerySuggested();
            }
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
                long iterationsPerSecond = (long)Math.Round(2_500_000.0 / (cpuMs / 1000.0));

                var memSw = System.Diagnostics.Stopwatch.StartNew();
                byte[] buffer = new byte[32 * 1024 * 1024];
                for (int i = 0; i < buffer.Length; i++)
                    buffer[i] = (byte)(i & 0xFF);

                memSw.Stop();
                double bufferSizeGb = buffer.Length / (1024.0 * 1024.0 * 1024.0);
                double memBandwidthGbS = Math.Round(bufferSizeGb / Math.Max(0.0005, memSw.Elapsed.TotalSeconds), 2);
                return (iterationsPerSecond, cpuMs, memBandwidthGbS, checksum: acc);
            });

            BenchmarkResultSummary = Loc.CurrentCulture == "pt-BR"
                ? $"CPU: {result.iterationsPerSecond:N0} iterações/s ({result.cpuMs} ms) · Escrita sequencial em RAM: {result.memBandwidthGbS:F2} GB/s · Teste concluído."
                : $"CPU: {result.iterationsPerSecond:N0} iterations/s ({result.cpuMs} ms) · Sequential RAM write: {result.memBandwidthGbS:F2} GB/s · Test completed.";
            BenchmarkCpuScoreText = result.iterationsPerSecond.ToString("N0") + " it/s";
            BenchmarkCpuTimeText = result.cpuMs.ToString("N0") + " ms";
            BenchmarkMemoryBandwidthText = result.memBandwidthGbS.ToString("F2") + " GB/s";
        });

        RunGpuBenchmarkCommand = new RelayCommand(async _ =>
        {
            BenchmarkGpuStatusText = "Executando teste gráfico Direct3D do Windows...";
            BenchmarkGpuScoreText = "Em andamento";
            try
            {
                string winsatPath = System.IO.Path.Combine(Environment.SystemDirectory, "winsat.exe");
                if (!System.IO.File.Exists(winsatPath))
                    throw new FileNotFoundException("O componente de avaliação gráfica do Windows (WinSAT) não foi encontrado.");

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = winsatPath,
                    Arguments = "graphics -d3d",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = new System.Diagnostics.Process { StartInfo = startInfo };
                if (!process.Start())
                    throw new InvalidOperationException("O Windows não iniciou o teste gráfico.");

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                    throw;
                }
                string output = await outputTask;
                string error = await errorTask;
                string combined = output + Environment.NewLine + error;

                if (process.ExitCode != 0)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                        ? $"O teste gráfico terminou com código {process.ExitCode}."
                        : error.Trim());

                var match = System.Text.RegularExpressions.Regex.Match(
                    combined,
                    @"D3D\s+Batch\s+Performance\s*[:=]?\s*([0-9]+(?:[.,][0-9]+)?)\s*([^\r\n]*)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                BenchmarkGpuScoreText = match.Success
                    ? $"{match.Groups[1].Value} {match.Groups[2].Value.Trim()}".Trim()
                    : "Concluído";

                BenchmarkGpuStatusText = match.Success
                    ? "Teste Direct3D concluído pelo WinSAT. Resultado sintético; compare somente testes feitos nas mesmas condições."
                    : "O WinSAT concluiu o teste Direct3D, mas não forneceu uma pontuação reconhecida. Nenhuma pontuação foi inventada.";
            }
            catch (OperationCanceledException)
            {
                BenchmarkGpuScoreText = "Tempo esgotado";
                BenchmarkGpuStatusText = "O teste gráfico ultrapassou 60 segundos e foi interrompido.";
            }
            catch (Exception ex)
            {
                BenchmarkGpuScoreText = "Indisponível";
                BenchmarkGpuStatusText = $"Não foi possível testar a placa de vídeo: {ex.Message}";
            }
        });

        InstallSoftwareCommand = new RelayCommand(async parameter =>
        {
            if (parameter is not VerifiedSoftwareItem package) return;

            MessageBoxResult confirmation = MessageBox.Show(
                $"Deseja instalar {package.AppName} agora?\n\nOrigem: {package.Source}\nPublicador: {package.Publisher}\n\nO instalador oficial será iniciado pelo WinGet.",
                "Confirmar instalação",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes) return;

            SoftwareInstallStatusText = $"Preparando instalação de {package.AppName}...";
            try
            {
                var progress = new Progress<string>(message => SoftwareInstallStatusText = message);
                var result = await new WingetSoftwareCenterService().InstallPackageAsync(
                    package.WingetId,
                    userConfirmed: true,
                    source: package.Source,
                    progress: progress);

                SoftwareInstallStatusText = result.Success
                    ? $"{package.AppName} foi instalado. Confirme no menu Iniciar."
                    : $"Não foi possível instalar {package.AppName}: {result.Status}. {result.OutputLog}";
            }
            catch (Exception ex)
            {
                SoftwareInstallStatusText = $"Falha ao instalar {package.AppName}: {ex.Message}";
            }
        });

        MeasureInputCadenceCommand = new RelayCommand(_ =>
        {
            PeripheralCadenceSummary = Loc.CurrentCulture == "pt-BR"
                ? $"Tela detectada: {DisplayInfoText}. Clique na área de teste e pressione teclas ou botões do mouse para conferir a resposta."
                : $"Detected display: {DisplayInfoText}. Click the test area and press keys or mouse buttons to check input response.";
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

        // Run the single initial Registry inspection after construction on the UI dispatcher.
        // The actual reads execute inside Task.Run; the continuation updates bound properties safely.
        Application.Current?.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(async () => await RefreshAllTweakStatesAsync()));
    }

    private static string ResolveSoftwareIconUrl(string packageId)
    {
        string domain = packageId switch
        {
            "Discord.Discord" => "discord.com",
            "Valve.Steam" => "store.steampowered.com",
            "EpicGames.EpicGamesLauncher" => "epicgames.com",
            "GOG.Galaxy" => "gog.com",
            "Opera.OperaGX" => "opera.com",
            "9MV0B5HZVK9Z" => "xbox.com",
            "Mozilla.Firefox" => "mozilla.org",
            "Google.Chrome" => "google.com",
            "Brave.Brave" => "brave.com",
            "Telegram.TelegramDesktop" => "telegram.org",
            "OBSProject.OBSStudio" => "obsproject.com",
            "VideoLAN.VLC" => "videolan.org",
            "REALiX.HWiNFO" => "hwinfo.com",
            "Intel.PresentMon" => "intel.com",
            "TechPowerUp.GPU-Z" => "techpowerup.com",
            "CPUID.CPU-Z" => "cpuid.com",
            "Playnite.Playnite" => "playnite.link",
            "Microsoft.PowerToys" => "learn.microsoft.com",
            "WireGuard.WireGuard" => "wireguard.com",
            _ => "microsoft.com"
        };
        return $"https://www.google.com/s2/favicons?domain={domain}&sz=64";
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
                $"winget install --id \"{pkg.PackageId}\" --exact --source {pkg.OfficialSource} --accept-package-agreements --accept-source-agreements",
                pkg.Description,
                ResolveSoftwareIconUrl(pkg.PackageId),
                pkg.OfficialSource,
                pkg.RequiresAdminUac
            ));
        }
    }

    private async Task RunSystemDoctorAuditAsync()
    {
        SystemDoctorSummaryText = "Verificando o computador... Aguarde.";
        SystemDoctorFindings.Clear();

        try
        {
            HardwareSnapshot snapshot;
            if (_hardwareMonitor != null)
            {
                snapshot = await _hardwareMonitor.CaptureSnapshotAsync();
            }
            else
            {
                snapshot = new HardwareSnapshot(
                    DateTime.UtcNow, null, null, null, null, null, null, null, null,
                    Array.Empty<SensorDataPoint>(),
                    MonitorStatus: SensorReadingStatus.ReadingUnavailable,
                    DiagnosticMessage: "Monitor de hardware não disponível.");
            }

            SystemDoctorReport report = await _systemDoctor.RunComprehensiveAuditAsync(snapshot);
            foreach (SystemDoctorFinding finding in report.Findings)
                SystemDoctorFindings.Add(finding);

            SystemDoctorSummaryText = $"{report.ExecutiveSummary} Verificação realizada em {report.AuditedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}. Os resultados abaixo mostram somente os itens que o Windows e os sensores conseguiram verificar.";
            if (report.Findings.Count == 0)
                SystemDoctorSummaryText += " Nenhum alerta foi encontrado nos itens verificados.";
        }
        catch (Exception ex)
        {
            SystemDoctorSummaryText = $"Não foi possível concluir o diagnóstico: {ex.Message}";
            SystemDoctorFindings.Add(new SystemDoctorFinding(
                "Diagnóstico",
                "A verificação foi interrompida",
                DiagnosticSeverityLevel.Alto,
                ex.Message,
                "Falha ao consultar um ou mais recursos do Windows ou sensores.",
                "Feche e abra o aplicativo novamente. Se persistir, envie este erro ao suporte.",
                false));
        }
    }

    private void RefreshAssistantInsights()
    {
        AssistantDiagnosticInsights.Clear();
        AssistantDiagnosticInsights.Add($"Placa de vídeo: {GpuModel} | Temperatura da GPU: {GpuTemp} | Hotspot: {GpuHotspot}. {GpuThermalDiagnosisText}");
        AssistantDiagnosticInsights.Add($"Conexão de jogos: {RouteVerdictText}");
        AssistantDiagnosticInsights.Add($"Segurança e restauração: {SecurityPostureText} Última cópia de segurança: {LastBackupStatus}");
        AssistantDiagnosticInsights.Add("O PC Control não altera drivers, voltagem ou proteções importantes do Windows automaticamente. As mudanças disponíveis podem ser conferidas e restauradas quando há histórico seguro.");
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
            new ModuleCapabilityMatrixItem(5, "Game Center", "Funcional (biblioteca + processos)", "GameNetworkAcceleratorService", "Sessão", "Descobre jogos por catálogos Steam, Epic, GOG, Ubisoft, EA e Xbox/Microsoft Store; identifica processos e conexões TCP/UDP em execução."),
            new ModuleCapabilityMatrixItem(6, "Performance Lab", "Funcional", "PresentMonSessionParser", "Arquivos CSV", "Captura frametimes reais (Média, 1% Low, 0.1% Low) via Intel PresentMon CLI."),
            new ModuleCapabilityMatrixItem(7, "Benchmark Center", "Parcial (CPU e memória)", "Benchmark local da MainViewModel", "Sessão", "Mede operações de CPU e largura de banda de memória; ainda não executa benchmarks reais de GPU nem de armazenamento."),
            new ModuleCapabilityMatrixItem(8, "Diagnostics / System Doctor", "Funcional (auditoria em leitura)", "SystemDoctorService + IntegrityVerifier", "Registro + EventLog + sessão", "Executa auditoria abrangente sob demanda, apresenta evidências e ações recomendadas; a verificação não altera configurações do Windows."),
            new ModuleCapabilityMatrixItem(9, "Windows Tweaks", "Funcional (leitura, aplicação e reversão)", "RegistryOptimizer + Agente Privilegiado", "Ledger DPAPI+HMAC + SQLite", "Consulta estados reais dos 23 ajustes, permite atualizar a leitura sem gravar no Registro e exibe explicações curtas em português, com aplicação e reversão verificadas pelo agente."),
            new ModuleCapabilityMatrixItem(10, "Debloat & Cleaner", "Funcional (4 categorias)", "DiskCleanerEngine", "Relatório de Sessão", "Faz varredura e exclusão real em quatro áreas autorizadas; não é uma limpeza geral do disco e alguns arquivos podem estar bloqueados por uso/permissão."),
            new ModuleCapabilityMatrixItem(11, "Network Lab", "Funcional", "NetworkDiagnosticsService", "Histórico em Memória", "ICMP Echo, TCP:443 multi-alvo, DNS e Throughput separados; zero perda TCP artificial."),
            new ModuleCapabilityMatrixItem(12, "Game Network Accelerator", "Parcial (sem provedor incluso)", "GameNetworkAcceleratorService + WireGuard", "SQLite + ACL Restrita", "Mede rota direta e permite avaliar um relay/VPS próprio; não inclui rede global de roteamento nem divisão por processo via WFP nesta versão."),
            new ModuleCapabilityMatrixItem(13, "Peripheral Test", "Parcial (somente monitor)", "DisplaySettingsService + Stopwatch", "Sessão", "Mostra informações do monitor e frequência do contador do Windows; não mede polling real de mouse/teclado nem perda de eventos."),
            new ModuleCapabilityMatrixItem(14, "Driver Center", "Parcial (inventário)", "DriverScannerService (WMI)", "Somente Leitura", "Lista drivers de algumas classes de dispositivos; ainda não identifica com segurança todos os dispositivos sem driver nem compara versões com as versões oficiais atuais."),
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
            tweak.VerificationState = GetTweakStateDisplay(tweak.InspectionState, isPt);
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
        foreach (var process in PresentMonSessionParser.ListAvailableProcesses()
            .Where(p => !string.IsNullOrWhiteSpace(p.MainWindowTitle)))
        {
            AvailableProcesses.Add(process);
        }
        if (SelectedProcess is null && AvailableProcesses.Count == 1)
            SelectedProcess = AvailableProcesses[0];
        PresentMonStatusText = AvailableProcesses.Count > 0
            ? $"{AvailableProcesses.Count} aplicativos com janela encontrados. Selecione o jogo aberto para medir FPS e frametime."
            : "Nenhum jogo ou aplicativo com janela foi encontrado. Abra o jogo e atualize a lista.";
    }

    private async Task RefreshDriversListAsync()
    {
        try
        {
            var drivers = await Task.Run(() => DriverScannerService.QuerySignedDrivers());
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                InstalledDrivers.Clear();
                foreach (var driver in drivers) InstalledDrivers.Add(driver);
                DriverScanStatusText = !string.IsNullOrWhiteSpace(DriverScannerService.LastScanError)
                    ? $"A leitura de drivers não foi concluída: {DriverScannerService.LastScanError}"
                    : InstalledDrivers.Count > 0
                        ? $"Escaneamento concluído: {InstalledDrivers.Count} driver(s) instalados encontrados no Windows. O status informa assinatura, não atualização."
                        : "O Windows não retornou drivers nesta consulta. Verifique o serviço WMI e tente novamente.";
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                InstalledDrivers.Clear();
                DriverScanStatusText = $"Não foi possível consultar os drivers instalados: {ex.Message}";
            });
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

    private static int GetCpuTemperaturePriority(string sensorName)
    {
        if (sensorName.Contains("Tctl/Tdie", StringComparison.OrdinalIgnoreCase)) return 100;
        if (sensorName.Contains("Tdie", StringComparison.OrdinalIgnoreCase)) return 95;
        if (sensorName.Contains("Tctl", StringComparison.OrdinalIgnoreCase)) return 90;
        if (sensorName.Contains("Package", StringComparison.OrdinalIgnoreCase)) return 85;
        if (sensorName.Contains("CCD", StringComparison.OrdinalIgnoreCase)) return 80;
        if (sensorName.Contains("Core", StringComparison.OrdinalIgnoreCase)) return 70;
        if (sensorName.Contains("CPU", StringComparison.OrdinalIgnoreCase)) return 60;
        return 1;
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

        SecurityPostureText = !IsAgentConnected
            ? "O serviço local do PC Control não está conectado. Abra o agente do Windows para consultar o computador e aplicar correções."
            : IsAdmin
                ? "Conexão segura ativa. O Windows autorizou as tarefas que precisam de administrador."
                : "Conexão segura ativa. Algumas tarefas podem pedir autorização do Windows.";

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
        GpuHardwareSensors.Clear();
        StorageDrives.Clear();
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
                foreach (var drive in snap.StorageDrives ?? Array.Empty<StorageTelemetryPoint>())
                    StorageDrives.Add(drive);

                foreach (var s in snap.Sensors)
                {
                    AllHardwareSensors.Add(s);
                    if (s.HardwareType.Contains("Gpu", StringComparison.OrdinalIgnoreCase))
                        GpuHardwareSensors.Add(s);
                }

                string notSupported = Loc["SensorStatusNotSupported"];
                string cpuSensorUnavailable = IsAdmin ? notSupported : "Leitura indisponível. Tente abrir o PC Control como administrador para acessar os sensores.";
                // Some motherboard/CPU combinations expose valid values only in the raw sensor inventory.
                // Fall back to those real readings before reporting that temperature or clock is unavailable.
                float? cpuTemperatureReading = snap.CpuPackageTemp ??
                    snap.Sensors
                        .Where(sensor => sensor.IsAvailable &&
                            string.Equals(sensor.HardwareType, "Cpu", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(sensor.SensorType, "Temperature", StringComparison.OrdinalIgnoreCase) &&
                            sensor.Value.HasValue && sensor.Value.Value > 0)
                        .OrderByDescending(sensor => GetCpuTemperaturePriority(sensor.Name))
                        .Select(sensor => sensor.Value)
                        .FirstOrDefault();

                float? cpuClockReading = snap.CpuClockMhz ??
                    snap.Sensors
                        .Where(sensor => sensor.IsAvailable &&
                            string.Equals(sensor.HardwareType, "Cpu", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(sensor.SensorType, "Clock", StringComparison.OrdinalIgnoreCase) &&
                            sensor.Value.HasValue && sensor.Value.Value > 0)
                        .Select(sensor => sensor.Value)
                        .Max();

                CpuModel = !string.IsNullOrWhiteSpace(snap.CpuModelName) ? snap.CpuModelName : unavailable;
                CpuTemp = cpuTemperatureReading.HasValue ? $"{cpuTemperatureReading.Value:F1} °C" : cpuSensorUnavailable;
                CpuLoad = snap.CpuTotalLoad.HasValue ? $"{snap.CpuTotalLoad.Value:F0} %" : notSupported;
                CpuLoadPercent = snap.CpuTotalLoad ?? 0;
                CpuClock = cpuClockReading.HasValue ? $"{cpuClockReading.Value:F0} MHz" : cpuSensorUnavailable;
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
                    GpuThermalDiagnosisText = BuildGpuThermalUserMessage(thermalEval, Loc.CurrentCulture == "pt-BR");
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
