using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace PCControlUltimate.App.Services;

public class LocalizationService : INotifyPropertyChanged
{
    private string _currentCulture = "pt-BR";

    public static string PreferencesFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCControlUltimate",
        "user_preferences.json"
    );

    private static readonly Dictionary<string, Dictionary<string, string>> Dictionaries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pt-BR"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AppTitle"] = "PC CONTROL ULTIMATE v2.6.0 — CONTROLE DO PC",
            ["HeaderSubtitle"] = "CONTROLE DO PC · DESEMPENHO, HARDWARE E JOGOS",
            ["HeaderDesc"] = "Acompanhe temperaturas e desempenho. Faça ajustes no Windows com segurança e veja o que está acontecendo no seu PC.",
            ["NavSection"] = "MENU PRINCIPAL",
            ["NavDashboard"] = "Painel",
            ["NavHardware"] = "Monitorar hardware",
            ["NavGpuHealth"] = "Saúde da placa de vídeo",
            ["NavBoostCenter"] = "Otimizar o PC",
            ["NavGameCenter"] = "Jogos",
            ["NavGaming"] = "FPS e desempenho",
            ["NavBenchmarks"] = "Testar desempenho",
            ["NavDiagnostics"] = "Diagnóstico",
            ["NavTweaks"] = "Ajustes do Windows",
            ["NavDebloat"] = "Limpeza do PC",
            ["NavNetwork"] = "Internet e rede",
            ["NavAccelerator"] = "Rede para jogos",
            ["NavPeripherals"] = "Mouse, teclado e tela",
            ["NavDrivers"] = "Drivers",
            ["NavSoftwareHub"] = "Instalar programas",
            ["NavRestore"] = "Restaurar alterações",
            ["NavAiAssistant"] = "Ajuda e recomendações",
            ["ViewTechnicalDetails"] = "Ver detalhes técnicos",
            ["BackupStateHeader"] = "ESTADO DO ÚLTIMO BACKUP",
            ["ActiveModuleHeader"] = "MÓDULO ATIVO",
            ["BtnSwitchLang"] = "PT-BR / EN-US",
            ["BtnElevateUac"] = "Elevar UAC",
            ["BtnCreateBackup"] = "Backup Verificado",
            ["BtnRefreshAll"] = "Atualizar Sensores",
            ["SensorUnavailable"] = "Não disponível (N/D)",
            ["SensorStatusAvailable"] = "Disponível (Leitura Física Real)",
            ["SensorStatusNotSupported"] = "Sensor não suportado pelo hardware",
            ["SensorStatusUnavailable"] = "Leitura indisponível no momento (requer privilégio/driver)",
            ["SensorStatusCommFailure"] = "Falha de comunicação com subsistema de sensores",
            ["NotDetected"] = "Não detectado",
            ["AgentConnectedAdmin"] = "Agente Nativo Conectado · Admin / HMAC-SHA256",
            ["AgentConnectedStandard"] = "Agente Nativo Conectado · Padrão / HMAC-SHA256",
            ["AgentDisconnected"] = "Agente Local Desconectado",
            ["CardCpuTitle"] = "PROCESSADOR (CPU)",
            ["CardGpuTitle"] = "PLACA DE VÍDEO DEDICADA (GPU)",
            ["CardRamTitle"] = "MEMÓRIA RAM FÍSICA",
            ["CardDisplayTitle"] = "MONITOR PRIMÁRIO (WIN32)",
            ["HardwareStateHeader"] = "LEITURA DOS SENSORES DO PC",
            ["SensorsListTitle"] = "SENSORES DETECTADOS",
            ["LblCpuClockPower"] = "Frequência e consumo da CPU",
            ["LblGpuClockPower"] = "Frequência e consumo da GPU",
            ["LblGpuVram"] = "GPU VRAM Alocada",
            ["LblGpuFanRpm"] = "GPU Rotação Ventoinha",
            ["GpuHealthTitle"] = "SAÚDE DA PLACA DE VÍDEO",
            ["GpuHealthNotice"] = "Veja a temperatura, o uso e o aquecimento da placa de vídeo. Nenhuma alteração de clock ou tensão é feita automaticamente.",
            ["BoostCenterTitle"] = "OTIMIZAÇÃO DO PC",
            ["BoostCenterSubtitle"] = "Ative ajustes seguros e reversíveis. Um backup é criado antes de aplicar as alterações.",
            ["BtnApplySafeBoost"] = "Aplicar otimizações seguras",
            ["GameCenterTitle"] = "JOGOS INSTALADOS E EM EXECUÇÃO",
            ["GameCenterSubtitle"] = "Veja os jogos detectados e acesse as ferramentas de desempenho.",
            ["BenchmarksTitle"] = "TESTE DE DESEMPENHO",
            ["BenchmarksSubtitle"] = "Faça um teste local do processador e da memória deste computador.",
            ["BtnRunBenchmark"] = "Iniciar teste",
            ["PeripheralsTitle"] = "MOUSE, TECLADO E MONITOR",
            ["PeripheralsSubtitle"] = "Consulte a resolução e a frequência do monitor. A medição da taxa de polling do mouse e teclado ainda não está disponível.",
            ["BtnTestInputCadence"] = "Verificar monitor e timer",
            ["SoftwareHubTitle"] = "INSTALAR PROGRAMAS",
            ["SoftwareHubSubtitle"] = "Encontre programas conhecidos e instale usando as fontes oficiais do Windows.",
            ["AiAssistantTitle"] = "AJUDA E RECOMENDAÇÕES",
            ["AiAssistantSubtitle"] = "Entenda os dados do seu PC e veja sugestões baseadas nas leituras disponíveis.",
            ["BtnGenerateInsights"] = "Atualizar Análise Técnica do Sistema",
            ["TweaksTitle"] = "AJUSTES DO WINDOWS",
            ["TweaksSubtitle"] = "Ative ou reverta ajustes do Windows. As alterações ficam registradas para que você possa desfazê-las.",
            ["BtnApplyTweak"] = "Aplicar Ajuste",
            ["BtnRestoreTweak"] = "Reverter Ajuste",
            ["RestoreTitle"] = "RESTAURAR ALTERAÇÕES",
            ["RestoreChecksumNote"] = "O backup é verificado antes da restauração. Se a verificação falhar, nenhuma alteração será restaurada.",
            ["BtnRefreshBackups"] = "Atualizar Snapshots",
            ["BtnVerifyBackup"] = "Verificar SHA-256",
            ["BtnRestoreFullBackup"] = "Restaurar Snapshot Selecionado",
            ["BtnRestoreSingleItem"] = "Restaurar Item",
            ["ChkConfirmRestoreExecution"] = "Confirmo explicitamente a execução da restauração dos itens listados",
            ["ChkConfirmIncomplete"] = "Permitir restauração mesmo se o snapshot registrou chaves inacessíveis na captura",
            ["ChkRollbackOnError"] = "Executar rollback automático caso algum item falhe durante a restauração",
            ["RestoreSnapshotsHeader"] = "SNAPSHOTS DISPONÍVEIS (DATA/HORA, TIPO, TAMANHO, STATUS E HASH SHA-256)",
            ["RestorePreviewHeader"] = "PRÉVIA DE ESCOPO — ITENS QUE SERÃO RESTAURADOS DO SNAPSHOT SELECIONADO",
            ["RestoreItemResultsHeader"] = "RESULTADO INDIVIDUAL POR ITEM RESTAURADO",
            ["RestoreUacWarning"] = "Alerta UAC: Este snapshot contém chaves HKLM (Sistema). Execute com elevação de Administrador (UAC) para evitar bloqueio de permissão.",
            ["RestoreUacOk"] = "Privilégio UAC Verificado: Sessão administrativa ou chaves exclusivamente de usuário (HKCU).",
            ["RestoreConfirmRequiredMsg"] = "Confirmação Obrigatória: Marque a caixa 'Confirmo explicitamente a execução da restauração' antes de prosseguir.",
            ["RestoreIntegrityBlockedMsg"] = "Restauração Bloqueada: O snapshot selecionado falhou na verificação de integridade SHA-256.",
            ["RestoreProgressIdle"] = "Pronto para verificação ou restauração.",
            ["RestoreProgressRunning"] = "Executando restauração verificada e validando catálogo fechado...",
            ["RestoreProgressDone"] = "Operação de restauração finalizada.",
            ["DebloatTitle"] = "LIMPEZA DO PC",
            ["DebloatSubtitle"] = "Veja primeiro o que pode ser removido. Arquivos pessoais e pastas protegidas não são incluídos.",
            ["BtnDryRunScan"] = "Simular Limpeza (Dry-Run)",
            ["BtnExecutePurge"] = "Executar Limpeza Real",
            ["ChkConfirmDeletion"] = "Confirmo explicitamente a exclusão dos arquivos temporários desta categoria",
            ["NetworkTitle"] = "INTERNET E REDE",
            ["NetworkSubtitle"] = "Teste a conexão, o ping, o DNS e a velocidade de download.",
            ["BtnRunNetworkDiag"] = "Executar Diagnóstico ICMP / TCP / DNS",
            ["BtnRunThroughput"] = "Medir Throughput Externo (Cloudflare CDN)",
            ["GamingTitle"] = "FPS E DESEMPENHO EM JOGOS",
            ["GamingSubtitle"] = "Escolha um jogo e faça uma captura para conferir FPS médio e estabilidade.",
            ["BtnCheckPresentMon"] = "Verificar PresentMon",
            ["BtnRefreshProcesses"] = "Listar Processos",
            ["BtnStartCapture"] = "Iniciar Captura PresentMon",
            ["BtnCancelCapture"] = "Cancelar Captura",
            ["DiagnosticsTitle"] = "DIAGNÓSTICO DO PC",
            ["DiagnosticsSubtitle"] = "Consulte o estado do sistema, os sensores e o histórico de alterações.",
            ["BtnRefreshAuditLogs"] = "Atualizar Auditoria",
            ["DriversTitle"] = "DRIVERS INSTALADOS",
            ["DriversSubtitle"] = "Veja os drivers encontrados no Windows. O aplicativo não instala nem altera drivers sem sua autorização.",
            ["BtnRefreshDrivers"] = "Atualizar Inventário WMI",
            ["AcceleratorTitle"] = "REDE PARA JOGOS",
            ["AcceleratorSubtitle"] = "Compare a conexão direta com uma rota alternativa. O resultado depende do servidor e da sua conexão.",
            ["BtnDetectGameSockets"] = "Detectar Jogos Ativos",
            ["BtnAnalyzeRoute"] = "Avaliar Rota Multi-Amostra",
            ["BtnRunTraceroute"] = "Executar Traceroute (TTL)",
            ["BtnAuditWgConfig"] = "Auditar Config WireGuard (AllowedIPs)",
            ["BtnQueryWgTunnel"] = "Verificar Serviço & Handshake",
            ["BtnAddRelayNode"] = "Cadastrar Nó WireGuard / VPS",
            ["FooterLeftText"] = "PC Control Ultimate v2.5.0 · Solução Desktop Nativa .NET 10 WPF · 17 Módulos · Ledger DPAPI/HMAC + SQLite",
            ["FooterRightText"] = "IPC Loopback Autenticado: 127.0.0.1:52194 (HMAC-SHA256)"
        },
        ["en-US"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AppTitle"] = "PC CONTROL ULTIMATE v2.6.0 — PC CONTROL CENTER",
            ["HeaderSubtitle"] = "PC CONTROL · PERFORMANCE, HARDWARE AND GAMING",
            ["HeaderDesc"] = "Monitor temperatures and performance. Apply Windows changes safely and understand what is happening on your PC.",
            ["NavSection"] = "MAIN MENU",
            ["NavDashboard"] = "Home",
            ["NavHardware"] = "Hardware monitor",
            ["NavGpuHealth"] = "Graphics card health",
            ["NavBoostCenter"] = "Optimize PC",
            ["NavGameCenter"] = "Games",
            ["NavGaming"] = "FPS and performance",
            ["NavBenchmarks"] = "Performance test",
            ["NavDiagnostics"] = "Diagnostics",
            ["NavTweaks"] = "Windows settings",
            ["NavDebloat"] = "PC cleanup",
            ["NavNetwork"] = "Internet and network",
            ["NavAccelerator"] = "Gaming network",
            ["NavPeripherals"] = "Mouse, keyboard and display",
            ["NavDrivers"] = "Drivers",
            ["NavSoftwareHub"] = "Install apps",
            ["NavRestore"] = "Restore changes",
            ["NavAiAssistant"] = "Help and recommendations",
            ["ViewTechnicalDetails"] = "View technical details",
            ["BackupStateHeader"] = "LAST BACKUP STATUS",
            ["ActiveModuleHeader"] = "ACTIVE MODULE",
            ["BtnSwitchLang"] = "EN-US / PT-BR",
            ["BtnElevateUac"] = "Elevate UAC",
            ["BtnCreateBackup"] = "Verified Backup",
            ["BtnRefreshAll"] = "Refresh Sensors",
            ["SensorUnavailable"] = "Not available (N/A)",
            ["SensorStatusAvailable"] = "Available (Real Physical Reading)",
            ["SensorStatusNotSupported"] = "Sensor not supported by hardware",
            ["SensorStatusUnavailable"] = "Reading currently unavailable (requires privilege/driver)",
            ["SensorStatusCommFailure"] = "Sensor subsystem communication failure",
            ["NotDetected"] = "Not detected",
            ["AgentConnectedAdmin"] = "Native Agent Connected · Admin / HMAC-SHA256",
            ["AgentConnectedStandard"] = "Native Agent Connected · Standard / HMAC-SHA256",
            ["AgentDisconnected"] = "Local Agent Disconnected",
            ["CardCpuTitle"] = "PROCESSOR (CPU)",
            ["CardGpuTitle"] = "DEDICATED GRAPHICS CARD (GPU)",
            ["CardRamTitle"] = "PHYSICAL RAM MEMORY",
            ["CardDisplayTitle"] = "PRIMARY DISPLAY (WIN32)",
            ["HardwareStateHeader"] = "PC SENSOR READINGS",
            ["SensorsListTitle"] = "DETECTED SENSORS",
            ["LblCpuClockPower"] = "CPU frequency and power",
            ["LblGpuClockPower"] = "GPU frequency and power",
            ["LblGpuVram"] = "GPU Allocated VRAM",
            ["LblGpuFanRpm"] = "GPU Fan Speed",
            ["GpuHealthTitle"] = "GRAPHICS CARD HEALTH",
            ["GpuHealthNotice"] = "View graphics card temperature, usage, and heat. Clock and voltage are never changed automatically.",
            ["BoostCenterTitle"] = "PC OPTIMIZATION",
            ["BoostCenterSubtitle"] = "Enable safe, reversible settings. A backup is created before changes are applied.",
            ["BtnApplySafeBoost"] = "Apply safe optimizations",
            ["GameCenterTitle"] = "INSTALLED AND RUNNING GAMES",
            ["GameCenterSubtitle"] = "See detected games and open performance tools.",
            ["BenchmarksTitle"] = "PERFORMANCE TEST",
            ["BenchmarksSubtitle"] = "Run a local test of this computer's processor and memory.",
            ["BtnRunBenchmark"] = "Start test",
            ["PeripheralsTitle"] = "MOUSE, KEYBOARD AND DISPLAY",
            ["PeripheralsSubtitle"] = "Check display resolution and refresh rate. Mouse and keyboard polling-rate measurement is not available yet.",
            ["BtnTestInputCadence"] = "Check display and timer",
            ["SoftwareHubTitle"] = "INSTALL APPS",
            ["SoftwareHubSubtitle"] = "Find common apps and install them from official Windows sources.",
            ["AiAssistantTitle"] = "HELP AND RECOMMENDATIONS",
            ["AiAssistantSubtitle"] = "Understand your PC readings and see suggestions based on available data.",
            ["BtnGenerateInsights"] = "Refresh System Technical Analysis",
            ["TweaksTitle"] = "WINDOWS SETTINGS",
            ["TweaksSubtitle"] = "Enable or revert Windows settings. Changes are recorded so you can undo them.",
            ["BtnApplyTweak"] = "Apply Tweak",
            ["BtnRestoreTweak"] = "Revert Tweak",
            ["RestoreTitle"] = "RESTORE CHANGES",
            ["RestoreChecksumNote"] = "The backup is verified before restoration. If verification fails, no changes are restored.",
            ["BtnRefreshBackups"] = "Refresh Snapshots",
            ["BtnVerifyBackup"] = "Verify SHA-256",
            ["BtnRestoreFullBackup"] = "Restore Selected Snapshot",
            ["BtnRestoreSingleItem"] = "Restore Item",
            ["ChkConfirmRestoreExecution"] = "I explicitly confirm executing the restoration of the items listed",
            ["ChkConfirmIncomplete"] = "Allow restoration even if the snapshot recorded unreadable keys during capture",
            ["ChkRollbackOnError"] = "Execute automatic rollback if any item fails during restoration",
            ["RestoreSnapshotsHeader"] = "AVAILABLE SNAPSHOTS (DATE/TIME, TYPE, SIZE, INTEGRITY STATUS & SHA-256 HASH)",
            ["RestorePreviewHeader"] = "SCOPE PREVIEW — ITEMS TO BE RESTORED FROM SELECTED SNAPSHOT",
            ["RestoreItemResultsHeader"] = "INDIVIDUAL RESULT PER RESTORED ITEM",
            ["RestoreUacWarning"] = "UAC Alert: This snapshot contains HKLM (System) keys. Run with Administrator (UAC) elevation to avoid permission denial.",
            ["RestoreUacOk"] = "UAC Privilege Verified: Administrative session active or user-only (HKCU) keys.",
            ["RestoreConfirmRequiredMsg"] = "Confirmation Required: Check 'I explicitly confirm executing the restoration' before proceeding.",
            ["RestoreIntegrityBlockedMsg"] = "Restoration Blocked: Selected snapshot failed SHA-256 integrity verification.",
            ["RestoreProgressIdle"] = "Ready for verification or restoration.",
            ["RestoreProgressRunning"] = "Executing verified restoration and validating closed catalog...",
            ["RestoreProgressDone"] = "Restoration operation finished.",
            ["DebloatTitle"] = "PC CLEANUP",
            ["DebloatSubtitle"] = "Review what can be removed first. Personal files and protected folders are excluded.",
            ["BtnDryRunScan"] = "Simulate Cleanup (Dry-Run)",
            ["BtnExecutePurge"] = "Execute Real Cleanup",
            ["ChkConfirmDeletion"] = "I explicitly confirm deleting temporary files in this category",
            ["NetworkTitle"] = "INTERNET AND NETWORK",
            ["NetworkSubtitle"] = "Test connectivity, ping, DNS, and download speed.",
            ["BtnRunNetworkDiag"] = "Run ICMP / TCP / DNS Diagnostics",
            ["BtnRunThroughput"] = "Measure External Throughput (Cloudflare CDN)",
            ["GamingTitle"] = "FPS AND GAMING PERFORMANCE",
            ["GamingSubtitle"] = "Choose a game and capture a session to check average FPS and stability.",
            ["BtnCheckPresentMon"] = "Verify PresentMon",
            ["BtnRefreshProcesses"] = "List Processes",
            ["BtnStartCapture"] = "Start PresentMon Capture",
            ["BtnCancelCapture"] = "Cancel Capture",
            ["DiagnosticsTitle"] = "PC DIAGNOSTICS",
            ["DiagnosticsSubtitle"] = "Review system status, sensors, and change history.",
            ["BtnRefreshAuditLogs"] = "Refresh Audit Trail",
            ["DriversTitle"] = "INSTALLED DRIVERS",
            ["DriversSubtitle"] = "View drivers detected by Windows. The app does not install or change drivers without your approval.",
            ["BtnRefreshDrivers"] = "Refresh WMI Inventory",
            ["AcceleratorTitle"] = "GAMING NETWORK",
            ["AcceleratorSubtitle"] = "Compare your direct connection with an alternative route. Results depend on the game server and your connection.",
            ["BtnDetectGameSockets"] = "Detect Active Games",
            ["BtnAnalyzeRoute"] = "Evaluate Multi-Sample Route",
            ["BtnRunTraceroute"] = "Run Traceroute (TTL)",
            ["BtnAuditWgConfig"] = "Audit WireGuard Config (AllowedIPs)",
            ["BtnQueryWgTunnel"] = "Verify Service & Handshake",
            ["BtnAddRelayNode"] = "Register WireGuard / VPS Node",
            ["FooterLeftText"] = "PC Control Ultimate v2.5.0 · Native .NET 10 WPF Desktop Suite · 17 Modules · DPAPI/HMAC Ledger + SQLite",
            ["FooterRightText"] = "Authenticated IPC Loopback: 127.0.0.1:52194 (HMAC-SHA256)"
        }
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    public LocalizationService()
    {
        LoadPersistedLanguage();
    }

    public string CurrentCulture
    {
        get => _currentCulture;
        set
        {
            string normalized = string.Equals(value, "en-US", StringComparison.OrdinalIgnoreCase) ? "en-US" : "pt-BR";
            if (_currentCulture != normalized)
            {
                _currentCulture = normalized;
                PersistLanguage(_currentCulture);
                OnPropertyChanged();
                OnPropertyChanged("Item[]");
            }
        }
    }

    public string this[string key]
    {
        get
        {
            if (Dictionaries.TryGetValue(_currentCulture, out var dict) && dict.TryGetValue(key, out var val))
            {
                return val;
            }
            if (Dictionaries["pt-BR"].TryGetValue(key, out var fallback))
            {
                return fallback;
            }
            return key;
        }
    }

    public void ToggleLanguage()
    {
        CurrentCulture = _currentCulture == "pt-BR" ? "en-US" : "pt-BR";
    }

    private void LoadPersistedLanguage()
    {
        try
        {
            if (File.Exists(PreferencesFilePath))
            {
                string json = File.ReadAllText(PreferencesFilePath, Encoding.UTF8);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("language", out var langEl))
                {
                    string? lang = langEl.GetString();
                    if (!string.IsNullOrWhiteSpace(lang))
                    {
                        _currentCulture = string.Equals(lang, "en-US", StringComparison.OrdinalIgnoreCase) ? "en-US" : "pt-BR";
                    }
                }
            }
        }
        catch { }
    }

    private static void PersistLanguage(string language)
    {
        try
        {
            string? dir = Path.GetDirectoryName(PreferencesFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(new { language, updatedUtc = DateTime.UtcNow.ToString("O") });
            File.WriteAllText(PreferencesFilePath, json, Encoding.UTF8);
        }
        catch { }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
