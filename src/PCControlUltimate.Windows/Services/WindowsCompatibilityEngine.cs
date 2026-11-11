using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;
using PCControlUltimate.Core.Models;

namespace PCControlUltimate.Windows.Services;

public record WindowsEnvironmentProfile(
    string OsVersionDescription,
    string Edition,
    int BuildNumber,
    string DisplayVersion,
    string Architecture,
    bool IsWindows11,
    bool IsWindows10OrLater,
    bool IsRunningAsAdministrator,
    bool IsDomainJoinedOrEnterpriseManaged,
    bool HasRegistryPolicyRestriction,
    bool IsDefenderActive,
    IReadOnlyList<string> DetectedPolicyRestrictions
);

public record OperationCompatibilityResult(
    string OperationId,
    string OperationName,
    OperationCompatibilityState State,
    bool CanExecuteNow,
    bool RequiresElevationPrompt,
    string Explanation,
    string? RecommendedAction
);

/// <summary>
/// Windows Compatibility Engine (§4):
/// Detects Windows 10/11 edition, build, architecture, UAC elevation, Group Policy restrictions,
/// and classifies every system operation into 6 states without ever attempting to bypass corporate policies.
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsCompatibilityEngine
{
    public WindowsEnvironmentProfile DetectEnvironment(
        bool? isRunningAsAdminOverride = null,
        bool? hasRegistryPolicyRestrictionOverride = null,
        bool? isEnterpriseManagedOverride = null,
        int? buildNumberOverride = null,
        string? editionOverride = null)
    {
        int build = buildNumberOverride ?? Environment.OSVersion.Version.Build;
        bool isWin11 = build >= 22000;
        bool isWin10OrLater = Environment.OSVersion.Version.Major >= 10 || build >= 10240;
        string arch = RuntimeInformation.OSArchitecture.ToString();
        bool isAdmin = isRunningAsAdminOverride ?? CheckIsAdministrator();

        string edition = editionOverride ?? "Windows Professional / Home";
        string displayVersion = isWin11 ? "23H2 / 24H2" : "22H2";
        bool hasRegPolicy = hasRegistryPolicyRestrictionOverride ?? false;
        var restrictions = new List<string>();

        if (OperatingSystem.IsWindows() && editionOverride == null)
        {
            try
            {
                using var cvKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false);
                if (cvKey != null)
                {
                    edition = cvKey.GetValue("EditionID")?.ToString() ?? edition;
                    displayVersion = cvKey.GetValue("DisplayVersion")?.ToString() ?? displayVersion;
                }
            }
            catch { }

            try
            {
                using var sysPol = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System", false);
                if (sysPol != null && Convert.ToInt32(sysPol.GetValue("DisableRegistryTools", 0)) != 0)
                {
                    hasRegPolicy = true;
                    restrictions.Add("Política de Grupo ativa: DisableRegistryTools está habilitado neste perfil.");
                }
            }
            catch { }
        }

        if (hasRegPolicy && restrictions.Count == 0)
        {
            restrictions.Add("Edição de Registro bloqueada por Política de Grupo (GPO/MDM).");
        }

        bool isEnterprise = isEnterpriseManagedOverride ?? CheckIsDomainJoined();
        if (isEnterprise)
        {
            restrictions.Add("Dispositivo gerenciado por domínio ou diretiva corporativa.");
        }

        return new WindowsEnvironmentProfile(
            OsVersionDescription: RuntimeInformation.OSDescription,
            Edition: edition,
            BuildNumber: build,
            DisplayVersion: displayVersion,
            Architecture: arch,
            IsWindows11: isWin11,
            IsWindows10OrLater: isWin10OrLater,
            IsRunningAsAdministrator: isAdmin,
            IsDomainJoinedOrEnterpriseManaged: isEnterprise,
            HasRegistryPolicyRestriction: hasRegPolicy,
            IsDefenderActive: true,
            DetectedPolicyRestrictions: restrictions
        );
    }

    public OperationCompatibilityResult EvaluateOperation(
        string operationId,
        string operationName,
        bool requiresAdmin,
        bool modifiesHklmRegistry,
        bool modifiesSystemServices,
        int minimumWindowsBuild = 19041,
        WindowsEnvironmentProfile? profileOverride = null)
    {
        var env = profileOverride ?? DetectEnvironment();

        // 1. Check OS build compatibility
        if (env.BuildNumber > 0 && env.BuildNumber < minimumWindowsBuild)
        {
            return new OperationCompatibilityResult(
                operationId,
                operationName,
                OperationCompatibilityState.NotSupported,
                CanExecuteNow: false,
                RequiresElevationPrompt: false,
                Explanation: $"Operação requer Windows build {minimumWindowsBuild} ou superior (build atual: {env.BuildNumber}).",
                RecommendedAction: "Atualize o Windows para uma versão suportada ou utilize apenas módulos de diagnóstico."
            );
        }

        // 2. Never bypass corporate or Group Policy restrictions (§4.3)
        if ((modifiesHklmRegistry || modifiesSystemServices) && env.HasRegistryPolicyRestriction)
        {
            return new OperationCompatibilityResult(
                operationId,
                operationName,
                OperationCompatibilityState.BlockedByPolicy,
                CanExecuteNow: false,
                RequiresElevationPrompt: false,
                Explanation: "Bloqueado por Política de Grupo (GPO/MDM) corporativa. O PC Control Ultimate respeita diretivas administrativas e nunca tenta contorná-las.",
                RecommendedAction: "Utilize o aplicativo em modo de diagnóstico e monitoramento somente-leitura."
            );
        }

        // 3. Check UAC Administrator requirement
        if ((requiresAdmin || modifiesHklmRegistry || modifiesSystemServices) && !env.IsRunningAsAdministrator)
        {
            return new OperationCompatibilityResult(
                operationId,
                operationName,
                OperationCompatibilityState.RequiresAdmin,
                CanExecuteNow: false,
                RequiresElevationPrompt: true,
                Explanation: $"'{operationName}' requer privilégios de Administrador (UAC) para alterar chaves HKLM ou serviços do sistema.",
                RecommendedAction: "Clique em 'Elevar UAC' para executar com confirmação explícita do Windows."
            );
        }

        // 4. Corporate domain warning (Compatible with limitations)
        if (env.IsDomainJoinedOrEnterpriseManaged && (modifiesHklmRegistry || modifiesSystemServices))
        {
            return new OperationCompatibilityResult(
                operationId,
                operationName,
                OperationCompatibilityState.CompatibleWithLimitations,
                CanExecuteNow: true,
                RequiresElevationPrompt: false,
                Explanation: "Compatível com limitações: políticas de domínio (GPO) podem reverter configurações de sistema no próximo logon.",
                RecommendedAction: "Crie um ponto de restauração ou snapshot antes de aplicar."
            );
        }

        return new OperationCompatibilityResult(
            operationId,
            operationName,
            OperationCompatibilityState.Compatible,
            CanExecuteNow: true,
            RequiresElevationPrompt: false,
            Explanation: "Totalmente compatível com a edição, build e nível de privilégio atuais do Windows.",
            RecommendedAction: null
        );
    }

    private static bool CheckIsAdministrator()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckIsDomainJoined()
    {
        try
        {
            string? domain = Environment.UserDomainName;
            string? machine = Environment.MachineName;
            return !string.IsNullOrWhiteSpace(domain) &&
                   !string.IsNullOrWhiteSpace(machine) &&
                   !string.Equals(domain, machine, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
