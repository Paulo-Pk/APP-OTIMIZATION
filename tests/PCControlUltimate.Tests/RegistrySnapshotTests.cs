using System.IO;
using System.Text;
using PCControlUltimate.Windows.Services;
using Xunit;

namespace PCControlUltimate.Tests;

public class RegistrySnapshotTests
{
    [Fact]
    public void VerifyBackupIntegrity_WithTamperedFile_ShouldDetectChecksumMismatch()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            string originalJson = "{\n" +
                                  "  \"SnapshotId\": \"snap-test-123\",\n" +
                                  "  \"CreatedUtc\": \"2026-11-10T12:00:00Z\",\n" +
                                  "  \"Description\": \"Test Snapshot\",\n" +
                                  "  \"TotalKeysCaptured\": 0,\n" +
                                  "  \"Entries\": [],\n" +
                                  "  \"ChecksumSha256\": \"1111222233334444555566667777888899990000aaaabbbbccccddddeeeeffff\"\n" +
                                  "}";

            File.WriteAllText(tempFile, originalJson, Encoding.UTF8);

            var (isValid, checksum, error) = RegistrySnapshotEngine.VerifyBackupIntegrity(tempFile);

            Assert.False(isValid);
            Assert.Contains("Checksum mismatch", error);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void VerifyBackupIntegrity_WithNonExistentFile_ReturnsFalse()
    {
        var (isValid, _, error) = RegistrySnapshotEngine.VerifyBackupIntegrity("invalid_non_existent_snapshot.json");
        Assert.False(isValid);
        Assert.NotNull(error);
    }
}
