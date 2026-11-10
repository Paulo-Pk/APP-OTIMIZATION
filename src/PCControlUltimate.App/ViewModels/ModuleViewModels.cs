using System.ComponentModel;
using System.Runtime.CompilerServices;
using PCControlUltimate.App.Services;

namespace PCControlUltimate.App.ViewModels;

public abstract class ModuleViewModelBase : INotifyPropertyChanged
{
    protected ModuleViewModelBase(MainViewModel workspace, string moduleKey, string titleKey)
    {
        Workspace = workspace;
        ModuleKey = moduleKey;
        TitleKey = titleKey;
    }

    public MainViewModel Workspace { get; }
    public LocalizationService Loc => Workspace.Loc;
    public string ModuleKey { get; }
    public string TitleKey { get; }
    public string ModuleTitle => Loc[TitleKey];

    public virtual void OnNavigatedTo()
    {
        OnPropertyChanged(nameof(ModuleTitle));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class DashboardViewModel : ModuleViewModelBase
{
    public DashboardViewModel(MainViewModel workspace)
        : base(workspace, "dashboard", "NavDashboard")
    {
    }
}

public sealed class HardwareMonitorViewModel : ModuleViewModelBase
{
    public HardwareMonitorViewModel(MainViewModel workspace)
        : base(workspace, "hardware", "NavHardware")
    {
    }
}

public sealed class RegistryOptimizerViewModel : ModuleViewModelBase
{
    public RegistryOptimizerViewModel(MainViewModel workspace)
        : base(workspace, "tweaks", "NavTweaks")
    {
    }
}

public sealed class RestoreCenterViewModel : ModuleViewModelBase
{
    public RestoreCenterViewModel(MainViewModel workspace)
        : base(workspace, "restore", "NavRestore")
    {
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        Workspace.RefreshBackupsCommand.Execute(null);
    }
}

public sealed class DebloatCleanerViewModel : ModuleViewModelBase
{
    public DebloatCleanerViewModel(MainViewModel workspace)
        : base(workspace, "debloat", "NavDebloat")
    {
    }
}

public sealed class NetworkLabViewModel : ModuleViewModelBase
{
    public NetworkLabViewModel(MainViewModel workspace)
        : base(workspace, "network", "NavNetwork")
    {
    }
}

public sealed class GamingPresentMonViewModel : ModuleViewModelBase
{
    public GamingPresentMonViewModel(MainViewModel workspace)
        : base(workspace, "gaming", "NavGaming")
    {
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        if (Workspace.AvailableProcesses.Count == 0)
        {
            Workspace.RefreshProcessesCommand.Execute(null);
        }
    }
}

public sealed class DiagnosticsViewModel : ModuleViewModelBase
{
    public DiagnosticsViewModel(MainViewModel workspace)
        : base(workspace, "diagnostics", "NavDiagnostics")
    {
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        Workspace.RefreshAuditLogsCommand.Execute(null);
    }
}

public sealed class DriverCenterViewModel : ModuleViewModelBase
{
    public DriverCenterViewModel(MainViewModel workspace)
        : base(workspace, "drivers", "NavDrivers")
    {
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        if (Workspace.InstalledDrivers.Count == 0)
        {
            Workspace.RefreshDriversCommand.Execute(null);
        }
    }
}
