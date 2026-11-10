# Plano de Execução — Correção Crítica, Segurança e Estabilização (PC Control Ultimate)

## Diagnóstico Técnico do Repositório Atual

A análise integral dos arquivos do repositório identificou a causa raiz exata de cada um dos bloqueios apontados na auditoria:

1. **Etapa 1 — Vite, WebSocket e HMR no Google AI Studio (`vite.config.ts`, `server.ts`, `src/main.tsx`):**
   - No container Cloud Run do Google AI Studio (`K_SERVICE=ais-dev-...`, `GOOGLE_RUNTIME=nodejs22`, `APP_URL=https://...`), a variável `DISABLE_HMR` não está definida no ambiente padrão, fazendo com que `process.env.DISABLE_HMR !== 'true'` mantenha o cliente WebSocket de HMR ativo dentro do proxy que bloqueia conexões WebSocket de HMR (`[vite] failed to connect to websocket`, `WebSocket closed without opened`, `Unhandled Rejection`).
   - Além disso, `server.ts` instancia `createViteServer({ server: { middlewareMode: true } })` sem propagar a desativação do HMR/watch quando em ambiente de preview AI Studio.

2. **Etapa 2 — Vulnerabilidade no Fluxo de Restauração do Registro (`RegistryOptimizer.cs`, `PrivilegedAgentServer.cs`, `RegistrySnapshotEngine.cs`):**
   - A rota `POST /api/tweaks/restore` e o método `RegistryOptimizer.RestoreCatalogTweak` ainda aceitam `originalValue`, `valueKind`, `wasInitiallyAbsent` e `keyCreatedByApp` vindos do corpo da requisição HTTP do cliente.
   - Isso permite que um cliente autenticado escolha qualquer valor (ou force a exclusão da chave com `wasInitiallyAbsent: true`) em vez de restaurar o estado real anterior.

3. **Etapa 3 — Compilação e Contrato do `IHardwareMonitorService` (`PCControlUltimate.Core`, `PCControlUltimate.Hardware`):**
   - O contrato `IHardwareMonitorService` e `SensorDataPoint` precisam diferenciar explicitamente três estados técnicos: `SupportedAndAvailable` (leitura válida), `SensorNotSupported` (hardware não expõe o sensor) e `CommunicationFailure` (falha de leitura/permissão UAC), garantindo descarte determinístico (`IDisposable`) e injeção de dependência limpa sem acoplamento nos ViewModels.

4. **Etapa 4 — Vulnerabilidade em `SQLitePCLRaw.lib.e_sqlite3` (`PCControlUltimate.Infrastructure.csproj`):**
   - O pacote `Microsoft.EntityFrameworkCore.Sqlite` (`9.0.2`) traz transitivamente `SQLitePCLRaw.lib.e_sqlite3` `2.1.10`, afetado por advisory de alta severidade.

5. **Etapa 5 — Navegação WPF (`MainWindow.xaml`, `MainViewModel.cs`):**
   - Atualmente toda a interface está concentrada em `MainWindow.xaml` alternando visibilidade de `StackPanel`s por `CurrentTab`, e possui erro de binding (`HardwareCategory` em vez de `HardwareType`). É necessário estruturar navegação real via `INavigationService`, `ContentControl`, `DataTemplate`s, `UserControl`s dedicados em `src/PCControlUltimate.App/Views/` e ViewModels específicos por módulo em `src/PCControlUltimate.App/ViewModels/`, registradas no container de DI (`App.xaml.cs`).

6. **Etapa 6 — Internacionalização WPF (`LocalizationService.cs`, XAML e ViewModels):**
   - Existem strings fixas em XAML (ex.: `"LIBREHARDWAREMONITOR — SENSORES FÍSICOS DETECTADOS"`, `"ITENS CAPTURADOS NO BACKUP SELECIONADO"`, `"Restaurar Item"`) e mensagens de status nos ViewModels que precisam ser movidas para o `LocalizationService` (`pt-BR` e `en-US`) com atualização imediata em tempo de execução.

