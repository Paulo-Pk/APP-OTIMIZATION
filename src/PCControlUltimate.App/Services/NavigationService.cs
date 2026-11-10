using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PCControlUltimate.App.ViewModels;

namespace PCControlUltimate.App.Services;

public interface INavigationService : INotifyPropertyChanged
{
    string CurrentModuleKey { get; }
    ModuleViewModelBase CurrentViewModel { get; }
    void NavigateTo(string moduleKey);
}

public class NavigationService : INavigationService
{
    private readonly Dictionary<string, Func<ModuleViewModelBase>> _factories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModuleViewModelBase> _instances = new(StringComparer.OrdinalIgnoreCase);

    private string _currentModuleKey = "dashboard";
    private ModuleViewModelBase _currentViewModel = null!;

    public event PropertyChangedEventHandler? PropertyChanged;

    public NavigationService(MainViewModel workspace)
    {
        RegisterModule("dashboard", () => new DashboardViewModel(workspace));
        RegisterModule("hardware", () => new HardwareMonitorViewModel(workspace));
        RegisterModule("tweaks", () => new RegistryOptimizerViewModel(workspace));
        RegisterModule("restore", () => new RestoreCenterViewModel(workspace));
        RegisterModule("debloat", () => new DebloatCleanerViewModel(workspace));
        RegisterModule("network", () => new NetworkLabViewModel(workspace));
        RegisterModule("gaming", () => new GamingPresentMonViewModel(workspace));
        RegisterModule("diagnostics", () => new DiagnosticsViewModel(workspace));
        RegisterModule("drivers", () => new DriverCenterViewModel(workspace));

        NavigateTo("dashboard");
    }

    public string CurrentModuleKey
    {
        get => _currentModuleKey;
        private set
        {
            if (!string.Equals(_currentModuleKey, value, StringComparison.OrdinalIgnoreCase))
            {
                _currentModuleKey = value;
                OnPropertyChanged();
            }
        }
    }

    public ModuleViewModelBase CurrentViewModel
    {
        get => _currentViewModel;
        private set
        {
            if (!ReferenceEquals(_currentViewModel, value))
            {
                _currentViewModel = value;
                OnPropertyChanged();
            }
        }
    }

    public void RegisterModule(string moduleKey, Func<ModuleViewModelBase> factory)
    {
        _factories[moduleKey] = factory;
    }

    public void NavigateTo(string moduleKey)
    {
        string normalized = string.IsNullOrWhiteSpace(moduleKey) ? "dashboard" : moduleKey.Trim().ToLowerInvariant();
        if (!_factories.ContainsKey(normalized))
        {
            normalized = "dashboard";
        }

        if (!_instances.TryGetValue(normalized, out var viewModel))
        {
            viewModel = _factories[normalized]();
            _instances[normalized] = viewModel;
        }

        CurrentModuleKey = normalized;
        CurrentViewModel = viewModel;
        viewModel.OnNavigatedTo();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
