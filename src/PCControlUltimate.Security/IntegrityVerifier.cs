using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace PCControlUltimate.Security;

public record TokenProtectionResult(
    bool Success,
    bool DpapiProtected,
    bool AclRestricted,
    string? ErrorMessage
);

public static class IntegrityVerifier
{
    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("PCControlUltimate_v2.5_IPC_Salt");

    public static string ComputeCanonicalSha256(string canonicalText)
    {
        using var sha = SHA256.Create();
        byte[] bytes = Encoding.UTF8.GetBytes(canonicalText);
        byte[] hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool VerifyFileSha256(string filePath, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(expectedHash) || !File.Exists(filePath))
            return false;

        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        byte[] hash = sha.ComputeHash(stream);
        string calculated = Convert.ToHexString(hash).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(calculated),
            Encoding.UTF8.GetBytes(expectedHash.Trim().ToLowerInvariant())
        );
    }

    /// <summary>
    /// Saves the agent session token protected via Windows DPAPI (CurrentUser scope)
    /// and restricts file ACLs to the current Windows user only.
    /// Never reports success on Windows if either DPAPI or NTFS ACL application fails.
    /// </summary>
    public static TokenProtectionResult SaveProtectedSessionTokenWithReport(string filePath, string plainToken)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(plainToken))
        {
            return new TokenProtectionResult(false, false, false, "File path or session token is empty.");
        }

        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            byte[] plainBytes = Encoding.UTF8.GetBytes(plainToken);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                byte[] encryptedBytes;
                try
                {
                    encryptedBytes = ProtectedData.Protect(plainBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
                }
                catch (Exception dpapiEx)
                {
                    return new TokenProtectionResult(false, false, false, $"DPAPI protection failed: {dpapiEx.Message}");
                }

                File.WriteAllBytes(filePath, encryptedBytes);

                if (!TryRestrictFileAclToCurrentUser(filePath, out string? aclError))
                {
                    try { File.Delete(filePath); } catch { }
                    return new TokenProtectionResult(false, true, false, $"NTFS ACL restriction failed: {aclError}");
                }

                return new TokenProtectionResult(true, true, true, null);
            }
            else
            {
                // Non-Windows environment fallback (e.g., Linux CI/test container):
                // Write file for local test roundtrip, but explicitly report that Windows DPAPI/ACL were not applied.
                File.WriteAllBytes(filePath, plainBytes);
                return new TokenProtectionResult(
                    Success: true,
                    DpapiProtected: false,
                    AclRestricted: false,
                    ErrorMessage: "Non-Windows OS: Windows DPAPI and NTFS ACL are not available in this environment."
                );
            }
        }
        catch (Exception ex)
        {
            return new TokenProtectionResult(false, false, false, ex.Message);
        }
    }

    public static bool SaveProtectedSessionToken(string filePath, string plainToken)
    {
        var report = SaveProtectedSessionTokenWithReport(filePath, plainToken);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return report.Success && report.DpapiProtected && report.AclRestricted;
        }
        return report.Success;
    }

    public static string? ReadProtectedSessionToken(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            byte[] storedBytes = File.ReadAllBytes(filePath);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                byte[] plainBytes = ProtectedData.Unprotect(storedBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }

            return Encoding.UTF8.GetString(storedBytes);
        }
        catch
        {
            return null;
        }
    }

    public static bool TryRestrictFileAclToCurrentUser(string filePath, out string? errorMessage)
    {
        errorMessage = null;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            errorMessage = "NTFS ACL restriction requires Windows OS.";
            return false;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity.User == null)
            {
                errorMessage = "Current WindowsIdentity SID is null.";
                return false;
            }

            var fileInfo = new FileInfo(filePath);
            var security = fileInfo.GetAccessControl();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            var currentRules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
            foreach (FileSystemAccessRule rule in currentRules)
            {
                security.RemoveAccessRule(rule);
            }

            security.AddAccessRule(new FileSystemAccessRule(
                identity.User,
                FileSystemRights.FullControl,
                AccessControlType.Allow));

            fileInfo.SetAccessControl(security);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}