7. **Etapa 7 — Restore Center e Backup (`RegistrySnapshotEngine.cs`, `RestoreCenterView.xaml`, `RestoreCenterViewModel.cs`):**
   - O Restore Center precisa exibir tipo de snapshot, tamanho em bytes (`FileSizeBytes`), status de verificação de integridade prévia obrigatória, hash SHA-256, escopo completo de itens antes da restauração, progresso real, exigência de confirmação explícita e resultado individual por item.

8. **Etapa 8 — Assinatura Criptográfica HMAC-SHA256 e Endurecimento do Agente Privilegiado (`PrivilegedAgentServer.cs`, `AgentClientService.cs`, `IntegrityVerifier.cs`):**
   - Conforme definido, além do Bearer token protegido por DPAPI + ACL NTFS (validando ACL tanto do arquivo quanto do diretório de segredos) e validação de `Timestamp` + `Nonce`, implementaremos assinatura criptográfica **HMAC-SHA256 por requisição** (`X-PCControl-Signature`) calculada sobre `HTTPMethod + "|" + PathAndQuery + "|" + Timestamp + "|" + Nonce + "|" + Sha256Hex(Body)` usando a chave de sessão protegida por DPAPI.

9. **Etapa 9 — GitHub Actions Windows x64 (`.github/workflows/windows-ci.yml`):**
   - Atualizar o workflow para compilar, testar e publicar explicitamente na arquitetura `x64` (`-p:Platform=x64` / `-r win-x64`), verificando pacotes vulneráveis (`dotnet list package --vulnerable --include-transitive`) e gerando o artefato somente após aprovação integral do build e dos testes.

---

## Plano de Execução por Etapas

### Etapa 1 — Corrigir Vite, WebSocket e HMR no Google AI Studio
- **`vite.config.ts` e `server.ts`:**
  - Detectar de forma determinística o ambiente de preview gerenciado (`process.env.DISABLE_HMR === 'true' || Boolean(process.env.K_SERVICE) || Boolean(process.env.APP_URL) || Boolean(process.env.GOOGLE_RUNTIME)`).
  - Desabilitar `server.hmr` e `server.watch` apenas quando em ambiente de preview AI Studio ou quando `DISABLE_HMR === 'true'`, preservando o HMR ativo no desenvolvimento local padrão.
- **`src/main.tsx`:**
  - Adicionar proteção contra `unhandledrejection` especificamente para falhas transitórias de transporte WebSocket do cliente Vite em iframes de preview, sem mascarar erros reais da aplicação.

### Etapa 2 — Correção Crítica do Restore do Registro (Ledger Backend DPAPI + HMAC-SHA256)
- **`src/PCControlUltimate.Windows/Services/RegistryStateLedger.cs` (Novo serviço backend):**
  - Persistir em `%LOCALAPPDATA%\PCControlUltimate\StateLedger\tweak_state_ledger.dat` (protegido por DPAPI `DataProtectionScope.CurrentUser`, ACL NTFS restrita ao SID atual e autenticado com HMAC-SHA256 interno) cada operação aplicada pelo `RegistryOptimizer.ApplyCatalogTweak`.
  - Cada registro conterá: `OperationId` (GUID único), `TweakId`, `Hive`, `SubKeyPath`, `ValueName`, `OriginalValueKind`, `OriginalRawValue`, `WasInitiallyAbsent`, `KeyCreatedByApp`, `AppliedValue`, `AppliedAtUtc`, `IsRestored` e `EntryHmacSha256`.
- **`src/PCControlUltimate.Windows/Services/RegistryOptimizer.cs` e `PrivilegedAgentServer.cs`:**
  - Remover da rota `/api/tweaks/restore` e de `RestoreCatalogTweak` a aceitação de `originalValue`, `valueKind`, `wasInitiallyAbsent` e `keyCreatedByApp` enviados livremente pelo cliente.
  - Exigir que a restauração ocorra **exclusivamente**:
    1. A partir de uma entrada autêntica e não reutilizada do `RegistryStateLedger` (por `operationId` ou pelo último estado confiável registrado para o `tweakId`); ou
    2. A partir de um arquivo de snapshot validado pelo `RegistrySnapshotEngine` (verificando existência, proveniência, hash SHA-256 canônico, correspondência do `TweakId`, tipo `RegistryValueKind` compatível com o catálogo e valor dentro de `AllowedValues` ou ausência original comprovada).
  - Rejeitar e registrar em auditoria qualquer tentativa de enviar payload adulterado, tipo incompatível, valor fora de `AllowedValues`, snapshot com hash inválido ou ajuste sem histórico confiável no backend.

