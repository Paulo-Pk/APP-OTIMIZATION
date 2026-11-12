using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;
using PCControlUltimate.Security;

namespace PCControlUltimate.Windows.Services;

public record RegistryLedgerEntry
{
    public string OperationId { get; set; } = string.Empty;
    public string TweakId { get; set; } = string.Empty;
    public string Hive { get; set; } = string.Empty;
    public string SubKeyPath { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public string OriginalValueKind { get; set; } = string.Empty;
    public string? OriginalRawValue { get; set; }
    public bool WasInitiallyAbsent { get; set; }
    public bool KeyCreatedByApp { get; set; }
    public string AppliedValue { get; set; } = string.Empty;
    public string CreatedAtUtc { get; set; } = string.Empty;
    public bool IsRestored { get; set; }
    public string? RestoredAtUtc { get; set; }
    public string EntryHmacSha256 { get; set; } = string.Empty;
}

public class RegistryLedgerFile
{
    public string FormatVersion { get; set; } = "2.6";
    public string UpdatedAtUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public List<RegistryLedgerEntry> Entries { get; set; } = new();
    public string LedgerHmacSha256 { get; set; } = string.Empty;
}

public record LedgerValidationResult(
    bool IsValid,
    RegistryLedgerEntry? Entry,
    string? ErrorCode,
    string? ErrorMessage
)
{
    public string ErrorReason =>
        string.IsNullOrWhiteSpace(ErrorCode)
            ? (ErrorMessage ?? string.Empty)
            : $"{ErrorCode}: {ErrorMessage}";
}

/// <summary>
/// Trusted backend-only ledger that records the original state of Registry tweaks before modification.
/// Protected by Windows DPAPI (CurrentUser), NTFS ACLs, and canonical HMAC-SHA256 authentication.
/// Fail-closed design: if the protected HMAC key cannot be loaded or created securely, no fallback secret is used
/// and restorations relying on the Ledger are refused safely.
/// </summary>
[SupportedOSPlatform("windows")]
public static class RegistryStateLedger
{
    private static readonly object _syncRoot = new();

    private sealed class TestLedgerScopeContext
    {
        public string? EphemeralSecretKey { get; init; }
        public string? IsolatedLedgerFilePath { get; init; }
        public bool SimulateKeyUnavailable { get; init; }
        public Dictionary<string, string> InMemoryProtectedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly AsyncLocal<TestLedgerScopeContext?> _testScope = new();

    private sealed class TestLedgerDisposableScope : IDisposable
    {
        private readonly TestLedgerScopeContext? _previous;
        private readonly string? _tempFileToDelete;
        private bool _disposed;

        public TestLedgerDisposableScope(TestLedgerScopeContext? previous, string? tempFileToDelete)
        {
            _previous = previous;
            _tempFileToDelete = tempFileToDelete;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _testScope.Value = _previous;
                if (!string.IsNullOrWhiteSpace(_tempFileToDelete) && File.Exists(_tempFileToDelete))
                {
                    try { File.Delete(_tempFileToDelete); } catch { }
                }
                _disposed = true;
            }
        }
    }

    private static string _defaultLedgerFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCControlUltimate",
        "StateLedger",
        "tweak_state_ledger.dpapi"
    );

    public static string LedgerFilePath
    {
        get => _testScope.Value?.IsolatedLedgerFilePath ?? _defaultLedgerFilePath;
        set => _defaultLedgerFilePath = value;
    }

    private static string LedgerKeyFilePath => Path.Combine(
        Path.GetDirectoryName(_defaultLedgerFilePath) ?? Path.GetTempPath(),
        "ledger_hmac_key.dpapi"
    );

    private static readonly string? _productionLedgerSecretKey = TryInitializeProductionLedgerKey();

    private static string? ActiveSecretKey
    {
        get
        {
            var scope = _testScope.Value;
            if (scope != null)
            {
                return scope.SimulateKeyUnavailable ? null : scope.EphemeralSecretKey;
            }
            return _productionLedgerSecretKey;
        }
    }

    public static bool IsLedgerKeyAvailable => !string.IsNullOrWhiteSpace(ActiveSecretKey) && ActiveSecretKey!.Length >= 64;

