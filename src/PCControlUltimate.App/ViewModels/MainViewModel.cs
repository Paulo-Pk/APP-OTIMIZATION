using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using PCControlUltimate.Core.Interfaces;
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

    public string TweakId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string RegistryPath { get; init; } = string.Empty;
    public string ValueName { get; init; } = string.Empty;
    public string RecommendedValue { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = string.Empty;

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

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class MainViewModel : INotifyPropertyChanged
{
    private readonly AgentClientService _agentClient;
    private readonly IHardwareMonitorService? _hardwareMonitor;

    private string _language = "pt-BR";
    private string _currentTab = "dashboard";
    private bool _isAgentConnected;
    private bool _isAdmin;
    private string _statusMessage = "Verificando comunicação com o Agente...";
    private string _cpuModel = "Não disponível";
    private string _cpuTemp = "Não disponível";
    private string _cpuLoad = "Não disponível";
    private string _gpuModel = "Não disponível";
    private string _gpuTemp = "Não disponível";
    private string _gpuHotspot = "Não disponível";
    private string _ramUsage = "Não disponível";
    private string _displayInfoText = "Não detectado";
    private string _presentMonStatusText = "Aguardando captura real PresentMon";
    private string _debloatEstimateText = "Execute varredura para estimar";
    private string _debloatFreedText = "0 B removidos";
    private string _lastBackupStatus = "Nenhum backup verificado na sessão";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TweakItemViewModel> CatalogTweaks { get; } = new();

    public string Language
    {
        get => _language;
        set { _language = value; OnPropertyChanged(); OnPropertyChanged(nameof(HeaderTitle)); }
    }

    public string HeaderTitle => _language == "pt-BR"
        ? "CONSOLE NATIVO DE ENGENHARIA E OTIMIZAÇÃO WINDOWS"
        : "NATIVE WINDOWS OPTIMIZATION & ENGINEERING CONSOLE";

    public string CurrentTab
    {
        get => _currentTab;
        set { _currentTab = value; OnPropertyChanged(); }
    }

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

    public string CpuModel
    {
        get => _cpuModel;
        set { _cpuModel = value; OnPropertyChanged(); }
    }

    public string CpuTemp
    {
        get => _cpuTemp;
        set { _cpuTemp = value; OnPropertyChanged(); }
    }

    public string CpuLoad
    {
        get => _cpuLoad;
        set { _cpuLoad = value; OnPropertyChanged(); }
    }

    public string GpuModel
    {
        get => _gpuModel;
        set { _gpuModel = value; OnPropertyChanged(); }
    }

    public string GpuTemp
    {
        get => _gpuTemp;
        set { _gpuTemp = value; OnPropertyChanged(); }
    }

    public string GpuHotspot
    {
        get => _gpuHotspot;
        set { _gpuHotspot = value; OnPropertyChanged(); }
    }

    public string RamUsage
    {
        get => _ramUsage;
        set { _ramUsage = value; OnPropertyChanged(); }
    }

    public string DisplayInfoText
    {
        get => _displayInfoText;
        set { _displayInfoText = value; OnPropertyChanged(); }
    }

    public string PresentMonStatusText
    {
        get => _presentMonStatusText;
        set { _presentMonStatusText = value; OnPropertyChanged(); }
    }

    public string DebloatEstimateText
    {
        get => _debloatEstimateText;
        set { _debloatEstimateText = value; OnPropertyChanged(); }
    }

    public string DebloatFreedText
    {
        get => _debloatFreedText;
        set { _debloatFreedText = value; OnPropertyChanged(); }
    }

    public string LastBackupStatus
    {
        get => _lastBackupStatus;
        set { _lastBackupStatus = value; OnPropertyChanged(); }
    }

    public ICommand SelectTabCommand { get; }
    public ICommand ToggleLanguageCommand { get; }
    public ICommand CreateSnapshotCommand { get; }
    public ICommand RefreshStatusCommand { get; }
    public ICommand ApplyTweakCommand { get; }
    public ICommand ScanDebloatCommand { get; }
    public ICommand PurgeTempUserCommand { get; }
    public ICommand CheckPresentMonCommand { get; }
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
                Name = def.Description,
                RegistryPath = $"{def.Hive}\\{def.SubKeyPath}",
                ValueName = def.ValueName,
                RecommendedValue = def.RecommendedValue,
                RiskLevel = def.RiskLevel,
                IsApplied = false,
                VerificationState = "Não verificado"
            });
        }

        SelectTabCommand = new RelayCommand(tab =>
        {
            if (tab is string tabName) CurrentTab = tabName;
        });

        ToggleLanguageCommand = new RelayCommand(_ =>
        {
            Language = Language == "pt-BR" ? "en-US" : "pt-BR";
            StatusMessage = Language == "pt-BR" ? "Idioma alterado para Português (PT-BR)" : "Language switched to English (EN-US)";
        });

        CreateSnapshotCommand = new RelayCommand(async _ =>
        {
            StatusMessage = Language == "pt-BR"
                ? "Criando backup dos ajustes suportados..."
                : "Creating backup of supported settings...";
            var (success, details) = await _agentClient.CreateSnapshotAsync();
            if (success)
            {
                LastBackupStatus = $"Backup SHA-256 verificado ({DateTime.Now:HH:mm:ss})";
                StatusMessage = Language == "pt-BR"
                    ? "Backup dos ajustes suportados criado e verificado!"
                    : "Backup of supported settings created and verified!";
            }
            else
            {
                StatusMessage = $"Falha no backup: {details}";
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
                if (ok)
                {
                    tweak.IsApplied = true;
                    tweak.VerificationState = $"Verificado ({tweak.RecommendedValue})";
                    StatusMessage = $"Ajuste '{tweak.TweakId}' gravado e verificado por releitura.";
                }
                else
                {
                    tweak.VerificationState = "Falha / Requer Elevação";
                    StatusMessage = $"Não foi possível aplicar '{tweak.TweakId}': {details}";
                }
            }
        });

        ScanDebloatCommand = new RelayCommand(_ =>
        {
            long d3d = DiskCleanerEngine.ScanCategoryBytes("cache-dx-shaders");
            long tempU = DiskCleanerEngine.ScanCategoryBytes("temp-user");
            long tempW = DiskCleanerEngine.ScanCategoryBytes("temp-windows");
            double totalMb = (d3d + tempU + tempW) / (1024.0 * 1024.0);
            DebloatEstimateText = $"Estimado: {totalMb:F2} MB (somente leitura)";
        });

        PurgeTempUserCommand = new RelayCommand(_ =>
        {
            var report = DiskCleanerEngine.PurgeCategoryWithReport("temp-user");
            double freedMb = report.FreedBytes / (1024.0 * 1024.0);
            DebloatFreedText = $"Liberado real: {freedMb:F2} MB ({report.DeletedFilesCount} arquivos removidos)";
        });

        CheckPresentMonCommand = new RelayCommand(_ =>
        {
            var (installed, path, ver) = PresentMonSessionParser.CheckPresentMonInstallation();
            PresentMonStatusText = installed
                ? $"PresentMon instalado (v{ver}) em {path}"
                : "PresentMon não instalado — métricas exibidas apenas após captura real";
        });

        RequestElevationCommand = new RelayCommand(_ =>
        {
            if (UacElevationBridge.IsRunningAsAdministrator())
            {
                StatusMessage = "O processo já possui privilégios administrativos (UAC ativo).";
                return;
            }
            bool restarted = UacElevationBridge.TryRestartElevated();
            if (!restarted)
            {
                StatusMessage = "Elevação UAC cancelada pelo usuário. Operando em modo padrão.";
            }
        });

        _ = RefreshTelemetryAndStatusAsync();
    }

    public async Task RefreshTelemetryAndStatusAsync()
    {
        string unavailable = Language == "pt-BR" ? "Não disponível" : "Unavailable";

        IsAgentConnected = await _agentClient.CheckConnectionAsync();
        IsAdmin = _agentClient.IsElevated;

        StatusMessage = IsAgentConnected
            ? (IsAdmin
                ? $"Agente Autenticado v{_agentClient.AgentVersion} (Administrador)"
                : $"Agente Autenticado v{_agentClient.AgentVersion} (Modo Padrão Sem Elevação)")
            : "Agente Desconectado ou Não Autenticado";

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
                DisplayInfoText = Language == "pt-BR" ? "Não detectado" : "Not detected";
            }
        }

        // Update Hardware Sensors from real LibreHardwareMonitor
        if (_hardwareMonitor != null && _hardwareMonitor.IsAvailable)
        {
            var snap = await _hardwareMonitor.CaptureSnapshotAsync();
            CpuModel = !string.IsNullOrWhiteSpace(snap.CpuModelName) ? snap.CpuModelName : unavailable;
            CpuTemp = snap.CpuPackageTemp.HasValue ? $"{snap.CpuPackageTemp.Value:F1} °C" : unavailable;
            CpuLoad = snap.CpuTotalLoad.HasValue ? $"{snap.CpuTotalLoad.Value:F0} %" : unavailable;

            GpuModel = !string.IsNullOrWhiteSpace(snap.GpuModelName) ? snap.GpuModelName : unavailable;
            GpuTemp = snap.GpuCoreTemp.HasValue ? $"{snap.GpuCoreTemp.Value:F1} °C" : unavailable;
            GpuHotspot = snap.GpuHotspotTemp.HasValue ? $"{snap.GpuHotspotTemp.Value:F1} °C" : unavailable;

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
            GpuModel = unavailable;
            GpuTemp = unavailable;
            GpuHotspot = unavailable;
            RamUsage = unavailable;
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