### Etapa 3 — Corrigir e Expandir `IHardwareMonitorService` e `LibreHardwareMonitorWrapper`
- **`src/PCControlUltimate.Core/Models/HardwareSnapshot.cs` e `src/PCControlUltimate.Core/Interfaces/IHardwareMonitorService.cs`:**
  - Adicionar o enum `SensorReadingStatus { Available, SensorNotSupported, ReadingUnavailable, CommunicationFailure }` em `PCControlUltimate.Core.Models`.
  - Atualizar `SensorDataPoint` e `HardwareSnapshot` para expor o status explícito de cada leitura e diagnósticos de comunicação, mantendo compatibilidade de propriedades para todos os projetos consumidores.
- **`src/PCControlUltimate.Hardware/LibreHardwareMonitorWrapper.cs`:**
  - Implementar `IHardwareMonitorService` diferenciando hardware sem suporte ao sensor (`SensorNotSupported`), leitura nula/indisponível (`ReadingUnavailable`) e falha de abertura/acesso Ring0/WMI (`CommunicationFailure`), além de `Dispose()` seguro liberando o `Timer` e fechando `Computer`.

### Etapa 4 — Corrigir a Vulnerabilidade do SQLite
- **`src/PCControlUltimate.Infrastructure/PCControlUltimate.Infrastructure.csproj`:**
  - Identificar a cadeia transitiva de `Microsoft.EntityFrameworkCore.Sqlite` e atualizar para versão corrigida compatível, além de referenciar explicitamente `SQLitePCLRaw.bundle_e_sqlite3` e `SQLitePCLRaw.lib.e_sqlite3` na versão segura `2.1.11` para eliminar a versão vulnerável `2.1.10`.
- **Verificação automatizada no CI:**
  - Adicionar etapa no `.github/workflows/windows-ci.yml` executando `dotnet list PCControlUltimate.sln package --vulnerable --include-transitive` para comprovar a ausência de pacotes vulneráveis no restore.

### Etapa 5 — Completar a Navegação WPF com Arquitetura MVVM Real
- **`src/PCControlUltimate.App/Services/NavigationService.cs`:**
  - Implementar `INavigationService` centralizado com resolução de ViewModels via `IServiceProvider`.
- **`src/PCControlUltimate.App/ViewModels/Modules/` e `src/PCControlUltimate.App/Views/`:**
  - Criar ViewModels dedicados (`DashboardViewModel`, `HardwareMonitorViewModel`, `RegistryOptimizerViewModel`, `RestoreCenterViewModel`, `DebloatCleanerViewModel`, `NetworkLabViewModel`, `GamingPresentMonViewModel`, `DiagnosticsViewModel`, `DriverCenterViewModel`) e seus respectivos `UserControl`s XAML.
  - Configurar `DataTemplate`s no `App.xaml` / `MainWindow.xaml` associados a um `ContentControl` principal (`Content="{Binding Navigation.CurrentViewModel}"`), eliminando a simulação por ocultação de `StackPanel`s e corrigindo todos os bindings (como `HardwareType` e `Status` dos sensores).

### Etapa 6 — Internacionalização Completa (`pt-BR` e `en-US`)
- **`src/PCControlUltimate.App/Services/LocalizationService.cs`:**
  - Mapear 100% dos textos de navegação, cabeçalhos, botões, confirmações, alertas de segurança, estados de sensores (`Sensor não suportado`, `Leitura indisponível`, `Falha de comunicação`), Restore Center e mensagens de progresso nos dicionários `pt-BR` e `en-US`, atualizando todos os `UserControl`s e ViewModels via binding reativo `{Binding Loc[...]}` sem necessidade de reinicialização.

