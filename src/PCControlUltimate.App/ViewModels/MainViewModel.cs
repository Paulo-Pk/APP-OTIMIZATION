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
using PCControlUltimate.Core.Interfaces;
using PCControlUltimate.Core.Models;
using PCControlUltimate.Diagnostics;
using PCControlUltimate.Gaming.PresentMon;
using PCControlUltimate.Infrastructure.Services;
using PCControlUltimate.Windows.Services;

namespace PCControlUltimate.App.ViewModels;

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
    private string? _previousRawValue;
    private string _previousValueKind = "DWord";
    private bool _wasInitiallyAbsent;
    private bool _keyCreatedByApp;

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

    public string? PreviousRawValue
    {
        get => _previousRawValue;
        set { _previousRawValue = value; OnPropertyChanged(); }
    }

    public string PreviousValueKind
    {
        get => _previousValueKind;
        set { _previousValueKind = value; OnPropertyChanged(); }
    }

    public bool WasInitiallyAbsent
    {
        get => _wasInitiallyAbsent;
        set { _wasInitiallyAbsent = value; OnPropertyChanged(); }
    }

    public bool KeyCreatedByApp
    {
        get => _keyCreatedByApp;
        set { _keyCreatedByApp = value; OnPropertyChanged(); }
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
    private CancellationTokenSource? _presentMonCts;

    public LocalizationService Loc { get; } = new();

    private string _currentTab = "dashboard";
    private bool _isAgentConnected;
    private bool _isAdmin;
    private string _statusMessage = "Verificando comunicação com o Agente...";

    // Hardware & GPU Health properties
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
    private bool _confirmIncompleteRestore;
    private bool _rollbackOnPartialFailure = true;
    private string _restoreOperationReport = "Selecione um backup para inspecionar ou restaurar.";

    // Debloat properties
    private bool _confirmCleanerDeletion;
    private string _debloatSummaryText = "Simule (Dry-Run) antes de executar qualquer exclusão.";

    // Network Lab properties
    private string _networkSummaryText = "Clique em Executar Diagnóstico para medir ICMP, TCP e DNS.";
    private string _externalThroughputText = "Clique para medir throughput real contra Cloudflare CDN.";

    // PresentMon properties
    private string _presentMonStatusText = "Verifique a instalação do PresentMon e selecione um processo.";
    private RunningProcessInfo? _selectedProcess;
    private string _manualProcessName = "cs2.exe";
    private int _captureDurationSeconds = 10;
    private bool _isCapturingPresentMon;
    private string _presentMonMetricsSummary = "Nenhuma sessão capturada.";

    // Diagnostics & Security properties
    private string _securityPostureText = "Verificando DPAPI, ACL NTFS e privilégios UAC...";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TweakItemViewModel> CatalogTweaks { get; } = new();
    public ObservableCollection<SensorDataPoint> AllHardwareSensors { get; } = new();
    public ObservableCollection<SnapshotSummaryItem> ExistingBackups { get; } = new();
    public ObservableCollection<RegistryValueEntry> SelectedBackupEntries { get; } = new();
    public ObservableCollection<RegistryReadFailure> SelectedBackupFailures { get; } = new();
    public ObservableCollection<CleanerCategoryViewModel> CleanerCategories { get; } = new();
    public ObservableCollection<string> CleanerAffectedFilesLog { get; } = new();
    public ObservableCollection<NetworkEndpointTestResult> NetworkEndpoints { get; } = new();
    public ObservableCollection<DnsQueryResult> DnsResults { get; } = new();
    public ObservableCollection<string> NetworkHistory { get; } = new();
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
        }
    }

    public string HeaderTitle => Loc["HeaderSubtitle"];

    public string CurrentTab
    {
        get => _currentTab;
        set
        {
            if (_currentTab != value)
            {
                _currentTab = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDashboardVisible));
                OnPropertyChanged(nameof(IsHardwareVisible));
                OnPropertyChanged(nameof(IsTweaksVisible));
                OnPropertyChanged(nameof(IsRestoreVisible));
                OnPropertyChanged(nameof(IsDebloatVisible));
                OnPropertyChanged(nameof(IsNetworkVisible));
                OnPropertyChanged(nameof(IsGamingVisible));
                OnPropertyChanged(nameof(IsDiagnosticsVisible));
                OnPropertyChanged(nameof(IsDriversVisible));
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

    public MainViewModel() : this(new AgentClientService(), null)
    {
    }

    public MainViewModel(AgentClientService agentClient, IHardwareMonitorService? hardwareMonitor)
    {
        _agentClient = agentClient;
        _hardwareMonitor = hardwareMonitor;

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
                if (tabName == "restore") RefreshBackupsList();
                if (tabName == "diagnostics") RefreshAuditLogs();
                if (tabName == "drivers" && InstalledDrivers.Count == 0) RefreshDriversList();
                if (tabName == "gaming" && AvailableProcesses.Count == 0) RefreshRunningProcesses();
            }
        });

        ToggleLanguageCommand = new RelayCommand(_ =>
        {
            Loc.ToggleLanguage();
            UpdateLocalizedCollections();
            OnPropertyChanged(nameof(Language));
            OnPropertyChanged(nameof(HeaderTitle));
            StatusMessage = Loc.CurrentCulture == "pt-BR"
                ? "Idioma alterado imediatamente para Português Brasileiro (PT-BR)."
                : "Language immediately switched to American English (EN-US).";
        });

        CreateSnapshotCommand = new RelayCommand(async _ =>
        {
            StatusMessage = Loc.CurrentCulture == "pt-BR"
                ? "Capturando e verificando backup SHA-256 dos ajustes suportados..."
                : "Capturing and verifying SHA-256 backup of supported settings...";
            var (success, details) = await _agentClient.CreateSnapshotAsync();
            RefreshBackupsList();
            RefreshAuditLogs();
            if (success)
            {
                LastBackupStatus = $"SHA-256 OK ({DateTime.Now:HH:mm:ss})";
                StatusMessage = Loc.CurrentCulture == "pt-BR"
                    ? "Backup criado e verificado no disco com sucesso."
                    : "Backup created and verified on disk successfully.";
            }
            else
            {
                StatusMessage = $"Falha / Backup incompleto: {details}";
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
                        tweak.PreviousRawValue = doc.RootElement.TryGetProperty("previousValue", out var pv) ? pv.GetString() : null;
                        tweak.PreviousValueKind = doc.RootElement.TryGetProperty("valueKind", out var vk) ? (vk.GetString() ?? tweak.ValueKind) : tweak.ValueKind;
                        tweak.WasInitiallyAbsent = doc.RootElement.TryGetProperty("wasInitiallyAbsent", out var wa) && wa.GetBoolean();
                        tweak.KeyCreatedByApp = doc.RootElement.TryGetProperty("keyCreatedByApp", out var kc) && kc.GetBoolean();
                    }
                    catch { }

                    tweak.IsApplied = true;
                    tweak.VerificationState = $"Verificado ({tweak.RecommendedValue})";
                    StatusMessage = $"Ajuste '{tweak.TweakId}' aplicado e confirmado por releitura.";
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
                var (ok, details) = await _agentClient.RestoreCatalogTweakAsync(
                    tweak.TweakId,
                    tweak.PreviousRawValue,
                    tweak.PreviousValueKind,
                    tweak.WasInitiallyAbsent,
                    tweak.KeyCreatedByApp
                );
                RefreshAuditLogs();
                if (ok)
                {
                    tweak.IsApplied = false;
                    tweak.VerificationState = "Restaurado e Verificado";
                    StatusMessage = $"Ajuste '{tweak.TweakId}' revertido ao estado original.";
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
                ? $"Integridade SHA-256 confirmada ({checksum}). Todas as entradas pertencem à whitelist."
                : $"Falha de integridade no backup: {err}";
        });

        RestoreSelectedBackupCommand = new RelayCommand(_ =>
        {
            if (SelectedBackup == null)
            {
                RestoreOperationReport = "Selecione um backup na lista antes de restaurar.";
                return;
            }

            var report = RegistrySnapshotEngine.RestoreFromBackupWithReport(
                SelectedBackup.FilePath,
                ConfirmIncompleteRestore,
                singleTweakId: null,
                rollbackOnPartialFailure: RollbackOnPartialFailure
            );
            RefreshAuditLogs();

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
                RestoreOperationReport = $"Resultado: {report.RestoredCount} restaurados, {report.FailedCount} falhas. Detalhe: {report.ErrorMessage}";
            }
        });

        RestoreSingleBackupEntryCommand = new RelayCommand(param =>
        {
            if (SelectedBackup == null || param is not RegistryValueEntry entry) return;
            string targetId = !string.IsNullOrWhiteSpace(entry.TweakId)
                ? entry.TweakId
                : (RegistryOptimizer.FindAuthorizedCatalogEntry(entry.KeyPath, entry.ValueName)?.TweakId ?? "");

            var report = RegistrySnapshotEngine.RestoreFromBackupWithReport(
                SelectedBackup.FilePath,
                confirmIncompleteBackup: true,
                singleTweakId: targetId,
                rollbackOnPartialFailure: false
            );
            RefreshAuditLogs();

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

        RefreshBackupsList();
        RefreshAuditLogs();
        _ = RefreshTelemetryAndStatusAsync();
    }

    private void UpdateLocalizedCollections()
    {
        bool isPt = Loc.CurrentCulture == "pt-BR";
        foreach (var tweak in CatalogTweaks)
        {
            tweak.Name = isPt ? tweak.NamePt : tweak.NameEn;
        }
        foreach (var cat in CleanerCategories)
        {
            cat.DisplayName = isPt ? cat.NamePt : cat.NameEn;
        }
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
        if (SelectedBackup == null) return;

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

        StatusMessage = IsAgentConnected
            ? (IsAdmin ? Loc["AgentConnectedAdmin"] : Loc["AgentConnectedStandard"])
            : Loc["AgentDisconnected"];

        SecurityPostureText =
            $"Agente Autenticado: {IsAgentConnected} | DPAPI Aplicado: {_agentClient.IsTokenDpapiProtected} | " +
            $"ACL NTFS Restrita ao Usuário: {_agentClient.IsTokenAclRestricted} | Administrador (UAC): {IsAdmin}";

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
        if (_hardwareMonitor != null && _hardwareMonitor.IsAvailable)
        {
            var snap = await _hardwareMonitor.CaptureSnapshotAsync();
            foreach (var s in snap.AllSensors)
            {
                AllHardwareSensors.Add(s);
            }

            CpuModel = !string.IsNullOrWhiteSpace(snap.CpuModelName) ? snap.CpuModelName : unavailable;
            CpuTemp = snap.CpuPackageTemp.HasValue ? $"{snap.CpuPackageTemp.Value:F1} °C" : unavailable;
            CpuLoad = snap.CpuTotalLoad.HasValue ? $"{snap.CpuTotalLoad.Value:F0} %" : unavailable;
            CpuClock = snap.CpuClockMhz.HasValue ? $"{snap.CpuClockMhz.Value:F0} MHz" : unavailable;
            CpuPower = snap.CpuPowerWatts.HasValue ? $"{snap.CpuPowerWatts.Value:F1} W" : unavailable;

            GpuModel = !string.IsNullOrWhiteSpace(snap.GpuModelName) ? snap.GpuModelName : unavailable;
            GpuTemp = snap.GpuCoreTemp.HasValue ? $"{snap.GpuCoreTemp.Value:F1} °C" : unavailable;
            GpuHotspot = snap.GpuHotspotTemp.HasValue ? $"{snap.GpuHotspotTemp.Value:F1} °C" : unavailable;
            GpuLoad = snap.GpuLoadPercent.HasValue ? $"{snap.GpuLoadPercent.Value:F0} %" : unavailable;
            GpuClock = snap.GpuClockMhz.HasValue ? $"{snap.GpuClockMhz.Value:F0} MHz" : unavailable;
            GpuPower = snap.GpuPowerWatts.HasValue ? $"{snap.GpuPowerWatts.Value:F1} W" : unavailable;
            GpuFanRpm = snap.GpuFanRpm.HasValue ? $"{snap.GpuFanRpm.Value:F0} RPM" : unavailable;

            if (snap.GpuVramUsedMb.HasValue && snap.GpuVramTotalMb.HasValue && snap.GpuVramTotalMb.Value > 0)
            {
                GpuVram = $"{snap.GpuVramUsedMb.Value:F0} MB / {snap.GpuVramTotalMb.Value:F0} MB";
            }
            else
            {
                GpuVram = unavailable;
            }

            if (snap.GpuCoreTemp.HasValue && snap.GpuHotspotTemp.HasValue)
            {
                var thermalEval = GpuThermalAuditor.EvaluateThermalState(snap.GpuCoreTemp.Value, snap.GpuHotspotTemp.Value);
                GpuThermalDiagnosisText = $"Status: {thermalEval.Status} (Delta Core/Hotspot: +{thermalEval.Delta:F1} °C) — {thermalEval.Recommendation}";
            }
            else
            {
                GpuThermalDiagnosisText = unavailable;
            }

            if (snap.RamUsedGb.HasValue && snap.RamTotalGb.HasValue && snap.RamTotalGb.Value > 0)
            {
                RamUsage = $"{snap.RamUsedGb.Value:F1} GB / {snap.RamTotalGb.Value:F1} GB";
            }
            else
            {
                RamUsage = unavailable;
            }
        }
        else
        {
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
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
