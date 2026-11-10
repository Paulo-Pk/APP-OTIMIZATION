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
            ["HeaderSubtitle"] = "CONSOLE NATIVO DE ENGENHARIA, DIAGNÓSTICO E OTIMIZAÇÃO WINDOWS",
            ["HeaderDesc"] = "Operações reais por catálogo fechado, ledger DPAPI+HMAC-SHA256, ACL NTFS e verificação por releitura.",
            ["NavSection"] = "MÓDULOS DO SISTEMA",
            ["NavDashboard"] = "01. Dashboard Geral",
            ["NavHardware"] = "02. Hardware & GPU Health",
            ["NavTweaks"] = "03. Otimizador de Registro",
            ["NavRestore"] = "04. Restore Center & Backups",
            ["NavDebloat"] = "05. Debloat & Limpeza Segura",
            ["NavNetwork"] = "06. Network Lab",
            ["NavGaming"] = "07. Gaming & PresentMon",
            ["NavDiagnostics"] = "08. Diagnósticos & Auditoria",
            ["NavDrivers"] = "09. Driver Center (Inventário)",
            ["BackupStateHeader"] = "ESTADO DO ÚLTIMO BACKUP",
            ["ActiveModuleHeader"] = "MÓDULO ATIVO",
            ["BtnSwitchLang"] = "Idioma: PT-BR (Mudar p/ EN-US)",
            ["BtnElevateUac"] = "Solicitar Elevação UAC",
            ["BtnCreateBackup"] = "Criar Backup Verificado",
            ["BtnRefreshAll"] = "Atualizar Sensores e Estado",
            ["SensorUnavailable"] = "Sensor indisponível (N/D)",
            ["SensorStatusAvailable"] = "Disponível (Leitura Física Real)",
            ["SensorStatusNotSupported"] = "Sensor não suportado pelo hardware",
            ["SensorStatusUnavailable"] = "Leitura indisponível no momento (requer privilégio/driver)",
            ["SensorStatusCommFailure"] = "Falha de comunicação com subsistema de sensores",
            ["NotDetected"] = "Não detectado",
            ["AgentConnectedAdmin"] = "Agente Autenticado (Administrador / UAC + HMAC-SHA256 Ativos)",
            ["AgentConnectedStandard"] = "Agente Autenticado (Modo Padrão Sem Elevação / HMAC-SHA256 Ativo)",
            ["AgentDisconnected"] = "Agente Local Desconectado ou Não Autenticado",
            ["CardCpuTitle"] = "PROCESSADOR (CPU)",
            ["CardGpuTitle"] = "PLACA DE VÍDEO (GPU)",
            ["CardRamTitle"] = "MEMÓRIA RAM FÍSICA",
            ["CardDisplayTitle"] = "MONITOR PRIMÁRIO (WIN32)",
            ["HardwareStateHeader"] = "ESTADO DO SUBSISTEMA DE SENSORES (LIBREHARDWAREMONITOR)",
            ["SensorsListTitle"] = "LIBREHARDWAREMONITOR — SENSORES FÍSICOS DETECTADOS",
            ["LblCpuClockPower"] = "CPU Clock / Potência",
            ["LblGpuClockPower"] = "GPU Clock / Potência",
            ["LblGpuVram"] = "GPU VRAM Alocada",
            ["LblGpuFanRpm"] = "GPU Rotação Ventoinha",
            ["GpuHealthTitle"] = "GPU HEALTH LAB — DIAGNÓSTICO TÉRMICO CONTEXTUALIZADO",
            ["GpuHealthNotice"] = "Nota Técnica: A diferença entre Core e Hotspot varia por arquitetura (NVIDIA/AMD/Intel). Este módulo não aplica overclock, undervolt ou alterações de driver automaticamente.",
            ["TweaksTitle"] = "CATÁLOGO FECHADO DE AJUSTES DO REGISTRO (ZERO BYPASS + LEDGER DPAPI)",
            ["TweaksSubtitle"] = "Apenas IDs do catálogo fechado são aceitos. O estado original é gravado no Ledger DPAPI+HMAC do backend antes de qualquer escrita.",
            ["BtnApplyTweak"] = "Aplicar Ajuste",
            ["BtnRestoreTweak"] = "Reverter (Ledger Seguro)",
            ["RestoreTitle"] = "RESTORE CENTER — SNAPSHOTS SHA-256, LEDGER DPAPI E ROLLBACK TRANSACIONAL",
            ["RestoreChecksumNote"] = "Aviso Técnico: O hash SHA-256 valida a integridade estrutural do manifesto antes da restauração. A restauração é bloqueada automaticamente se a verificação falhar.",
            ["BtnRefreshBackups"] = "Atualizar Lista de Snapshots",
            ["BtnVerifyBackup"] = "Verificar Integridade SHA-256",
            ["BtnRestoreFullBackup"] = "Restaurar Snapshot Selecionado",
            ["BtnRestoreSingleItem"] = "Restaurar Item",
            ["ChkConfirmRestoreExecution"] = "Confirmo explicitamente a execução da restauração dos itens listados abaixo",
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
            ["DebloatTitle"] = "DEBLOAT & CLEANER — SIMULAÇÃO (DRY-RUN) E LIMPEZA COM PROTEÇÃO ANTI-SYMLINK",
            ["DebloatSubtitle"] = "Protege pastas pessoais e bloqueia ReparsePoints/Junctions NTFS. Nunca apresenta estimativa como espaço liberado.",
            ["BtnDryRunScan"] = "Simular Limpeza (Dry-Run / Estimativa)",
            ["BtnExecutePurge"] = "Executar Limpeza Real da Categoria",
            ["ChkConfirmDeletion"] = "Confirmo explicitamente a exclusão dos arquivos temporários desta categoria",
            ["NetworkTitle"] = "NETWORK LAB — DIAGNÓSTICO SEPARADO (ICMP, TCP, DNS E THROUGHPUT EXTERNO)",
            ["NetworkSubtitle"] = "Separa ping ICMP de handshake TCP e resolução DNS. Não confunde endereços de borda com servidores internos de partidas.",
            ["BtnRunNetworkDiag"] = "Executar Diagnóstico ICMP / TCP / DNS",
            ["BtnRunThroughput"] = "Medir Throughput Externo Real (Cloudflare CDN)",
            ["GamingTitle"] = "GAMING & INTEL PRESENTMON — CAPTURA REAL DE FRAMETIMES",
            ["GamingSubtitle"] = "Selecione um processo em execução e inicie uma sessão temporizada. Argumentos validados sem concatenação insegura.",
            ["BtnCheckPresentMon"] = "Verificar Binário PresentMon",
            ["BtnRefreshProcesses"] = "Listar Processos Ativos",
            ["BtnStartCapture"] = "Iniciar Captura PresentMon",
            ["BtnCancelCapture"] = "Cancelar Captura",
            ["DiagnosticsTitle"] = "DIAGNÓSTICOS DE SEGURANÇA, DPAPI/ACL, HMAC-SHA256 E TRILHA DE AUDITORIA",
            ["DiagnosticsSubtitle"] = "Exibe estado real de proteção do token de sessão, assinatura por requisição, elevação UAC e histórico auditável no Registro.",
            ["BtnRefreshAuditLogs"] = "Atualizar Trilha de Auditoria",
            ["DriversTitle"] = "DRIVER CENTER — INVENTÁRIO SOMENTE-LEITURA DE DRIVERS ASSINADOS (WMI)",
            ["DriversSubtitle"] = "Lista fabricante, versão, data e assinatura digital via Win32_PnPSignedDriver. Não baixa nem instala drivers automaticamente.",
            ["BtnRefreshDrivers"] = "Atualizar Inventário de Drivers",
            ["NavAccelerator"] = "10. Game Network Accelerator",
            ["AcceleratorTitle"] = "GAME NETWORK ACCELERATOR — DIAGNÓSTICO MULTI-AMOSTRA, TRACEROUTE E TÚNEL WIREGUARD SELETIVO",
            ["AcceleratorSubtitle"] = "Compara Rota Direta vs. Nós Intermediários com múltiplas amostras (ICMP/TCP/Jitter). Recomenda manter a Rota Direta sempre que o túnel não trouxer ganho real.",
            ["BtnDetectGameSockets"] = "Detectar Jogos e Conexões Ativas",
            ["BtnAnalyzeRoute"] = "Avaliar Rota Multi-Amostra (Direta vs. Nós)",
            ["BtnRunTraceroute"] = "Executar Traceroute (Saltos TTL)",
            ["BtnAuditWgConfig"] = "Validar Configuração WireGuard (AllowedIPs)",
            ["BtnQueryWgTunnel"] = "Verificar Serviço & Handshake WireGuard",
            ["BtnAddRelayNode"] = "Cadastrar Nó WireGuard / VPS",
            ["FooterLeftText"] = "PC Control Ultimate v2.5.0 · Solução Desktop Nativa .NET 10 WPF · Catálogo Fechado + Ledger DPAPI/HMAC + SQLite",
            ["FooterRightText"] = "IPC Loopback Autenticado: 127.0.0.1:52194 (HMAC-SHA256)"
        },
        ["en-US"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AppTitle"] = "PC CONTROL ULTIMATE v2.5.0 — Native Windows 10/11 x64 Suite",
            ["HeaderSubtitle"] = "NATIVE WINDOWS ENGINEERING, DIAGNOSTICS & OPTIMIZATION CONSOLE",
            ["HeaderDesc"] = "Real operations via closed catalog, DPAPI+HMAC-SHA256 ledger, NTFS ACLs, and post-write verification.",
            ["NavSection"] = "SYSTEM MODULES",
            ["NavDashboard"] = "01. Overview Dashboard",
            ["NavHardware"] = "02. Hardware & GPU Health",
            ["NavTweaks"] = "03. Registry Optimizer",
            ["NavRestore"] = "04. Restore Center & Backups",
            ["NavDebloat"] = "05. Debloat & Safe Cleaner",
            ["NavNetwork"] = "06. Network Lab",
            ["NavGaming"] = "07. Gaming & PresentMon",
            ["NavDiagnostics"] = "08. Diagnostics & Audit",
            ["NavDrivers"] = "09. Driver Center (Inventory)",
            ["BackupStateHeader"] = "LAST BACKUP STATUS",
            ["ActiveModuleHeader"] = "ACTIVE MODULE",
            ["BtnSwitchLang"] = "Language: EN-US (Switch to PT-BR)",
            ["BtnElevateUac"] = "Request UAC Elevation",
            ["BtnCreateBackup"] = "Create Verified Backup",
            ["BtnRefreshAll"] = "Refresh Sensors & State",
            ["SensorUnavailable"] = "Sensor unavailable (N/A)",
            ["SensorStatusAvailable"] = "Available (Real Physical Reading)",
            ["SensorStatusNotSupported"] = "Sensor not supported by hardware",
            ["SensorStatusUnavailable"] = "Reading currently unavailable (requires privilege/driver)",
            ["SensorStatusCommFailure"] = "Sensor subsystem communication failure",
            ["NotDetected"] = "Not detected",
            ["AgentConnectedAdmin"] = "Authenticated Agent (Administrator / UAC + HMAC-SHA256 Active)",
            ["AgentConnectedStandard"] = "Authenticated Agent (Standard Non-Elevated Mode / HMAC-SHA256 Active)",
            ["AgentDisconnected"] = "Local Agent Disconnected or Unauthenticated",
            ["CardCpuTitle"] = "PROCESSOR (CPU)",
            ["CardGpuTitle"] = "GRAPHICS CARD (GPU)",
            ["CardRamTitle"] = "PHYSICAL RAM MEMORY",
            ["CardDisplayTitle"] = "PRIMARY DISPLAY (WIN32)",
            ["HardwareStateHeader"] = "SENSOR SUBSYSTEM STATUS (LIBREHARDWAREMONITOR)",
            ["SensorsListTitle"] = "LIBREHARDWAREMONITOR — DETECTED PHYSICAL SENSORS",
            ["LblCpuClockPower"] = "CPU Clock / Power",
            ["LblGpuClockPower"] = "GPU Clock / Power",
            ["LblGpuVram"] = "GPU Allocated VRAM",
            ["LblGpuFanRpm"] = "GPU Fan Speed",
            ["GpuHealthTitle"] = "GPU HEALTH LAB — CONTEXTUALIZED THERMAL DIAGNOSTICS",
            ["GpuHealthNotice"] = "Technical Note: Core-to-Hotspot delta varies by silicon architecture (NVIDIA/AMD/Intel). This module never applies automatic overclocking, undervolting, or driver modifications.",
            ["TweaksTitle"] = "CLOSED REGISTRY OPTIMIZATION CATALOG (ZERO BYPASS + DPAPI LEDGER)",
            ["TweaksSubtitle"] = "Only pre-approved backend catalog IDs are accepted. Pre-modification state is recorded in the backend DPAPI+HMAC ledger prior to any write.",
            ["BtnApplyTweak"] = "Apply Tweak",
            ["BtnRestoreTweak"] = "Revert (Secure Ledger)",
            ["RestoreTitle"] = "RESTORE CENTER — SHA-256 SNAPSHOTS, DPAPI LEDGER & TRANSACTIONAL ROLLBACK",
            ["RestoreChecksumNote"] = "Technical Notice: SHA-256 hash validates structural manifest integrity prior to restoration. Restoration is automatically blocked if integrity verification fails.",
            ["BtnRefreshBackups"] = "Refresh Snapshot List",
            ["BtnVerifyBackup"] = "Verify SHA-256 Integrity",
            ["BtnRestoreFullBackup"] = "Restore Selected Snapshot",
            ["BtnRestoreSingleItem"] = "Restore Item",
            ["ChkConfirmRestoreExecution"] = "I explicitly confirm executing the restoration of the items listed below",
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
            ["BtnDryRunScan"] = "Simulate Cleanup (Dry-Run Estimate)",
            ["BtnExecutePurge"] = "Execute Real Category Cleanup",
            ["ChkConfirmDeletion"] = "I explicitly confirm deleting temporary files in this category",
            ["NetworkTitle"] = "NETWORK LAB — SEPARATED DIAGNOSTICS (ICMP, TCP, DNS & EXTERNAL THROUGHPUT)",
            ["NetworkSubtitle"] = "Separates ICMP ping from TCP handshake and DNS resolution. Never conflates edge addresses with private game match servers.",
            ["BtnRunNetworkDiag"] = "Run ICMP / TCP / DNS Diagnostics",
            ["BtnRunThroughput"] = "Measure Real External Throughput (Cloudflare CDN)",
            ["GamingTitle"] = "GAMING & INTEL PRESENTMON — REAL FRAMETIME CAPTURE",
            ["GamingSubtitle"] = "Select a running process and start a timed session. Arguments validated without unsafe string concatenation.",
            ["BtnCheckPresentMon"] = "Verify PresentMon Binary",
            ["BtnRefreshProcesses"] = "List Running Processes",
            ["BtnStartCapture"] = "Start PresentMon Capture",
            ["BtnCancelCapture"] = "Cancel Capture",
            ["DiagnosticsTitle"] = "SECURITY DIAGNOSTICS, DPAPI/ACL, HMAC-SHA256 & AUDIT TRAIL",
            ["DiagnosticsSubtitle"] = "Displays real session token protection state, per-request cryptographic signature, UAC elevation, and auditable Registry history.",
            ["BtnRefreshAuditLogs"] = "Refresh Audit Trail",
            ["DriversTitle"] = "DRIVER CENTER — READ-ONLY SIGNED DRIVER INVENTORY (WMI)",
            ["DriversSubtitle"] = "Lists vendor, version, date, and digital signature via Win32_PnPSignedDriver. Never downloads or installs drivers automatically.",
            ["BtnRefreshDrivers"] = "Refresh Driver Inventory",
            ["NavAccelerator"] = "10. Game Network Accelerator",
            ["AcceleratorTitle"] = "GAME NETWORK ACCELERATOR — MULTI-SAMPLE DIAGNOSTICS, TRACEROUTE & SELECTIVE WIREGUARD TUNNEL",
            ["AcceleratorSubtitle"] = "Compares Direct Route vs. Relay Nodes with multi-sample probing (ICMP/TCP/Jitter). Recommends keeping Direct Route whenever the tunnel offers no real gain.",
            ["BtnDetectGameSockets"] = "Detect Running Games & Active Sockets",
            ["BtnAnalyzeRoute"] = "Evaluate Multi-Sample Route (Direct vs. Nodes)",
            ["BtnRunTraceroute"] = "Run Traceroute (TTL Hops)",
            ["BtnAuditWgConfig"] = "Validate WireGuard Config (AllowedIPs)",
            ["BtnQueryWgTunnel"] = "Verify WireGuard Service & Handshake",
            ["BtnAddRelayNode"] = "Register WireGuard / VPS Node",
            ["FooterLeftText"] = "PC Control Ultimate v2.5.0 · Native .NET 10 WPF Desktop Suite · Closed Catalog + DPAPI/HMAC Ledger + SQLite",
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