### Etapa 7 — Completar o Restore Center e Backup
- **`src/PCControlUltimate.Windows/Services/RegistrySnapshotEngine.cs` e `RestoreCenterView.xaml`:**
  - Incluir `SnapshotType`, `FileSizeBytes` e status de verificação prévia em `SnapshotSummaryItem` e `RegistrySnapshotFile`.
  - Exibir na interface do Restore Center: lista de snapshots, data/hora UTC, tipo de snapshot, integridade verificada, hash SHA-256, tamanho do arquivo, pré-visualização completa dos itens e valores que serão restaurados, exigência de confirmação explícita antes da restauração, verificação de elevação UAC quando houver chaves `HKLM`, barra/indicador de progresso real e relatório individual por item restaurado.

### Etapa 8 — Revisar e Endurecer o Agente Privilegiado com Assinatura HMAC-SHA256
- **`src/PCControlUltimate.Security/IntegrityVerifier.cs`:**
  - Adicionar verificação de ACL NTFS tanto para o diretório `%LOCALAPPDATA%\PCControlUltimate` quanto para os arquivos de segredo (`agent_session.dpapi` e `tweak_state_ledger.dat`).
  - Implementar `ComputeRequestHmacSha256(string sessionKey, string method, string pathAndQuery, string timestamp, string nonce, string bodyContent)` e verificação em tempo constante (`CryptographicOperations.FixedTimeEquals`).
- **`src/PCControlUltimate.Windows/Services/PrivilegedAgentServer.cs` e `src/PCControlUltimate.Infrastructure/Services/AgentClientService.cs`:**
  - Exigir e validar `X-PCControl-Signature` (HMAC-SHA256) nas chamadas do cliente WPF, além de Bearer token, `Host`, `Origin`/`X-PCControl-Client`, `Timestamp` e `Nonce` anti-replay, sanitizando logs para nunca expor tokens ou segredos e documentando claramente as garantias e ameaças residuais.

### Etapa 9 — Testes de Segurança xUnit e GitHub Actions Windows x64
- **`tests/PCControlUltimate.Tests/`:**
  - Expandir `RegistryOptimizerTests.cs`, `RegistrySnapshotTests.cs` e `OptimizationSafetyTests.cs` com todos os testes negativos obrigatórios:
    - Rejeição de valor restaurado fora de `AllowedValues`.
    - Rejeição de `RegistryValueKind` incompatível.
    - Rejeição de payload adulterado ou tentativa do frontend de enviar valor original arbitrário.
    - Rejeição de snapshot com hash incorreto ou de outra operação.
    - Bloqueio de restauração de ajuste sem histórico confiável no `RegistryStateLedger`.
    - Restauração exata de registro originalmente inexistente (`WasInitiallyAbsent = true`).
    - Preservação de `String`, `DWord`, `QWord`, `Binary` e `MultiString`.
    - Relatório fiel de falha parcial (nunca reportando restauração completa quando um item falha).
    - Validação de assinatura HMAC-SHA256 por requisição e bloqueio de adulteração de corpo/timestamp/nonce.
- **`.github/workflows/windows-ci.yml`:**
  - Executar em `windows-latest` na ordem estrita:
    1. Checkout (`actions/checkout@v4`)
    2. Setup .NET 10 SDK (`x64`)
    3. Restore (`dotnet restore PCControlUltimate.sln`) + Verificação de vulnerabilidades NuGet (`dotnet list PCControlUltimate.sln package --vulnerable --include-transitive`)
    4. Build Release x64 (`dotnet build PCControlUltimate.sln -c Release -p:Platform=x64 --no-restore`)
    5. Testes Release x64 (`dotnet test PCControlUltimate.sln -c Release -p:Platform=x64 --no-build --verbosity normal`)
    6. Publicação Windows x64 (`dotnet publish src/PCControlUltimate.App/PCControlUltimate.App.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained false -o artifacts/publish/win-x64`)
    7. Empacotamento e Upload do artefato (`if: success()`).
