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
            ["HeaderDesc"] = "Operações reais por catálogo fechado, proteção DPAPI/ACL e verificação por releitura.",
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
            ["BtnSwitchLang"] = "Idioma: PT-BR (Mudar p/ EN-US)",
            ["BtnElevateUac"] = "Solicitar Elevação UAC",
            ["BtnCreateBackup"] = "Criar Backup Verificado",
            ["BtnRefreshAll"] = "Atualizar Sensores e Estado",
            ["SensorUnavailable"] = "Sensor indisponível (N/D)",
            ["NotDetected"] = "Não detectado",
            ["AgentConnectedAdmin"] = "Agente Autenticado (Administrador / UAC Ativo)",
            ["AgentConnectedStandard"] = "Agente Autenticado (Modo Padrão Sem Elevação)",
            ["AgentDisconnected"] = "Agente Local Desconectado ou Não Autenticado",
            ["CardCpuTitle"] = "PROCESSADOR (CPU)",
            ["CardGpuTitle"] = "PLACA DE VÍDEO (GPU)",
            ["CardRamTitle"] = "MEMÓRIA RAM FÍSICA",
            ["CardDisplayTitle"] = "MONITOR PRIMÁRIO (WIN32)",
            ["GpuHealthTitle"] = "GPU HEALTH LAB — DIAGNÓSTICO TÉRMICO CONTEXTUALIZADO",
            ["GpuHealthNotice"] = "Nota Técnica: A diferença entre Core e Hotspot varia por arquitetura (NVIDIA/AMD/Intel). Este módulo não aplica overclock, undervolt ou alterações de driver automaticamente.",
            ["TweaksTitle"] = "CATÁLOGO FECHADO DE AJUSTES DO REGISTRO (SEM BYPASS)",
            ["TweaksSubtitle"] = "Apenas IDs pré-aprovados no catálogo do backend são aceitos. Cada escrita é verificada por releitura imediata.",
            ["BtnApplyTweak"] = "Aplicar Ajuste",
            ["BtnRestoreTweak"] = "Reverter Ajuste",
            ["RestoreTitle"] = "RESTORE CENTER — BACKUPS SHA-256 E RESTAURAÇÃO COM ROLLBACK",
            ["RestoreChecksumNote"] = "Aviso Técnico: O checksum SHA-256 detecta corrupção acidental do arquivo JSON, mas não equivale a uma assinatura digital criptográfica.",
            ["BtnRefreshBackups"] = "Atualizar Lista de Backups",
            ["BtnVerifyBackup"] = "Verificar Integridade SHA-256",
            ["BtnRestoreFullBackup"] = "Restaurar Backup Completo",
            ["ChkConfirmIncomplete"] = "Confirmo restaurar mesmo se o backup tiver itens não lidos na captura",
            ["ChkRollbackOnError"] = "Executar rollback automático caso algum item falhe durante a restauração",
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
            ["DiagnosticsTitle"] = "DIAGNÓSTICOS DE SEGURANÇA, DPAPI/ACL E TRILHA DE AUDITORIA",
            ["DiagnosticsSubtitle"] = "Exibe estado real de proteção do token de sessão, elevação UAC e histórico auditável de operações no Registro.",
            ["DriversTitle"] = "DRIVER CENTER — INVENTÁRIO SOMENTE-LEITURA DE DRIVERS ASSINADOS (WMI)",
            ["DriversSubtitle"] = "Lista fabricante, versão, data e assinatura digital via Win32_PnPSignedDriver. Não baixa nem instala drivers automaticamente.",
            ["BtnRefreshDrivers"] = "Atualizar Inventário de Drivers"
        },
        ["en-US"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AppTitle"] = "PC CONTROL ULTIMATE v2.5.0 — Native Windows 10/11 x64 Suite",
            ["HeaderSubtitle"] = "NATIVE WINDOWS ENGINEERING, DIAGNOSTICS & OPTIMIZATION CONSOLE",
            ["HeaderDesc"] = "Real operations via closed catalog, DPAPI/ACL token protection, and post-write verification.",
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
            ["BtnSwitchLang"] = "Language: EN-US (Switch to PT-BR)",
            ["BtnElevateUac"] = "Request UAC Elevation",
            ["BtnCreateBackup"] = "Create Verified Backup",
            ["BtnRefreshAll"] = "Refresh Sensors & State",
            ["SensorUnavailable"] = "Sensor unavailable (N/A)",
            ["NotDetected"] = "Not detected",
            ["AgentConnectedAdmin"] = "Authenticated Agent (Administrator / UAC Active)",
            ["AgentConnectedStandard"] = "Authenticated Agent (Standard Non-Elevated Mode)",
            ["AgentDisconnected"] = "Local Agent Disconnected or Unauthenticated",
            ["CardCpuTitle"] = "PROCESSOR (CPU)",
            ["CardGpuTitle"] = "GRAPHICS CARD (GPU)",
            ["CardRamTitle"] = "PHYSICAL RAM MEMORY",
            ["CardDisplayTitle"] = "PRIMARY DISPLAY (WIN32)",
            ["GpuHealthTitle"] = "GPU HEALTH LAB — CONTEXTUALIZED THERMAL DIAGNOSTICS",
            ["GpuHealthNotice"] = "Technical Note: Core-to-Hotspot delta varies by silicon architecture (NVIDIA/AMD/Intel). This module never applies automatic overclocking, undervolting, or driver modifications.",
            ["TweaksTitle"] = "CLOSED REGISTRY OPTIMIZATION CATALOG (ZERO BYPASS)",
            ["TweaksSubtitle"] = "Only pre-approved IDs in the backend catalog are accepted. Every write is verified by immediate re-read.",
            ["BtnApplyTweak"] = "Apply Tweak",
            ["BtnRestoreTweak"] = "Restore Tweak",
            ["RestoreTitle"] = "RESTORE CENTER — SHA-256 BACKUPS & ROLLBACK RESTORATION",
            ["RestoreChecksumNote"] = "Technical Notice: SHA-256 checksum detects accidental JSON file corruption, but is not a cryptographic digital signature.",
            ["BtnRefreshBackups"] = "Refresh Backup List",
            ["BtnVerifyBackup"] = "Verify SHA-256 Integrity",
            ["BtnRestoreFullBackup"] = "Restore Full Backup",
            ["ChkConfirmIncomplete"] = "I confirm restoring even if the backup had unreadable items during capture",
            ["ChkRollbackOnError"] = "Execute automatic rollback if any item fails during restoration",
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
            ["DiagnosticsTitle"] = "SECURITY DIAGNOSTICS, DPAPI/ACL & AUDIT TRAIL",
            ["DiagnosticsSubtitle"] = "Displays real session token protection state, UAC elevation, and auditable Registry operation history.",
            ["DriversTitle"] = "DRIVER CENTER — READ-ONLY SIGNED DRIVER INVENTORY (WMI)",
            ["DriversSubtitle"] = "Lists vendor, version, date, and digital signature via Win32_PnPSignedDriver. Never downloads or installs drivers automatically.",
            ["BtnRefreshDrivers"] = "Refresh Driver Inventory"
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
