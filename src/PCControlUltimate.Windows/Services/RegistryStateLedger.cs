using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public string FormatVersion { get; set; } = "2.5";
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
/// Ensures the backend never trusts originalValue, valueKind, wasInitiallyAbsent, or keyCreatedByApp sent by the frontend.
/// </summary>
public static class RegistryStateLedger
{
    private static readonly object _syncRoot = new();
    private static readonly string _ledgerSecretKey = InitializeProcessLedgerKey();

    public static string LedgerFilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCControlUltimate",
        "StateLedger",
        "tweak_state_ledger.dpapi"
    );

    private static string LedgerKeyFilePath => Path.Combine(
        Path.GetDirectoryName(LedgerFilePath) ?? Path.GetTempPath(),
        "ledger_hmac_key.dpapi"
    );

    private static string InitializeProcessLedgerKey()
    {
        try
        {
            string? existing = IntegrityVerifier.ReadProtectedSessionToken(LedgerKeyFilePath);
            if (!string.IsNullOrWhiteSpace(existing) && existing.Length >= 32)
            {
                return existing;
            }

            byte[] keyBytes = new byte[32];
            RandomNumberGenerator.Fill(keyBytes);
            string generated = Convert.ToHexString(keyBytes).ToLowerInvariant();
            IntegrityVerifier.SaveProtectedSessionTokenWithReport(LedgerKeyFilePath, generated);
            return generated;
        }
        catch
        {
            return "PCControlUltimate_Fallback_Ledger_Secret_v25";
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
            entry.IsRestored ? "1" : "0"
        );
    }

    public static string ComputeEntryHmac(RegistryLedgerEntry entry)
    {
        return IntegrityVerifier.ComputeHmacSha256(_ledgerSecretKey, BuildEntryCanonicalString(entry));
    }

    public static bool VerifyEntryHmac(RegistryLedgerEntry? entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.EntryHmacSha256))
            return false;

        return IntegrityVerifier.VerifyHmacSha256(
            _ledgerSecretKey,
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
    /// Blocks replayed operations, mismatched tweakIds, incompatible RegistryValueKinds, tampered HMACs,
    /// or values outside the catalog's AllowedValues.
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

                // Rule 8: Verify that the operation strictly belongs to the tweakId being restored
                if (!string.Equals(targetEntry.TweakId, def.TweakId, StringComparison.OrdinalIgnoreCase))
                {
                    return new LedgerValidationResult(false, null, "OPERATION_TWEAK_MISMATCH",
                        $"Violação de segurança: a operação '{operationId}' pertence ao ajuste '{targetEntry.TweakId}' e não pode ser usada para restaurar '{def.TweakId}'.");
                }

                // Rule 7: Prevent reuse / replay of an already-restored operation
                if (targetEntry.IsRestored)
                {
                    return new LedgerValidationResult(false, null, "OPERATION_ALREADY_RESTORED",
                        $"Proteção anti-replay: a operação '{operationId}' já foi restaurada anteriormente em {targetEntry.RestoredAtUtc}.");
                }
            }
            else
            {
                // Find the most recent un-restored entry for this tweakId
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

            // Verify entry-level HMAC-SHA256
            if (!VerifyEntryHmac(targetEntry))
            {
                return new LedgerValidationResult(false, null, "ENTRY_HMAC_INVALID",
                    $"Adulteração detectada na entrada de histórico '{targetEntry.OperationId}'.");
            }

            // Rule 8: Verify Hive, SubKeyPath, and ValueName match the closed catalog
            if (!string.Equals(targetEntry.Hive, def.Hive, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(RegistryOptimizer.NormalizeSubKeyPath(targetEntry.SubKeyPath), RegistryOptimizer.NormalizeSubKeyPath(def.SubKeyPath), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(targetEntry.ValueName, def.ValueName, StringComparison.OrdinalIgnoreCase))
            {
                return new LedgerValidationResult(false, null, "ENTRY_PATH_MISMATCH",
                    $"A entrada de histórico não corresponde ao caminho ou nome de valor do catálogo para '{def.TweakId}'.");
            }

            // Rule 5: Validate RegistryValueKind against the catalog definition
            if (!Enum.TryParse<RegistryValueKind>(targetEntry.OriginalValueKind, true, out var parsedKind) ||
                parsedKind != def.ValueKind)
            {
                return new LedgerValidationResult(false, null, "INCOMPATIBLE_VALUE_KIND",
                    $"Tipo de Registro '{targetEntry.OriginalValueKind}' incompatível com o tipo exigido pelo catálogo ('{def.ValueKind}').");
            }

            // Rule 6: If the value existed originally, validate that OriginalRawValue is in AllowedValues
            if (!targetEntry.WasInitiallyAbsent)
            {
                if (string.IsNullOrWhiteSpace(targetEntry.OriginalRawValue) ||
                    !def.AllowedValues.Contains(targetEntry.OriginalRawValue, StringComparer.OrdinalIgnoreCase))
                {
                    return new LedgerValidationResult(false, null, "RESTORE_VALUE_NOT_ALLOWED",
                        $"Valor original '{targetEntry.OriginalRawValue ?? "<NULL>"}' fora de AllowedValues para o ajuste '{def.TweakId}'.");
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
            if (File.Exists(LedgerFilePath))
            {
                try { File.Delete(LedgerFilePath); } catch { }
            }
        }
    }

    /// <summary>
    /// Injects an entry into the ledger (used by unit tests to test tamper detection, incompatible kinds, or valid states).
    /// </summary>
    public static void SaveRawEntryForTesting(RegistryLedgerEntry entry, bool recomputeValidHmac = true)
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
        if (!File.Exists(LedgerFilePath))
        {
            return new RegistryLedgerFile();
        }

        try
        {
            string? json = IntegrityVerifier.ReadProtectedSessionToken(LedgerFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                errorMessage = "Não foi possível descriptografar ou ler o Ledger DPAPI do Registro.";
                return null;
            }

            var ledger = JsonSerializer.Deserialize<RegistryLedgerFile>(json);
            if (ledger == null || ledger.Entries == null)
            {
                errorMessage = "Formato do Ledger de estado do Registro inválido.";
                return null;
            }

            if (!IntegrityVerifier.VerifyHmacSha256(_ledgerSecretKey, BuildLedgerCanonicalString(ledger.Entries), ledger.LedgerHmacSha256))
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
        ledger.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
        ledger.LedgerHmacSha256 = IntegrityVerifier.ComputeHmacSha256(_ledgerSecretKey, BuildLedgerCanonicalString(ledger.Entries));
        string json = JsonSerializer.Serialize(ledger, new JsonSerializerOptions { WriteIndented = true });
        IntegrityVerifier.SaveProtectedSessionTokenWithReport(LedgerFilePath, json);
    }
}