    /// <summary>
    /// Internal test-only scope that provisions an isolated CSPRNG 256-bit HMAC key and isolated Ledger storage
    /// (or simulates an unavailable DPAPI key to verify fail-closed behavior).
    /// </summary>
    internal static IDisposable UseTestEphemeralKeyScope(string? customTestLedgerFilePath = null, bool simulateUnavailableKey = false)
    {
        var previous = _testScope.Value;
        byte[] keyBytes = new byte[32];
        RandomNumberGenerator.Fill(keyBytes);
        string ephemeralHex = Convert.ToHexString(keyBytes).ToLowerInvariant();

        string tempFile = customTestLedgerFilePath ?? Path.Combine(
            Path.GetTempPath(),
            $"pccontrol_test_ledger_{Guid.NewGuid():N}.dpapi"
        );

        _testScope.Value = new TestLedgerScopeContext
        {
            EphemeralSecretKey = simulateUnavailableKey ? null : ephemeralHex,
            IsolatedLedgerFilePath = tempFile,
            SimulateKeyUnavailable = simulateUnavailableKey
        };

        return new TestLedgerDisposableScope(previous, tempFile);
    }

    private static string? TryInitializeProductionLedgerKey()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            string? existing = IntegrityVerifier.ReadProtectedSessionToken(LedgerKeyFilePath);
            if (!string.IsNullOrWhiteSpace(existing) && existing.Length >= 64)
            {
                return existing;
            }

            byte[] keyBytes = new byte[32];
            RandomNumberGenerator.Fill(keyBytes);
            string generated = Convert.ToHexString(keyBytes).ToLowerInvariant();

            var saveReport = IntegrityVerifier.SaveProtectedSessionTokenWithReport(LedgerKeyFilePath, generated);
            if (!saveReport.Success || !saveReport.DpapiApplied)
            {
                return null;
            }

            string? verifiedRead = IntegrityVerifier.ReadProtectedSessionToken(LedgerKeyFilePath);
            if (!string.Equals(verifiedRead, generated, StringComparison.Ordinal))
            {
                return null;
            }

