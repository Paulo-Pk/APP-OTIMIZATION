using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace PCControlUltimate.App;

public partial class MainWindow : Window
{
    private const string AppHost = "app.pccontrol.local";
    private readonly string _agentSessionToken;

    public MainWindow(string agentSessionToken)
    {
        InitializeComponent();
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

            // Expose only a narrowly scoped native executable picker to the trusted bundled UI.
            // The browser never supplies a path; the user chooses the file in the Windows dialog.
            AppWebView.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                try
                {
                    if (!Uri.TryCreate(args.Source, UriKind.Absolute, out var source) ||
                        !string.Equals(source.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(source.Host, AppHost, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    string message = args.TryGetWebMessageAsString();
                    using var messageDocument = JsonDocument.Parse(message);
                    var root = messageDocument.RootElement;
                    if (!root.TryGetProperty("type", out var typeElement) ||
                        typeElement.GetString() != "pccontrol:pick-executable" ||
                        !root.TryGetProperty("requestId", out var requestElement))
                    {
                        return;
                    }

                    string requestId = requestElement.GetString() ?? string.Empty;
                    if (requestId.Length is < 8 or > 80 ||
                        requestId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
                    {
                        return;
                    }

                    var dialog = new OpenFileDialog
                    {
                        Title = "Selecione o executável do jogo",
                        Filter = "Aplicativos Windows (*.exe)|*.exe",
                        CheckFileExists = true,
                        CheckPathExists = true,
                        Multiselect = false,
                        DereferenceLinks = true
                    };

                    string? selectedPath = dialog.ShowDialog(this) == true
                        ? Path.GetFullPath(dialog.FileName)
                        : null;
                    if (!string.IsNullOrWhiteSpace(selectedPath) &&
                        (!File.Exists(selectedPath) ||
                         !string.Equals(Path.GetExtension(selectedPath), ".exe", StringComparison.OrdinalIgnoreCase)))
                    {
                        selectedPath = null;
                    }

                    string? title = selectedPath == null ? null : Path.GetFileNameWithoutExtension(selectedPath);
                    string response = JsonSerializer.Serialize(new
                    {
                        type = "pccontrol:pick-executable-result",
                        requestId,
                        path = selectedPath,
                        title
                    });
                    AppWebView.CoreWebView2.PostWebMessageAsJson(response);
                }
                catch
                {
                    // Invalid messages and cancelled dialogs must not interrupt the desktop host.
                }
            };

            AppWebView.CoreWebView2.ProcessFailed += (_, _) =>
                Dispatcher.Invoke(() => ShowStartupError("A interface foi interrompida pelo WebView2. Feche e abra o PC Control Ultimate novamente."));

            AppWebView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    StartupOverlay.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ShowStartupError($"Falha ao carregar a interface React (WebErrorStatus: {args.WebErrorStatus}).");
                }
            };

            AppWebView.Source = new Uri($"https://{AppHost}/index.html");
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