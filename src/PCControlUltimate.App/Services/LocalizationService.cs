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
            ["AppTitle"] = "PC CONTROL ULTIMATE v2.5.0 — Suíte Nativa Windows 10/11 x64",
            ["HeaderSubtitle"] = "PAINEL DE CONTROLE ULTIMATE · ENGENHARIA, TELEMETRIA E OTIMIZAÇÃO WINDOWS",
            ["HeaderDesc"] = "Telemetria de sensores físicos em tempo real, monitoramento térmico de GPU Hotspot, otimizações reversíveis DPAPI+HMAC e aceleração seletiva de rede.",
            ["NavSection"] = "MÓDULOS DO SISTEMA",
            ["NavDashboard"] = "Dashboard",
            ["NavHardware"] = "Hardware Monitor",
            ["NavGpuHealth"] = "GPU Health Lab",
            ["NavBoostCenter"] = "Boost Center",
            ["NavGameCenter"] = "Game Center",
            ["NavGaming"] = "Performance Lab",
            ["NavBenchmarks"] = "Benchmark Center",
            ["NavDiagnostics"] = "Diagnostics",
            ["NavTweaks"] = "Windows Tweaks",
            ["NavDebloat"] = "Debloat & Cleaner",
            ["NavNetwork"] = "Network Lab",
            ["NavAccelerator"] = "Game Network Accelerator",
            ["NavPeripherals"] = "Peripheral Test",
            ["NavDrivers"] = "Driver Center",
            ["NavSoftwareHub"] = "Software Hub",
            ["NavRestore"] = "Restore Center",
            ["NavAiAssistant"] = "AI Assistant",
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
            ["HardwareStateHeader"] = "SUBSISTEMA DE SENSORES FÍSICOS (LIBREHARDWAREMONITOR)",
            ["SensorsListTitle"] = "LIBREHARDWAREMONITOR — SENSORES FÍSICOS DETECTADOS",
            ["LblCpuClockPower"] = "CPU Clock / Potência",
            ["LblGpuClockPower"] = "GPU Clock / Potência",
            ["LblGpuVram"] = "GPU VRAM Alocada",
            ["LblGpuFanRpm"] = "GPU Rotação Ventoinha",
            ["GpuHealthTitle"] = "GPU HEALTH LAB — DIAGNÓSTICO TÉRMICO CORE VS. HOTSPOT (ΔT)",
            ["GpuHealthNotice"] = "Prioriza automaticamente GPUs dedicadas (AMD Radeon, NVIDIA GeForce/RTX, Intel Arc) sobre gráficos integrados (iGPU). Monitora Core, Hotspot e Delta ΔT sem aplicar overclock ou alterar tensões.",
            ["BoostCenterTitle"] = "BOOST CENTER — PERFIS REVERSÍVEIS DE ESTABILIDADE E JOGOS",
            ["BoostCenterSubtitle"] = "Aplica perfis seguros de energia, agendamento multimídia e redução de telemetria em segundo plano com snapshot prévio obrigatório.",
            ["BtnApplySafeBoost"] = "Executar Boost Seguro (Com Backup SHA-256)",
            ["GameCenterTitle"] = "GAME CENTER — DETECÇÃO DE JOGOS E PERFIS DE EXECUÇÃO",
            ["GameCenterSubtitle"] = "Identifica jogos em execução e seus sockets ativos sem injetar DLLs nem alterar memória do processo.",
            ["BenchmarksTitle"] = "BENCHMARK CENTER — AVALIAÇÃO SINTÉTICA DETERMINÍSTICA LOCAL",
            ["BenchmarksSubtitle"] = "Mede throughput aritmético de CPU, latência de alocação de memória e I/O sequencial sem inventar pontuações externas.",
            ["BtnRunBenchmark"] = "Executar Benchmark Determinístico",
            ["PeripheralsTitle"] = "PERIPHERAL TEST — TAXA DE ATUALIZAÇÃO WIN32 E CADÊNCIA DE INPUT",
            ["PeripheralsSubtitle"] = "Inspeciona resolução/Hz nativos do monitor primário e mede a cadência e jitter de eventos de entrada.",
            ["BtnTestInputCadence"] = "Aferir Cadência e Estabilidade de Periféricos",
            ["SoftwareHubTitle"] = "SOFTWARE HUB — CATÁLOGO VERIFICADO WINGET (FONTES OFICIAIS)",
            ["SoftwareHubSubtitle"] = "Exibe identificadores oficiais do Windows Package Manager (winget) para utilitários e runtimes essenciais, sem instalação silenciosa oculta.",
            ["AiAssistantTitle"] = "AI ASSISTANT — MOTOR CONSULTIVO DE DIAGNÓSTICO LOCAL",
            ["AiAssistantSubtitle"] = "Analisa telemetria térmica (Delta Hotspot), integridade de snapshots e comparação de rotas de rede com explicações técnicas transparentes.",
            ["BtnGenerateInsights"] = "Atualizar Análise Técnica do Sistema",
            ["TweaksTitle"] = "WINDOWS TWEAKS — CATÁLOGO AUDITADO DO REGISTRO (LEDGER DPAPI+HMAC)",
            ["TweaksSubtitle"] = "Leitura real de valor e tipo no Registro do Windows, verificação pós-escrita e pós-restauração, e documentação técnica detalhada por ajuste.",
            ["BtnApplyTweak"] = "Aplicar",
            ["BtnRestoreTweak"] = "Reverter",
            ["BtnRefreshTweakStates"] = "ATUALIZAR ESTADOS",
            ["BtnExplainTweak"] = "Explicação Detalhada",
            ["RestoreTitle"] = "RESTORE CENTER — SNAPSHOTS SHA-256, LEDGER DPAPI E ROLLBACK TRANSACIONAL",
            ["RestoreChecksumNote"] = "O hash SHA-256 valida a integridade estrutural do manifesto antes da restauração. A restauração é bloqueada automaticamente se a verificação falhar.",
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
            ["DebloatTitle"] = "DEBLOAT & CLEANER — SIMULAÇÃO (DRY-RUN) E LIMPEZA ANTI-SYMLINK",
            ["DebloatSubtitle"] = "Protege pastas pessoais e bloqueia ReparsePoints/Junctions NTFS. Nunca apresenta estimativa como espaço efetivamente liberado.",
            ["BtnDryRunScan"] = "Simular Limpeza (Dry-Run)",
            ["BtnExecutePurge"] = "Executar Limpeza Real",
            ["ChkConfirmDeletion"] = "Confirmo explicitamente a exclusão dos arquivos temporários desta categoria",
            ["NetworkTitle"] = "NETWORK LAB — DIAGNÓSTICO SEPARADO (ICMP, TCP, DNS E THROUGHPUT EXTERNO)",
            ["NetworkSubtitle"] = "Separa ping ICMP de handshake TCP e resolução DNS. Mede download real contra Cloudflare CDN, nunca contra localhost.",
            ["BtnRunNetworkDiag"] = "Executar Diagnóstico ICMP / TCP / DNS",
            ["BtnRunThroughput"] = "Medir Throughput Externo (Cloudflare CDN)",
            ["GamingTitle"] = "PERFORMANCE LAB — INTEL PRESENTMON & FRAMETIME TELEMETRY",
            ["GamingSubtitle"] = "Selecione um processo em execução e inicie uma sessão temporizada para aferir FPS médio, 1% Low e variação de frametime.",
            ["BtnCheckPresentMon"] = "Verificar PresentMon",
            ["BtnRefreshProcesses"] = "Listar Processos",
            ["BtnStartCapture"] = "Iniciar Captura PresentMon",
            ["BtnCancelCapture"] = "Cancelar Captura",
            ["DiagnosticsTitle"] = "DIAGNOSTICS — SEGURANÇA DPAPI/ACL, HMAC-SHA256 E MATRIZ DE MÓDULOS",
            ["DiagnosticsSubtitle"] = "Exibe postura real de proteção criptográfica, assinatura por requisição, elevação UAC e trilha auditável de alterações.",
            ["BtnRefreshAuditLogs"] = "Atualizar Auditoria",
            ["DriversTitle"] = "DRIVER CENTER — INVENTÁRIO SOMENTE-LEITURA DE DRIVERS ASSINADOS (WMI)",
            ["DriversSubtitle"] = "Lista fabricante, versão, data e assinatura digital via Win32_PnPSignedDriver sem baixar ou alterar drivers automaticamente.",
            ["BtnRefreshDrivers"] = "Atualizar Inventário WMI",
            ["AcceleratorTitle"] = "GAME NETWORK ACCELERATOR — ROTA MULTI-AMOSTRA E TÚNEL WIREGUARD SELETIVO",
            ["AcceleratorSubtitle"] = "Compara Rota Direta vs. Nós Intermediários com múltiplas amostras (ICMP/TCP/Jitter). Bloqueia ativação quando a rota direta é mais rápida ou estável.",
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
            ["AppTitle"] = "PC CONTROL ULTIMATE v2.5.0 — Native Windows 10/11 x64 Suite",
            ["HeaderSubtitle"] = "ULTIMATE CONTROL PANEL · WINDOWS ENGINEERING, TELEMETRY & OPTIMIZATION",
            ["HeaderDesc"] = "Real-time physical sensor telemetry, GPU Hotspot thermal diagnostics, reversible DPAPI+HMAC optimizations, and selective network acceleration.",
            ["NavSection"] = "SYSTEM MODULES",
            ["NavDashboard"] = "Dashboard",
            ["NavHardware"] = "Hardware Monitor",
            ["NavGpuHealth"] = "GPU Health Lab",
            ["NavBoostCenter"] = "Boost Center",
            ["NavGameCenter"] = "Game Center",
            ["NavGaming"] = "Performance Lab",
            ["NavBenchmarks"] = "Benchmark Center",
            ["NavDiagnostics"] = "Diagnostics",
            ["NavTweaks"] = "Windows Tweaks",
            ["NavDebloat"] = "Debloat & Cleaner",
            ["NavNetwork"] = "Network Lab",
            ["NavAccelerator"] = "Game Network Accelerator",
            ["NavPeripherals"] = "Peripheral Test",
            ["NavDrivers"] = "Driver Center",
            ["NavSoftwareHub"] = "Software Hub",
            ["NavRestore"] = "Restore Center",
            ["NavAiAssistant"] = "AI Assistant",
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
            ["HardwareStateHeader"] = "PHYSICAL SENSOR SUBSYSTEM (LIBREHARDWAREMONITOR)",
            ["SensorsListTitle"] = "LIBREHARDWAREMONITOR — DETECTED PHYSICAL SENSORS",
            ["LblCpuClockPower"] = "CPU Clock / Power",
            ["LblGpuClockPower"] = "GPU Clock / Power",
            ["LblGpuVram"] = "GPU Allocated VRAM",
            ["LblGpuFanRpm"] = "GPU Fan Speed",
            ["GpuHealthTitle"] = "GPU HEALTH LAB — CORE VS. HOTSPOT (ΔT) THERMAL DIAGNOSTICS",
            ["GpuHealthNotice"] = "Automatically prioritizes discrete GPUs (AMD Radeon, NVIDIA GeForce/RTX, Intel Arc) over integrated graphics (iGPU). Monitors Core, Hotspot, and ΔT without automatic overclocking or voltage changes.",
            ["BoostCenterTitle"] = "BOOST CENTER — REVERSIBLE STABILITY & GAMING PROFILES",
            ["BoostCenterSubtitle"] = "Applies safe power, multimedia scheduling, and background telemetry reduction profiles with mandatory pre-execution snapshots.",
            ["BtnApplySafeBoost"] = "Execute Safe Boost (With SHA-256 Backup)",
            ["GameCenterTitle"] = "GAME CENTER — GAME DETECTION & EXECUTION PROFILES",
            ["GameCenterSubtitle"] = "Detects running games and active sockets without DLL injection or process memory manipulation.",
            ["BenchmarksTitle"] = "BENCHMARK CENTER — LOCAL DETERMINISTIC SYNTHETIC EVALUATION",
            ["BenchmarksSubtitle"] = "Measures CPU arithmetic throughput, memory allocation latency, and sequential I/O without fabricated third-party scores.",
            ["BtnRunBenchmark"] = "Run Deterministic Benchmark",
            ["PeripheralsTitle"] = "PERIPHERAL TEST — WIN32 REFRESH RATE & INPUT CADENCE",
            ["PeripheralsSubtitle"] = "Inspects native resolution/Hz of the primary monitor and measures input event cadence and jitter.",
            ["BtnTestInputCadence"] = "Measure Peripheral Cadence & Stability",
            ["SoftwareHubTitle"] = "SOFTWARE HUB — VERIFIED WINGET CATALOG (OFFICIAL SOURCES)",
            ["SoftwareHubSubtitle"] = "Displays official Windows Package Manager (winget) identifiers for essential utilities and runtimes without hidden silent installations.",
            ["AiAssistantTitle"] = "AI ASSISTANT — LOCAL DIAGNOSTIC ADVISORY ENGINE",
            ["AiAssistantSubtitle"] = "Analyzes thermal telemetry (Hotspot Delta), snapshot integrity, and network route comparisons with transparent technical explanations.",
            ["BtnGenerateInsights"] = "Refresh System Technical Analysis",
            ["TweaksTitle"] = "WINDOWS TWEAKS — AUDITED REGISTRY CATALOG (DPAPI+HMAC LEDGER)",
            ["TweaksSubtitle"] = "Real-time value and type inspection in Windows Registry, post-write and post-restore verification, and detailed technical documentation per item.",
            ["BtnApplyTweak"] = "Apply",
            ["BtnRestoreTweak"] = "Revert",
            ["BtnRefreshTweakStates"] = "REFRESH STATES",
            ["BtnExplainTweak"] = "Detailed Explanation",
            ["RestoreTitle"] = "RESTORE CENTER — SHA-256 SNAPSHOTS, DPAPI LEDGER & TRANSACTIONAL ROLLBACK",
            ["RestoreChecksumNote"] = "SHA-256 hash validates structural manifest integrity prior to restoration. Restoration is automatically blocked if integrity verification fails.",
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
            ["DebloatTitle"] = "DEBLOAT & CLEANER — DRY-RUN SIMULATION & ANTI-SYMLINK PURGE",
            ["DebloatSubtitle"] = "Protects personal folders and blocks NTFS ReparsePoints/Junctions. Never reports an estimate as actual freed space.",
            ["BtnDryRunScan"] = "Simulate Cleanup (Dry-Run)",
            ["BtnExecutePurge"] = "Execute Real Cleanup",
            ["ChkConfirmDeletion"] = "I explicitly confirm deleting temporary files in this category",
            ["NetworkTitle"] = "NETWORK LAB — SEPARATED DIAGNOSTICS (ICMP, TCP, DNS & EXTERNAL THROUGHPUT)",
            ["NetworkSubtitle"] = "Separates ICMP ping from TCP handshake and DNS resolution. Measures real download against Cloudflare CDN, never localhost.",
            ["BtnRunNetworkDiag"] = "Run ICMP / TCP / DNS Diagnostics",
            ["BtnRunThroughput"] = "Measure External Throughput (Cloudflare CDN)",
            ["GamingTitle"] = "PERFORMANCE LAB — INTEL PRESENTMON & FRAMETIME TELEMETRY",
            ["GamingSubtitle"] = "Select a running process and start a timed session to measure average FPS, 1% Low, and frametime variance.",
            ["BtnCheckPresentMon"] = "Verify PresentMon",
            ["BtnRefreshProcesses"] = "List Processes",
            ["BtnStartCapture"] = "Start PresentMon Capture",
            ["BtnCancelCapture"] = "Cancel Capture",
            ["DiagnosticsTitle"] = "DIAGNOSTICS — DPAPI/ACL SECURITY, HMAC-SHA256 & MODULE MATRIX",
            ["DiagnosticsSubtitle"] = "Displays real cryptographic protection posture, per-request signature verification, UAC elevation, and auditable Registry history.",
            ["BtnRefreshAuditLogs"] = "Refresh Audit Trail",
            ["DriversTitle"] = "DRIVER CENTER — READ-ONLY SIGNED DRIVER INVENTORY (WMI)",
            ["DriversSubtitle"] = "Lists vendor, version, date, and digital signature via Win32_PnPSignedDriver without automatically downloading or altering drivers.",
            ["BtnRefreshDrivers"] = "Refresh WMI Inventory",
            ["AcceleratorTitle"] = "GAME NETWORK ACCELERATOR — MULTI-SAMPLE ROUTE & SELECTIVE WIREGUARD TUNNEL",
            ["AcceleratorSubtitle"] = "Compares Direct Route vs. Relay Nodes with multi-sample probing (ICMP/TCP/Jitter). Blocks activation when the direct route is faster or more stable.",
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
