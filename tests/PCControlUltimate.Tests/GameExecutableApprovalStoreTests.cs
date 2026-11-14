using System;
using System.IO;
using PCControlUltimate.Windows.Services;
using Xunit;

namespace PCControlUltimate.Tests;

public sealed class GameExecutableApprovalStoreTests
{
    [Fact]
    public void Approval_IsOneTime_AndReturnsOnlyTheRegisteredExecutable()
    {
        string executablePath = CreateTemporaryExecutable();
        try
        {
            string approvalId = GameExecutableApprovalStore.Register(executablePath);

            Assert.True(GameExecutableApprovalStore.TryConsume(approvalId, out string approvedPath));
            Assert.Equal(Path.GetFullPath(executablePath), approvedPath);
            Assert.False(GameExecutableApprovalStore.TryConsume(approvalId, out string secondPath));
            Assert.Equal(string.Empty, secondPath);
        }
        finally
        {
            if (File.Exists(executablePath))
                File.Delete(executablePath);
        }
    }

    [Fact]
    public void Approval_RejectsExecutableChangedAfterSelection()
    {
        string executablePath = CreateTemporaryExecutable();
        try
        {
            string approvalId = GameExecutableApprovalStore.Register(executablePath);
            File.WriteAllText(executablePath, "tampered executable content with a different length");

            Assert.False(GameExecutableApprovalStore.TryConsume(approvalId, out string approvedPath));
            Assert.Equal(string.Empty, approvedPath);
        }
        finally
        {
            if (File.Exists(executablePath))
                File.Delete(executablePath);
        }
    }

    [Fact]
    public void Register_RejectsFilesThatAreNotExecutables()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(filePath, "not an executable");

            Assert.Throws<InvalidOperationException>(() => GameExecutableApprovalStore.Register(filePath));
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    private static string CreateTemporaryExecutable()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.exe");
        File.WriteAllText(path, "test executable placeholder");
        return path;
    }
}
