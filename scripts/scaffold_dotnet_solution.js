import fs from 'fs';
import path from 'path';

const rootDir = process.cwd();

const requiredSolutionPaths = [
  'PCControlUltimate.sln',
  'src/PCControlUltimate.Core/PCControlUltimate.Core.csproj',
  'src/PCControlUltimate.Infrastructure/PCControlUltimate.Infrastructure.csproj',
  'src/PCControlUltimate.Hardware/PCControlUltimate.Hardware.csproj',
  'src/PCControlUltimate.Windows/PCControlUltimate.Windows.csproj',
  'src/PCControlUltimate.Windows/Services/RegistryBackend.cs',
  'src/PCControlUltimate.Windows/Services/RegistryOptimizer.cs',
  'src/PCControlUltimate.Windows/Services/RegistryStateLedger.cs',
  'src/PCControlUltimate.Windows/Services/RegistrySnapshotEngine.cs',
  'src/PCControlUltimate.Gaming/PCControlUltimate.Gaming.csproj',
  'src/PCControlUltimate.Diagnostics/PCControlUltimate.Diagnostics.csproj',
  'src/PCControlUltimate.Security/PCControlUltimate.Security.csproj',
  'src/PCControlUltimate.App/PCControlUltimate.App.csproj',
  'src/PCControlUltimate.App/MainWindow.xaml',
  'src/PCControlUltimate.App/ViewModels/MainViewModel.cs',
  'tests/PCControlUltimate.Tests/PCControlUltimate.Tests.csproj',
  'tests/PCControlUltimate.Tests/RegistryOptimizerTests.cs',
  'installer/PCControlUltimate-Setup.iss',
];

console.log('Verifying PCControlUltimate .NET 10 solution structure (read-only)...');

let missingCount = 0;
for (const relPath of requiredSolutionPaths) {
  const fullPath = path.join(rootDir, relPath);
  if (fs.existsSync(fullPath)) {
    console.log('Verified existing file:', relPath);
  } else {
    console.error('Missing required solution file:', relPath);
    missingCount++;
  }
}

if (missingCount > 0) {
  console.error(`Verification failed: ${missingCount} required file(s) missing.`);
  process.exitCode = 1;
} else {
  console.log('All 9 projects and core implementation files in PCControlUltimate.sln verified without overwriting.');
}
