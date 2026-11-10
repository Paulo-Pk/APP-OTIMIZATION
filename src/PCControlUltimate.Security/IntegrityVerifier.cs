using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace PCControlUltimate.Security;

public static class IntegrityVerifier
{
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
    /// </summary>
    public static bool SaveProtectedSessionToken(string filePath, string plainToken)
    {
        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            byte[] plainBytes = Encoding.UTF8.GetBytes(plainToken);
            byte[] storedBytes;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                byte[] entropy = Encoding.UTF8.GetBytes("PCControlUltimate_v2.4_IPC_Salt");
                storedBytes = ProtectedData.Protect(plainBytes, entropy, DataProtectionScope.CurrentUser);
            }
            else
            {
                storedBytes = plainBytes;
            }

            File.WriteAllBytes(filePath, storedBytes);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                RestrictFileAclToCurrentUser(filePath);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string? ReadProtectedSessionToken(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            byte[] storedBytes = File.ReadAllBytes(filePath);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                byte[] entropy = Encoding.UTF8.GetBytes("PCControlUltimate_v2.4_IPC_Salt");
                byte[] plainBytes = ProtectedData.Unprotect(storedBytes, entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }

            return Encoding.UTF8.GetString(storedBytes);
        }
        catch
        {
            return null;
        }
    }

    private static void RestrictFileAclToCurrentUser(string filePath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity.User == null) return;

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
        }
        catch
        {
            // Ignore ACL adjustment errors on filesystems without NTFS ACL support
        }
    }
}