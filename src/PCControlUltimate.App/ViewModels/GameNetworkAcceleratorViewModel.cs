using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PCControlUltimate.App.Services;
using PCControlUltimate.Gaming.NetworkAcceleration;

namespace PCControlUltimate.App.ViewModels;

public sealed class GameNetworkAcceleratorViewModel : ModuleViewModelBase
{
    private readonly GameProcessDetector _detector = new();
    private readonly IRouteProbe _probe = new NetworkRouteProbe();
    private readonly ITunnelProvider _tunnel = new WireGuardWindowsProvider();
    private CancellationTokenSource? _operationCts;
    private string _targetHost = "1.1.1.1";
    private string _targetPort = "443";
    private string _status = "Pronto. Faça uma medição direta antes de testar um túnel.";
    private string _configPath = string.Empty;
    private string _tunnelName = string.Empty;
    private GameProcessInfo? _selectedGame;
    private bool _busy;
    private bool _tunnelInstalled;

    public GameNetworkAcceleratorViewModel(MainViewModel workspace)
        : base(workspace, "accelerator", "NavAccelerator")
    {
        RefreshGamesCommand = new RelayCommand(_ => RefreshGames());
        MeasureRouteCommand = new RelayCommand(async _ => await MeasureRouteAsync(), _ => !IsBusy);
        TraceRouteCommand = new RelayCommand(async _ => await TraceRouteAsync(), _ => !IsBusy);
        ImportConfigCommand = new RelayCommand(_ => ImportConfig());
        InstallTunnelCommand = new RelayCommand(async _ => await InstallTunnelAsync(), _ => !IsBusy && !string.IsNullOrWhiteSpace(ConfigPath));
        RemoveTunnelCommand = new RelayCommand(async _ => await RemoveTunnelAsync(), _ => !IsBusy && !string.IsNullOrWhiteSpace(TunnelName));
        RefreshGames();
    }

    public ObservableCollection<GameProcessInfo> RunningGames { get; } = new();
    public ObservableCollection<RouteProbeResult> Measurements { get; } = new();
    public ObservableCollection<TraceHopResult> TraceHops { get; } = new();
    public ICommand RefreshGamesCommand { get; }
    public ICommand MeasureRouteCommand { get; }
    public ICommand TraceRouteCommand { get; }
    public ICommand ImportConfigCommand { get; }
    public ICommand InstallTunnelCommand { get; }
    public ICommand RemoveTunnelCommand { get; }

    public string TargetHost { get => _targetHost; set { _targetHost = value; OnPropertyChanged(); } }
    public string TargetPort { get => _targetPort; set { _targetPort = value; OnPropertyChanged(); } }
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
    public string ConfigPath { get => _configPath; set { _configPath = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); } }
    public string TunnelName { get => _tunnelName; set { _tunnelName = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); } }
    public GameProcessInfo? SelectedGame { get => _selectedGame; set { _selectedGame = value; OnPropertyChanged(); } }
    public bool IsBusy { get => _busy; private set { _busy = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); } }
    public bool WireGuardAvailable => _tunnel.IsAvailable;

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        RefreshGames();
    }

    private void RefreshGames()
    {
        RunningGames.Clear();
        foreach (var game in _detector.FindRunningGames()) RunningGames.Add(game);
        Status = RunningGames.Count == 0
            ? "Nenhum jogo conhecido detectado. A medição manual continua disponível."
            : $"{RunningGames.Count} processo(s) de jogo detectado(s). A detecção não altera o tráfego.";
    }

    private async Task MeasureRouteAsync()
    {
        if (!int.TryParse(TargetPort, out int port) || port is < 1 or > 65535)
        {
            Status = "Porta inválida. Informe um número entre 1 e 65535.";
            return;
        }
        IsBusy = true;
        _operationCts = new CancellationTokenSource();
        try
        {
            Status = $"Medindo handshake TCP para {TargetHost}:{port}. Isso não é o ping interno da partida.";
            var result = await _probe.MeasureTcpAsync(TargetHost.Trim(), port, 7, _operationCts.Token);
            Measurements.Insert(0, result with { Label = _tunnelInstalled ? "Após instalar túnel (handshake não verificado)" : "Direta (sem túnel)" });
            while (Measurements.Count > 20) Measurements.RemoveAt(Measurements.Count - 1);
            Status = result.AverageMs.HasValue
                ? $"Medição concluída: {result.AverageMs:F1} ms TCP, jitter {result.JitterMs?.ToString("F1") ?? "N/D"} ms, perda de conexão {result.PacketLossPercent:F1}%."
                : result.Status;
        }
        catch (Exception ex) { Status = $"Falha na medição: {ex.Message}"; }
        finally { _operationCts.Dispose(); _operationCts = null; IsBusy = false; }
    }

    private async Task TraceRouteAsync()
    {
        IsBusy = true;
        _operationCts = new CancellationTokenSource();
        try
        {
            TraceHops.Clear();
            Status = $"Executando traceroute ICMP para {TargetHost}. Alguns roteadores podem não responder.";
            foreach (var hop in await _probe.TraceRouteAsync(TargetHost.Trim(), 16, _operationCts.Token))
                TraceHops.Add(hop);
            Status = $"Traceroute finalizado: {TraceHops.Count} salto(s) observados. Saltos sem resposta não significam necessariamente perda de tráfego do jogo.";
        }
        catch (Exception ex) { Status = $"Traceroute indisponível: {ex.Message}"; }
        finally { _operationCts.Dispose(); _operationCts = null; IsBusy = false; }
    }

    private void ImportConfig()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar configuração WireGuard fornecida pelo seu provedor ou servidor",
            Filter = "WireGuard config (*.conf)|*.conf|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;
        ConfigPath = dialog.FileName;
        TunnelName = Path.GetFileNameWithoutExtension(dialog.FileName);
        Status = "Perfil selecionado. Revise o provedor, Endpoint e AllowedIPs antes de instalar. A chave privada permanece no arquivo selecionado.";
    }

    private async Task InstallTunnelAsync()
    {
        if (MessageBox.Show(
            "Instalar este túnel WireGuard criará um serviço de rede no Windows e poderá alterar as rotas do sistema. Se AllowedIPs incluir 0.0.0.0/0 ou ::/0, TODO o tráfego poderá passar pelo túnel. O roteamento por processo/jogo não está implementado. Deseja continuar?",
            "Confirmação de alteração de rede",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            var result = await _tunnel.InstallAsync(ConfigPath);
            if (result.Success && !string.IsNullOrWhiteSpace(result.TunnelName))
            {
                TunnelName = result.TunnelName;
                _tunnelInstalled = true;
            }
            Status = result.Message;
        }
        finally { IsBusy = false; }
    }

    private async Task RemoveTunnelAsync()
    {
        if (MessageBox.Show(
            $"Remover o serviço WireGuard '{TunnelName}' e restaurar as rotas gerenciadas por ele?",
            "Confirmar remoção do túnel",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        IsBusy = true;
        try
        {
            var result = await _tunnel.RemoveAsync(TunnelName);
            if (result.Success) _tunnelInstalled = false;
            Status = result.Message;
        }
        finally { IsBusy = false; }
    }
}
