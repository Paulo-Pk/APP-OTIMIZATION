# Plano de Implementação — Integração Segura, Eliminação de Avisos (`CS8602` / `CA1416`) e Validação Final

## 1. Visão Geral da Integração e Preservação
O objetivo desta etapa é garantir que a árvore da branch `feature/game-network-accelerator` incorpore integralmente as correções validadas na execução `42907069357` (`104/104` testes aprovados), eliminando os avisos de compilação remanescentes (`CS8602` e `CA1416`) sem suprimir alertas (`NoWarn`) e preservando todos os commits e funcionalidades exclusivas do **Game Network Accelerator**.

---

## 2. Alterações Técnicas Planejadas

### Etapa 1 — Correção dos Avisos `CS8602` em `MainViewModel.cs` (Linhas 745 e 793)
- **Arquivo**: `/src/PCControlUltimate.App/ViewModels/MainViewModel.cs`
- **Causa**: A propriedade `SnapshotRestoreResult.ItemReports` é anulável (`IReadOnlyList<SnapshotItemRestoreReport>?`), gerando `CS8602` ao iterar diretamente em `foreach (var itemReport in report.ItemReports)` dentro de `RestoreSelectedBackupCommand` (linha 745) e `RestoreSingleBackupEntryCommand` (linha 793).
- **Correção**: Utilizar coalescência nula segura `foreach (var itemReport in report.ItemReports ?? Array.Empty<SnapshotItemRestoreReport>())` em ambos os pontos.

### Etapa 2 — Correção dos Avisos `CA1416` nas APIs do Registro e Hardware Windows
- **Arquivos**:
  - `/src/PCControlUltimate.Windows/Services/RegistryOptimizer.cs`
  - `/src/PCControlUltimate.Windows/Services/RegistrySnapshotEngine.cs`
  - `/src/PCControlUltimate.Windows/Services/RegistryStateLedger.cs`
  - `/src/PCControlUltimate.Security/IntegrityVerifier.cs`
  - `/src/PCControlUltimate.Hardware/LibreHardwareMonitorWrapper.cs`
  - `/tests/PCControlUltimate.Tests/RegistryOptimizerTests.cs`
  - `/tests/PCControlUltimate.Tests/RegistrySnapshotTests.cs`
- **Causa**: O analisador Roslyn `CA1416` no `.NET 10` exige `[SupportedOSPlatform("windows")]` ou guardas `OperatingSystem.IsWindows()` (em vez de `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`) para chamadas a `Microsoft.Win32.Registry`, `RegistryKey`, `RegistryHive`, `RegistryValueKind`, `ProtectedData` e `System.Management`.
- **Correção**:
  - Adicionar `[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]` (e atributos `[SupportedOSPlatform("windows")]` nas classes de serviço Windows/Testes) além de substituir `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` por `OperatingSystem.IsWindows()` nos métodos de acesso ao Registro, sem usar `<NoWarn>`.

### Etapa 3 — Verificação de Preservação dos Contratos Críticos
1. **`RegistryOptimizer` & `RegistryStateLedger`**:
   - Preservar `RegistryOperationResult.ErrorCode` e o identificador `NO_TRUSTED_BACKEND_HISTORY`, mantendo o bloqueio fail-closed de qualquer restauração sem histórico autenticado.
2. **Dependências NuGet**:
   - Manter `Microsoft.EntityFrameworkCore.Sqlite 9.0.4`, `Microsoft.Extensions.DependencyInjection 9.0.4`, `SQLitePCLRaw.bundle_e_sqlite3 3.0.5` e `SQLitePCLRaw.core 3.0.5`, sem `SQLitePCLRaw.lib.e_sqlite3 2.1.11` e sem a referência redundante `Microsoft.Win32.Registry`.
3. **Game Network Accelerator**:
   - Preservar `GameNetworkAcceleratorService` (detecção de processos/sockets, sondas multi-amostra ICMP/TCP, traceroute TTL, comparador honesto de rotas e histórico) e `WireGuardTunnelManager` (`WireGuardConfigAuditResult` com `AltersDefaultRoute`, `AltersDefaultWindowsRoute`, `AltersIpv4DefaultRoute`, `AltersIpv6DefaultRoute` e verificação de handshake real).
