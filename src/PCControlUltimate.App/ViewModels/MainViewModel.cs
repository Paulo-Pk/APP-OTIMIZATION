using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
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
    private CancellationTokenSource? _presentMonCts;

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

    public string Language
    {
        get => Loc.CurrentCulture;
        set
        {
            Loc.CurrentCulture = value;
            UpdateLocalizedCollections();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HeaderTitle));
            OnPropertyChanged(nameof(ActiveModuleDisplayTitle));
        }
    }

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
                OnPropertyChanged(nameof(IsTweaksVisible));
                OnPropertyChanged(nameof(IsRestoreVisible));
                OnPropertyChanged(nameof(IsDebloatVisible));
                OnPropertyChanged(nameof(IsNetworkVisible));
                OnPropertyChanged(nameof(IsGamingVisible));
                OnPropertyChanged(nameof(IsDiagnosticsVisible));
                OnPropertyChanged(nameof(IsDriversVisible));
                OnPropertyChanged(nameof(IsAcceleratorVisible));
            }
        }
    }

    public bool IsDashboardVisible => string.Equals(_currentTab, "dashboard", StringComparison.OrdinalIgnoreCase);
    public bool IsHardwareVisible => string.Equals(_currentTab, "hardware", StringComparison.OrdinalIgnoreCase);
    public bool IsTweaksVisible => string.Equals(_currentTab, "tweaks", StringComparison.OrdinalIgnoreCase);
    public bool IsRestoreVisible => string.Equals(_currentTab, "restore", StringComparison.OrdinalIgnoreCase);
    public bool IsDebloatVisible => string.Equals(_currentTab, "debloat", StringComparison.OrdinalIgnoreCase);
    public bool IsNetworkVisible => string.Equals(_currentTab, "network", StringComparison.OrdinalIgnoreCase);
    public bool IsGamingVisible => string.Equals(_currentTab, "gaming", StringComparison.OrdinalIgnoreCase);
    public bool IsDiagnosticsVisible => string.Equals(_currentTab, "diagnostics", StringComparison.OrdinalIgnoreCase);
    public bool IsDriversVisible => string.Equals(_currentTab, "drivers", StringComparison.OrdinalIgnoreCase);
    public bool IsAcceleratorVisible => string.Equals(_currentTab, "accelerator", StringComparison.OrdinalIgnoreCase);

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

    public MainViewModel() : this(new AgentClientService(), null, new LocalizationService(), new DatabaseService())
    {
    }

    public MainViewModel(
        AgentClientService agentClient,
        IHardwareMonitorService? hardwareMonitor,
        LocalizationService? localizationService = null,
        DatabaseService? databaseService = null)
    {
        _agentClient = agentClient;
        _hardwareMonitor = hardwareMonitor;
        Loc = localizationService ?? new LocalizationService();
        _databaseService = databaseService ?? new DatabaseService();
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
            OnPropertyChanged(nameof(HeaderTitle));
            OnPropertyChanged(nameof(ActiveModuleDisplayTitle));
            StatusMessage = Loc.CurrentCulture == "pt-BR"
                ? "Idioma alterado imediatamente para Português Brasileiro (PT-BR)."
                : "Language immediately switched to American English (EN-US).";
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
            var resultObj = await NetworkDiagnosticsService.RunRealDiagnosticsAsync();
            string json = JsonSerializer.Serialize(resultObj);
            using var doc = JsonDocument.Parse(json);

            NetworkEndpoints.Clear();
            if (doc.RootElement.TryGetProperty("endpointDetails", out var eps) && eps.ValueKind == JsonValueKind.Array)
            {
                foreach (var ep in eps.EnumerateArray())
                {
                    var item = JsonSerializer.Deserialize<NetworkEndpointTestResult>(ep.GetRawText());
                    if (item != null) NetworkEndpoints.Add(item);
                }
            }

            DnsResults.Clear();
            if (doc.RootElement.TryGetProperty("dnsChecks", out var dnsArr) && dnsArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in dnsArr.EnumerateArray())
                {
                    var item = JsonSerializer.Deserialize<DnsQueryResult>(d.GetRawText());
                    if (item != null) DnsResults.Add(item);
                }
            }

            NetworkHistory.Clear();
            foreach (var h in NetworkDiagnosticsService.GetDiagnosticHistory())
            {
                NetworkHistory.Add(h);
            }

            double loss = doc.RootElement.TryGetProperty("packetLoss", out var pl) ? pl.GetDouble() : 0;
            NetworkSummaryText = $"Diagnóstico concluído ({DateTime.Now:HH:mm:ss}) · Perda TCP medida: {loss:F1}%";
        });

        RunExternalThroughputCommand = new RelayCommand(async _ =>
        {
            ExternalThroughputText = "Medindo download real contra https://speed.cloudflare.com/__down ...";
            var res = await NetworkDiagnosticsService.MeasureExternalThroughputAsync();
            if (res.Available && res.DownloadMbps.HasValue)
            {
                ExternalThroughputText = $"Throughput Real: {res.DownloadMbps.Value:F1} Mbps ({res.BytesTransferred / 1024} KB em {res.DurationMs} ms via Cloudflare CDN)";
            }
            else
            {
                ExternalThroughputText = $"Indisponível: {res.ErrorReason}";
            }

            NetworkHistory.Clear();
            foreach (var h in NetworkDiagnosticsService.GetDiagnosticHistory())
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

        RegisterSampleVpsNodeCommand = new RelayCommand(async _ =>
        {
            var (ok, err, node) = GameNetworkAcceleratorService.RegisterOrUpdateRelayNode(
                nodeId: "sa-gru-relay-01",
                displayName: "Nó São Paulo (GRU-01 WireGuard Seletivo)",
                regionCode: "SA-EAST-1",
                endpointHost: "1.1.1.1",
                endpointPort: 51820,
                publicKey: "mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=",
                gameTargetCidrs: new[] { "155.133.248.0/24", "104.160.152.0/24" },
                isCustomVps: true
            );

            if (ok && node != null)
            {
                await _databaseService.UpsertRelayNodeAsync(new RelayNodeEntity
                {
                    NodeId = node.NodeId,
                    DisplayName = node.DisplayName,
                    RegionCode = node.RegionCode,
                    EndpointHost = node.EndpointHost,
                    EndpointPort = node.EndpointPort,
                    PublicKey = node.PublicKey,
                    AllowedTargetCidrs = string.Join(", ", node.GameTargetCidrs),
                    IsCustomVps = node.IsCustomVps
                });

                RelayNodes.Clear();
                foreach (var n in GameNetworkAcceleratorService.GetRegisteredRelayNodes())
                {
                    RelayNodes.Add(n);
                }
            }
            else
            {
                RouteVerdictText = $"Erro ao cadastrar nó: {err}";
            }
        });

        AuditWireGuardConfigCommand = new RelayCommand(_ =>
        {
            var node = GameNetworkAcceleratorService.GetRegisteredRelayNodes().FirstOrDefault();
            if (node == null)
            {
                WireGuardConfigAuditText = "Cadastre ao menos um nó WireGuard/VPS antes de validar a configuração.";
                return;
            }

            string selectiveCfg = WireGuardTunnelManager.BuildSelectiveGameTunnelConfig(
                "wJ9K4m8N1pQ6rT3vX5zB7cF0hL2sD4gA8eM1uP9yV3E=",
                "10.66.66.2/32",
                node
            );
            var audit = WireGuardTunnelManager.ValidateAndAuditConfig(selectiveCfg, allowDefaultRouteOverride: false);
            WireGuardConfigAuditText = audit.IsValid
                ? (Loc.CurrentCulture == "pt-BR" ? audit.RouteImpactExplanationPt : audit.RouteImpactExplanationEn)
                : $"Configuração bloqueada: {audit.ErrorReason}";
        });

        CheckWireGuardTunnelCommand = new RelayCommand(async _ =>
        {
            var status = await WireGuardTunnelManager.QueryTunnelStatusAsync("PCControlGameTunnel");
            WireGuardStatusText = status.DiagnosticMessage;
        });

        Navigation = new NavigationService(this);
        RefreshBackupsList();
        RefreshAuditLogs();
        _ = _databaseService.InitializeAndVerifyAsync();
        _ = RefreshTelemetryAndStatusAsync();
    }

    private void InitializeModuleCapabilityMatrix()
    {
        ModuleCapabilityMatrix.Clear();
        var items = new[]
        {
            new ModuleCapabilityMatrixItem(1, "Hardware Monitor", "Funcional", "LibreHardwareMonitorWrapper + Win32", "Logs / Sessão", "Exibe somente sensores físicos reais; N/D quando não suportado."),
            new ModuleCapabilityMatrixItem(2, "GPU Health Lab", "Funcional", "GpuThermalAuditor", "Logs / Sessão", "Diagnóstico térmico Core/Hotspot por arquitetura; zero overclock automático."),
            new ModuleCapabilityMatrixItem(3, "Boost Center", "Funcional (Perfis)", "RegistryOptimizer (Catálogo Fechado)", "SQLite + Ledger DPAPI", "Aplica perfis reversíveis; não encerra processos arbitrariamente."),
            new ModuleCapabilityMatrixItem(4, "Game Center", "Funcional", "GameNetworkAcceleratorService", "SQLite", "Detecta processos reais de jogos e conexões TCP/UDP ativas."),
            new ModuleCapabilityMatrixItem(5, "Performance Lab", "Funcional", "PresentMonSessionParser", "Arquivos CSV", "Captura frametimes reais via Intel PresentMon CLI."),
            new ModuleCapabilityMatrixItem(6, "Benchmark Center", "Parcial / Honesto", "Diagnóstico Sintético Local", "SQLite", "Medição sintética determinística; sem scores inventados de terceiros."),
            new ModuleCapabilityMatrixItem(7, "Diagnostics", "Funcional", "IntegrityVerifier + Auditoria", "Registro + SQLite", "Valida DPAPI, ACL NTFS, HMAC-SHA256, UAC e trilha completa."),
            new ModuleCapabilityMatrixItem(8, "Windows Tweaks", "Funcional", "RegistryOptimizer", "Ledger DPAPI+HMAC + SQLite", "Zero bypass; grava estado original no Ledger antes da escrita."),
            new ModuleCapabilityMatrixItem(9, "Debloat / Cleaner", "Funcional", "DiskCleanerEngine", "Relatório de Sessão", "Dry-Run separado de Purge; bloqueia ReparsePoints/Symlinks."),
            new ModuleCapabilityMatrixItem(10, "Network Lab", "Funcional", "NetworkDiagnosticsService", "Histórico em Memória", "ICMP Echo, TCP Handshake, DNS e Throughput HTTP separados."),
            new ModuleCapabilityMatrixItem(11, "Peripheral Test", "Funcional", "Monitor & Input Cadence Inspector", "Sessão", "Avalia cadência de eventos de input e refresh rate Win32."),
            new ModuleCapabilityMatrixItem(12, "Driver Center", "Funcional (Leitura)", "DriverScannerService (WMI)", "Somente Leitura", "Inventário de drivers assinados; não baixa nem instala drivers."),
            new ModuleCapabilityMatrixItem(13, "Software Hub", "Consultivo", "Catálogo Winget Verificado", "Somente Leitura", "Exibe comandos winget oficiais; sem instalação oculta."),
            new ModuleCapabilityMatrixItem(14, "Restore Center", "Funcional", "RegistrySnapshotEngine + Ledger", "JSON SHA-256 + DPAPI", "Verificação SHA-256 obrigatória e rollback transacional."),
            new ModuleCapabilityMatrixItem(15, "AI Assistant", "Consultivo Local", "Motor de Diagnóstico Determinístico", "Somente Leitura", "Explica telemetria e rotas localmente sem chamadas externas ocultas."),
            new ModuleCapabilityMatrixItem(16, "Game Network Accelerator", "Funcional c/ Limite WFP", "GameNetworkAcceleratorService + WireGuard", "SQLite + Config", "Roteamento seletivo por AllowedIPs do jogo; driver kernel WFP desativado.")
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
                CpuClock = snap.CpuClockMhz.HasValue ? $"{snap.CpuClockMhz.Value:F0} MHz" : notSupported;
                CpuPower = snap.CpuPowerWatts.HasValue ? $"{snap.CpuPowerWatts.Value:F1} W" : notSupported;

                GpuModel = !string.IsNullOrWhiteSpace(snap.GpuModelName) ? snap.GpuModelName : unavailable;
                GpuTemp = snap.GpuCoreTemp.HasValue ? $"{snap.GpuCoreTemp.Value:F1} °C" : notSupported;
                GpuHotspot = snap.GpuHotspotTemp.HasValue ? $"{snap.GpuHotspotTemp.Value:F1} °C" : notSupported;
                GpuLoad = snap.GpuLoadPercent.HasValue ? $"{snap.GpuLoadPercent.Value:F0} %" : notSupported;
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
                    var thermalEval = GpuThermalAuditor.EvaluateThermalState(snap.GpuCoreTemp.Value, snap.GpuHotspotTemp.Value);
                    GpuThermalDiagnosisText = $"Status: {thermalEval.Status} (Delta Core/Hotspot: +{thermalEval.Delta:F1} °C) — {thermalEval.Message}";
                }
                else
                {
                    GpuThermalDiagnosisText = notSupported;
                }

                if (snap.RamUsedGb.HasValue && snap.RamTotalGb.HasValue && snap.RamTotalGb.Value > 0)
                {
                    RamUsage = $"{snap.RamUsedGb.Value:F1} GB / {snap.RamTotalGb.Value:F1} GB";
                }
                else
                {
                    RamUsage = notSupported;
                }
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
        CpuClock = unavailable;
        CpuPower = unavailable;
        GpuModel = unavailable;
        GpuTemp = unavailable;
        GpuHotspot = unavailable;
        GpuLoad = unavailable;
        GpuClock = unavailable;
        GpuPower = unavailable;
        GpuFanRpm = unavailable;
        GpuVram = unavailable;
        GpuThermalDiagnosisText = unavailable;
        RamUsage = unavailable;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