            return generated;
        }
        catch
        {
            // Fail-closed: never fall back to a hardcoded or predictable secret
            return null;
        }
    }

    public static string BuildEntryCanonicalString(RegistryLedgerEntry entry)
    {
        return string.Join("|",
            entry.OperationId ?? string.Empty,
            (entry.TweakId ?? string.Empty).ToLowerInvariant(),
            (entry.Hive ?? string.Empty).ToUpperInvariant(),
            RegistryOptimizer.NormalizeSubKeyPath(entry.SubKeyPath ?? string.Empty).ToLowerInvariant(),
            (entry.ValueName ?? string.Empty).ToLowerInvariant(),
            entry.OriginalValueKind ?? string.Empty,
            entry.WasInitiallyAbsent ? "1" : "0",
            entry.KeyCreatedByApp ? "1" : "0",
            entry.OriginalRawValue ?? "<ABSENT>",
            entry.AppliedValue ?? string.Empty,
            entry.CreatedAtUtc ?? string.Empty,
            entry.IsRestored ? "1" : "0",
            entry.RestoredAtUtc ?? "<NOT_RESTORED>"
        );
    }

    public static string ComputeEntryHmac(RegistryLedgerEntry entry)
    {
        string? secret = ActiveSecretKey;
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("LEDGER_KEY_UNAVAILABLE: Chave de autenticação do Ledger indisponível.");
        }
        return IntegrityVerifier.ComputeHmacSha256(secret, BuildEntryCanonicalString(entry));
    }

    public static bool VerifyEntryHmac(RegistryLedgerEntry? entry)
    {
        string? secret = ActiveSecretKey;
        if (string.IsNullOrWhiteSpace(secret) || entry == null || string.IsNullOrWhiteSpace(entry.EntryHmacSha256))
            return false;

        return IntegrityVerifier.VerifyHmacSha256(
            secret,
            BuildEntryCanonicalString(entry),
            entry.EntryHmacSha256
        );
    }

    public static string BuildLedgerCanonicalString(IEnumerable<RegistryLedgerEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var e in entries.OrderBy(x => x.CreatedAtUtc, StringComparer.Ordinal).ThenBy(x => x.OperationId, StringComparer.Ordinal))
        {
            sb.Append(BuildEntryCanonicalString(e)).Append('|').Append(e.EntryHmacSha256).Append('\n');
        }
        return sb.ToString();
    }

    public static bool IsSupportedRegistryValueKind(RegistryValueKind kind) =>
        kind is RegistryValueKind.DWord
            or RegistryValueKind.QWord
            or RegistryValueKind.String
            or RegistryValueKind.ExpandString
            or RegistryValueKind.MultiString
            or RegistryValueKind.Binary;

    /// <summary>
    /// Records a verified pre-modification state in the backend ledger and returns its unique OperationId.
    /// </summary>
    public static RegistryLedgerEntry RecordBeforeModification(
        TweakDefinition def,
        RegistryValueKind originalKind,
        string? originalRawValue,
        bool wasInitiallyAbsent,
        bool keyCreatedByApp,
        string appliedValue)
    {
        return RecordAppliedTweakState(def, originalRawValue, originalKind, wasInitiallyAbsent, keyCreatedByApp, appliedValue);
    }

    /// <summary>
    /// Records a verified pre-modification state in the backend ledger and returns its unique OperationId.
    /// </summary>
    public static RegistryLedgerEntry RecordAppliedTweakState(
        TweakDefinition def,
        string? originalRawValue,
        RegistryValueKind originalKind,
        bool wasInitiallyAbsent,
        bool keyCreatedByApp,
        string appliedValue)
    {
        lock (_syncRoot)
        {
            if (!IsLedgerKeyAvailable)
            {
                throw new InvalidOperationException("LEDGER_KEY_UNAVAILABLE: Não é possível registrar o estado original sem chave protegida válida.");
            }

            var ledger = LoadVerifiedLedgerInternal() ?? new RegistryLedgerFile();

            var entry = new RegistryLedgerEntry
            {
                OperationId = $"op-{Guid.NewGuid():N}",
                TweakId = def.TweakId,
                Hive = def.Hive,
                SubKeyPath = def.SubKeyPath,
                ValueName = def.ValueName,
                OriginalValueKind = originalKind.ToString(),
                OriginalRawValue = wasInitiallyAbsent ? null : originalRawValue,
                WasInitiallyAbsent = wasInitiallyAbsent,
                KeyCreatedByApp = keyCreatedByApp,
                AppliedValue = appliedValue,
                CreatedAtUtc = DateTime.UtcNow.ToString("O"),
                IsRestored = false,
                RestoredAtUtc = null
            };
            entry.EntryHmacSha256 = ComputeEntryHmac(entry);

            ledger.Entries.Add(entry);
            SaveLedgerInternal(ledger);
            return entry;
        }
    }

    public static LedgerValidationResult ResolveAndValidateRestore(string tweakId, string? operationId = null)
    {
        return ResolveTrustedRestoreEntry(tweakId, operationId);
    }

    /// <summary>
    /// Resolves and strictly validates a trusted restoration state from the backend ledger.
    /// Fails closed if the protected HMAC key is unavailable, if the entry or file HMAC is invalid,
    /// if the TweakId/Hive/SubKeyPath/ValueName does not match the closed catalog, or if OriginalRawValue
    /// cannot be safely converted to OriginalValueKind.
    /// </summary>
    public static LedgerValidationResult ResolveTrustedRestoreEntry(string tweakId, string? operationId = null)
    {
        if (string.IsNullOrWhiteSpace(tweakId) || !RegistryOptimizer.Catalog.TryGetValue(tweakId.Trim(), out var def))
        {
            return new LedgerValidationResult(false, null, "UNCATALOGED_TWEAK",
                $"O ajuste '{tweakId}' não pertence ao catálogo fechado.");
        }

        lock (_syncRoot)
        {
            if (!IsLedgerKeyAvailable)
            {
                return new LedgerValidationResult(false, null, "LEDGER_KEY_UNAVAILABLE",
                    "Chave criptográfica do histórico de restauração indisponível. Operação recusada por segurança.");
            }

            var ledger = LoadVerifiedLedgerInternal(out string? integrityError);
            if (ledger == null)
            {
                return new LedgerValidationResult(false, null, "LEDGER_INTEGRITY_FAILED",
                    integrityError ?? "Histórico confiável do backend ausente ou adulterado.");
            }

            RegistryLedgerEntry? targetEntry;

            if (!string.IsNullOrWhiteSpace(operationId))
            {
                targetEntry = ledger.Entries.FirstOrDefault(e =>
                    string.Equals(e.OperationId, operationId.Trim(), StringComparison.OrdinalIgnoreCase));

                if (targetEntry == null)
                {
                    return new LedgerValidationResult(false, null, "OPERATION_NOT_FOUND",
                        $"Operação '{operationId}' não encontrada no histórico confiável do backend.");
                }

                if (!string.Equals(targetEntry.TweakId, def.TweakId, StringComparison.OrdinalIgnoreCase))
                {
                    return new LedgerValidationResult(false, null, "OPERATION_TWEAK_MISMATCH",
                        $"Violação de segurança: a operação '{operationId}' pertence ao ajuste '{targetEntry.TweakId}' e não pode ser usada para restaurar '{def.TweakId}'.");
                }

                if (targetEntry.IsRestored)
                {
                    return new LedgerValidationResult(false, null, "OPERATION_ALREADY_RESTORED",
                        $"Proteção anti-replay: a operação '{operationId}' já foi restaurada anteriormente em {targetEntry.RestoredAtUtc}.");
                }
            }
            else
            {
                targetEntry = ledger.Entries
                    .Where(e => string.Equals(e.TweakId, def.TweakId, StringComparison.OrdinalIgnoreCase) && !e.IsRestored)
                    .OrderByDescending(e => e.CreatedAtUtc, StringComparer.Ordinal)
                    .FirstOrDefault();

                if (targetEntry == null)
                {
                    bool hasAlreadyRestored = ledger.Entries.Any(e =>
                        string.Equals(e.TweakId, def.TweakId, StringComparison.OrdinalIgnoreCase) && e.IsRestored);

                    if (hasAlreadyRestored)
                    {
                        return new LedgerValidationResult(false, null, "OPERATION_ALREADY_RESTORED",
                            $"O ajuste '{def.TweakId}' já teve sua operação restaurada e não possui estado pendente.");
                    }

                    return new LedgerValidationResult(false, null, "NO_TRUSTED_BACKEND_HISTORY",
                        $"Restauração bloqueada: não existe histórico confiável no backend para o ajuste '{def.TweakId}'. Crie ou utilize um Snapshot verificado.");
                }
            }

            // 1. Verify entry-level HMAC-SHA256 before trusting any field
            if (!VerifyEntryHmac(targetEntry))
            {
                return new LedgerValidationResult(false, null, "ENTRY_HMAC_INVALID",
                    $"Adulteração detectada na entrada de histórico '{targetEntry.OperationId}'.");
            }

            // 2. Confirm TweakId, Hive, SubKeyPath, and ValueName strictly match the closed catalog
            if (!string.Equals(targetEntry.Hive, def.Hive, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(RegistryOptimizer.NormalizeSubKeyPath(targetEntry.SubKeyPath), RegistryOptimizer.NormalizeSubKeyPath(def.SubKeyPath), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(targetEntry.ValueName, def.ValueName, StringComparison.OrdinalIgnoreCase))
            {
                return new LedgerValidationResult(false, null, "ENTRY_PATH_MISMATCH",
                    $"A entrada de histórico não corresponde ao caminho ou nome de valor do catálogo para '{def.TweakId}'.");
            }

            // 3. Validate that OriginalValueKind is a valid, supported RegistryValueKind
            if (!Enum.TryParse<RegistryValueKind>(targetEntry.OriginalValueKind, true, out var parsedOriginalKind) ||
                !IsSupportedRegistryValueKind(parsedOriginalKind))
            {
                return new LedgerValidationResult(false, null, "INCOMPATIBLE_VALUE_KIND",
                    $"Tipo de Registro '{targetEntry.OriginalValueKind}' inválido ou não suportado para restauração.");
            }

            // 4. If the value existed originally, validate conversion of OriginalRawValue to OriginalValueKind (including MultiString and Binary)
            if (!targetEntry.WasInitiallyAbsent)
            {
                if (targetEntry.OriginalRawValue == null ||
                    !RegistryOptimizer.TryDeserializeRegistryValue(targetEntry.OriginalRawValue, parsedOriginalKind, out _, out string? convError))
                {
                    return new LedgerValidationResult(false, null, "INVALID_ORIGINAL_VALUE_FORMAT",
                        $"Valor original '{targetEntry.OriginalRawValue ?? "<NULL>"}' não pode ser convertido com segurança para o tipo '{parsedOriginalKind}': {convError}");
                }
            }

            return new LedgerValidationResult(true, targetEntry, null, null);
        }
    }

    public static bool MarkOperationRestored(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId)) return false;

        lock (_syncRoot)
        {
            if (!IsLedgerKeyAvailable) return false;

            var ledger = LoadVerifiedLedgerInternal();
            if (ledger == null) return false;

            var entry = ledger.Entries.FirstOrDefault(e =>
                string.Equals(e.OperationId, operationId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (entry == null) return false;

            entry.IsRestored = true;
            entry.RestoredAtUtc = DateTime.UtcNow.ToString("O");
            entry.EntryHmacSha256 = ComputeEntryHmac(entry);

            SaveLedgerInternal(ledger);
            return true;
        }
    }

    public static IReadOnlyList<RegistryLedgerEntry> GetAllEntries()
    {
        lock (_syncRoot)
        {
            var ledger = LoadVerifiedLedgerInternal();
            return ledger?.Entries.ToList() ?? new List<RegistryLedgerEntry>();
        }
    }

    public static void ClearLedgerForTesting() => ClearLedger();

    public static void ClearLedger()
    {
        lock (_syncRoot)
        {
            var scope = _testScope.Value;
            if (scope != null)
            {
                scope.InMemoryProtectedFiles.Clear();
            }
            if (File.Exists(LedgerFilePath))
            {
                try { File.Delete(LedgerFilePath); } catch { }
            }
        }
    }

    /// <summary>
    /// Internal test helper to inject an entry into the ledger (used by unit tests to test tamper detection, incompatible kinds, or valid states).
    /// </summary>
    internal static void SaveRawEntryForTesting(RegistryLedgerEntry entry, bool recomputeValidHmac = true)
    {
        lock (_syncRoot)
        {
            var ledger = LoadVerifiedLedgerInternal() ?? new RegistryLedgerFile();
            ledger.Entries.RemoveAll(e => string.Equals(e.OperationId, entry.OperationId, StringComparison.OrdinalIgnoreCase));
            if (recomputeValidHmac)
            {
                entry.EntryHmacSha256 = ComputeEntryHmac(entry);
            }
            ledger.Entries.Add(entry);
            SaveLedgerInternal(ledger);
        }
    }

    private static RegistryLedgerFile? LoadVerifiedLedgerInternal() => LoadVerifiedLedgerInternal(out _);

    private static RegistryLedgerFile? LoadVerifiedLedgerInternal(out string? errorMessage)
    {
        errorMessage = null;
        string? secret = ActiveSecretKey;
        if (string.IsNullOrWhiteSpace(secret))
        {
            errorMessage = "Chave de autenticação do Ledger indisponível.";
            return null;
        }

        string? json = null;
        var scope = _testScope.Value;
        if (scope != null && scope.InMemoryProtectedFiles.TryGetValue(LedgerFilePath, out string? memJson))
        {
            json = memJson;
        }
        else if (!File.Exists(LedgerFilePath))
        {
            return new RegistryLedgerFile();
        }
        else
        {
            json = IntegrityVerifier.ReadProtectedSessionToken(LedgerFilePath);
        }

        try
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                errorMessage = "Não foi possível descriptografar ou ler o Ledger do Registro.";
                return null;
            }

            var ledger = JsonSerializer.Deserialize<RegistryLedgerFile>(json);
            if (ledger == null || ledger.Entries == null)
            {
                errorMessage = "Formato do Ledger de estado do Registro inválido.";
                return null;
            }

            if (!IntegrityVerifier.VerifyHmacSha256(secret, BuildLedgerCanonicalString(ledger.Entries), ledger.LedgerHmacSha256))
            {
                errorMessage = "Assinatura HMAC-SHA256 global do Ledger inválida (adulteração detectada).";
                return null;
            }

            foreach (var entry in ledger.Entries)
            {
                if (!VerifyEntryHmac(entry))
                {
                    errorMessage = $"Assinatura HMAC-SHA256 inválida para a operação '{entry.OperationId}'.";
                    return null;
                }
            }

            return ledger;
        }
        catch (Exception ex)
        {
            errorMessage = $"Erro ao verificar integridade do Ledger: {ex.Message}";
            return null;
        }
    }

    private static void SaveLedgerInternal(RegistryLedgerFile ledger)
    {
        string? secret = ActiveSecretKey;
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("LEDGER_KEY_UNAVAILABLE: Não é possível salvar o Ledger sem chave de autenticação válida.");
        }

        ledger.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
        ledger.LedgerHmacSha256 = IntegrityVerifier.ComputeHmacSha256(secret, BuildLedgerCanonicalString(ledger.Entries));
        string json = JsonSerializer.Serialize(ledger, new JsonSerializerOptions { WriteIndented = true });

        var scope = _testScope.Value;
        if (scope != null)
        {
            scope.InMemoryProtectedFiles[LedgerFilePath] = json;
        }
        if (OperatingSystem.IsWindows())
        {
            IntegrityVerifier.SaveProtectedSessionTokenWithReport(LedgerFilePath, json);
        }
    }
}
