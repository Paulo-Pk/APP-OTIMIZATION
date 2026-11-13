using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using PCControlUltimate.App.ViewModels;

namespace PCControlUltimate.App;

public partial class MainWindow : Window
{
    private const string AppHost = "app.pccontrol.local";
    private readonly string _agentSessionToken;

    public MainWindow(MainViewModel viewModel, string agentSessionToken)
    {
        InitializeComponent();
        DataContext = viewModel;
        _agentSessionToken = agentSessionToken ?? string.Empty;
        Loaded += async (_, _) => await InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            string webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            string entryPoint = Path.Combine(webRoot, "index.html");
            if (!File.Exists(entryPoint))
            {
                ShowStartupError("A interface React não foi incluída nesta compilação. Execute primeiro o build do frontend (bun run build) e depois compile o aplicativo Windows.");
                return;
            }

            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PCControlUltimate", "WebView2");
            Directory.CreateDirectory(userData);

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userData);
            await AppWebView.EnsureCoreWebView2Async(environment);

            AppWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            AppWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            AppWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            AppWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            AppWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                AppHost, webRoot, CoreWebView2HostResourceAccessKind.DenyCors);

            // Only the bundled first-party app is allowed to navigate inside the WebView.
            AppWebView.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var target) ||
                    !string.Equals(target.Host, AppHost, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(target.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    args.Cancel = true;
                }
            };

            // Inject the short-lived DPAPI-backed agent session token before React modules execute.
            string tokenLiteral = JsonSerializer.Serialize(_agentSessionToken);
            await AppWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                $"window.__PCCONTROL_AGENT_TOKEN__ = {tokenLiteral};");

            AppWebView.CoreWebView2.ProcessFailed += (_, _) =>
                Dispatcher.Invoke(() => ShowStartupError("A interface foi interrompida pelo WebView2. Feche e abra o PC Control Ultimate novamente."));

            AppWebView.Source = new Uri($"https://{AppHost}/index.html");
            StartupOverlay.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowStartupError($"Não foi possível iniciar a interface integrada: {ex.Message}");
        }
    }

    private void ShowStartupError(string message)
    {
        StartupOverlay.Visibility = Visibility.Visible;
        StartupStatus.Text = message;
    }
}