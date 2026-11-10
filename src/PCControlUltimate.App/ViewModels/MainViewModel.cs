using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using PCControlUltimate.Infrastructure.Services;

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

public class MainViewModel : INotifyPropertyChanged
{
    private readonly AgentClientService _agentClient;
    private string _currentTab = "dashboard";
    private bool _isAgentConnected;
    private bool _isAdmin;
    private string _statusMessage = "Pronto para otimizar";
    private string _cpuTemp = "-- °C";
    private string _gpuTemp = "-- °C";
    private string _ramUsage = "-- %";
    private string _activeProfile = "PRO GAMER EXTREME";

    public event PropertyChangedEventHandler? PropertyChanged;

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

    public string CpuTemp
    {
        get => _cpuTemp;
        set { _cpuTemp = value; OnPropertyChanged(); }
    }

    public string GpuTemp
    {
        get => _gpuTemp;
        set { _gpuTemp = value; OnPropertyChanged(); }
    }

    public string RamUsage
    {
        get => _ramUsage;
        set { _ramUsage = value; OnPropertyChanged(); }
    }

    public string ActiveProfile
    {
        get => _activeProfile;
        set { _activeProfile = value; OnPropertyChanged(); }
    }

    public ICommand SelectTabCommand { get; }
    public ICommand CreateSnapshotCommand { get; }
    public ICommand RefreshStatusCommand { get; }

    public MainViewModel()
    {
        _agentClient = new AgentClientService();

        SelectTabCommand = new RelayCommand(tab =>
        {
            if (tab is string tabName) CurrentTab = tabName;
        });

        CreateSnapshotCommand = new RelayCommand(async _ =>
        {
            StatusMessage = "Criando snapshot seguro do Registro...";
            var (success, details) = await _agentClient.CreateSnapshotAsync();
            StatusMessage = success ? "Snapshot criado e verificado com sucesso!" : $"Falha no snapshot: {details}";
        });

        RefreshStatusCommand = new RelayCommand(async _ =>
        {
            await RefreshAgentStatusAsync();
        });

        _ = RefreshAgentStatusAsync();
    }

    private async Task RefreshAgentStatusAsync()
    {
        IsAgentConnected = await _agentClient.CheckConnectionAsync();
        IsAdmin = _agentClient.IsElevated;
        StatusMessage = IsAgentConnected 
            ? (IsAdmin ? "Agente Conectado (Permissão de Administrador Ativa)" : "Agente Conectado (Modo Padrão)")
            : "Agente Windows Privilegiado Desconectado (Execute como Admin)";
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
