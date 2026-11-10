# PC Control Ultimate — Plano de Correção Crítica de Compilação e Validação CI (Release x64)

Este plano consolida a auditoria técnica (**Fase A**) dos 36 erros e avisos de compilação reportados pelo GitHub Actions na etapa **Build Solution (Release x64)** e define as correções exatas de contratos em C#/.NET 10 sem enfraquecer validações de segurança, HMAC-SHA256, DPAPI ou catálogo fechado do Registro.

---

## User Review & Critical Decisions

> [!IMPORTANT]
> As decisões abaixo foram confirmadas e guiam a configuração de publicação e integração contínua:

- **Decisão Confirmada 1 (Publicação Windows x64 Self-Contained)**: O comando `dotnet publish` no pipeline gerará um pacote **autossuficiente (`--self-contained true -r win-x64`)**, incluindo o runtime .NET 10 Windows Desktop para execução imediata em qualquer máquina Windows 10/11 x64.
- **Decisão Confirmada 2 (Gatilhos do GitHub Actions)**: O workflow de CI será acionado automaticamente em pushes e pull requests nas branches `main`, `master`, `feature/*` e explicitamente `feature/game-network-accelerator`, sem realizar merge automático de Pull Requests.

---

## 1. Overview & Core Concept

- **What It Does**: Elimina todas as divergências de assinatura, tipos de retorno e propriedades entre os serviços de backend (`RegistryStateLedger`, `RegistryOptimizer`, `RegistrySnapshotEngine`, `DiskCleanerEngine`, `PresentMonSessionParser`, `PrivilegedAgentServer`, `HardwareSnapshot`), a camada de apresentação (`MainViewModel`) e a suíte de testes automatizados xUnit (`PCControlUltimate.Tests`).
- **Target Audience / Persona**: Engenheiros e usuários finais em Windows 10/11 x64 que necessitam de uma build `Release x64` limpa, auditável e validada por testes automatizados no GitHub Actions.
- **Key Value**: Garante compilação 100% limpa em `Release x64`, execução integral da suíte de testes de segurança sem remover nenhum teste ou enfraquecer verificações criptográficas, e geração verificada do artefato executável nativo.

---

## 2. User Experience & Visual Design

- **Key User Flows**:
  - **Restore Center & Otimizador de Registro**: Quando o usuário aplica ou reverte um ajuste, a `MainViewModel` consome o resultado real de `SnapshotItemRestoreReport` (`RestoredAndVerified` e `Message`) e do `RegistryOperationResult` (`VerifiedCurrentValue` / `VerifiedValue`), exibindo feedback fiel por item sem estados simulados.
  - **Hardware Monitor**: Quando o subsistema de sensores (`LibreHardwareMonitor`) é consultado, `HardwareSnapshot` expõe `Status`, `DiagnosticReason` e `IsHardwareMonitorAvailable`, diferenciando claramente leitura física disponível de `SensorNotSupported`, `ReadingUnavailable` e `CommunicationFailure`.
- **Visual Identity & Theme**:
  - Preserva integralmente a identidade gamer dark premium (`#07090E` background, `#131722` cards, `#A855F7` roxo neon, `#C084FC` violeta, tipografia `Consolas` para telemetria e hashes SHA-256).

---

## 3. Key Product Decisions & Trade-Offs (Fase A — Diagnóstico de Origem dos Erros)

- **1. Restore Center, Ledger DPAPI e Registro (`RegistryStateLedger`, `RegistryOptimizer`, `RegistrySnapshotEngine`)**:
  - *Origem do Erro*:
    - `RegistryLedgerEntry` foi declarado como `class` em vez de `record`, impedindo o uso da expressão `with` nos testes de adulteração de HMAC (`tamperedEntry = recorded with { ... }`).
    - `RegistryStateLedger` expôs os métodos `ClearLedger`, `ResolveTrustedRestoreEntry` e `RecordAppliedTweakState`, enquanto a suíte de testes e chamadas internas referenciam `ClearLedgerForTesting`, `ResolveAndValidateRestore`, `RecordBeforeModification` e `VerifyEntryHmac`, além da propriedade `ErrorReason` em `LedgerValidationResult`.
    - `RegistryOptimizer.ValidateRestoreParametersAgainstCatalog` possuía apenas a sobrecarga de 3 elementos na tupla `(bool isValid, RegistryValueKind parsedKind, string? errorMessage)` recebendo `TweakDefinition`, enquanto os testes de segurança validam por `tweakId` (`string`) e `RegistryValueKind` desconstruindo uma tupla de 2 elementos `(bool isValid, string errorReason)` contendo os códigos `RESTORE_VALUE_NOT_ALLOWED` e `INCOMPATIBLE_VALUE_KIND`.
    - `RegistryOptimizer.RestoreCatalogTweak` possuía assinatura com parâmetros string em ordem distinta da sobrecarga tipada `(string tweakId, bool wasInitiallyAbsent, string? originalRawValue, RegistryValueKind originalKind, bool keyCreatedByApp)` usada para comprovar que valores arbitrários do cliente são ignorados em favor do Ledger DPAPI.
    - `RegistryOperationResult` expunha `VerifiedValue`, mas não o alias `VerifiedCurrentValue`.
    - `RegistryOptimizer.Catalog` continha `"game-fso-override"`, enquanto os testes referenciavam `"perf-fso-flags"`.
    - Em `RegistrySnapshotTests.cs`, `UnreadableItems` (do tipo `List<RegistryReadFailure>`) recebia `new List<RegistrySnapshotReadFailure>()`, causando erro de invariância genérica `CS0029`.
  - *Solução*: Converter `RegistryLedgerEntry` para `record`, adicionar os métodos/sobrecargas compatíveis em `RegistryStateLedger` e `RegistryOptimizer` preservando todas as travas de segurança, expor `VerifiedCurrentValue` em `RegistryOperationResult`, garantir a entrada `"perf-fso-flags"` no catálogo fechado e tipar `new List<RegistryReadFailure>` no teste de snapshot incompleto.

- **2. Disk Cleaner (`DiskCleanerEngine` & `DiskCleanerTests`)**:
  - *Origem do Erro*: `DiskDryRunReport` não continha as propriedades `Success` (indicando se a categoria solicitada no Dry-Run é válida e segura) e `EstimatedFilesCount` (alias semântico de `CandidateFilesCount`).
  - *Solução*: Adicionar `bool Success` ao contrato `DiskDryRunReport` (retornando `false` para categorias inválidas/proibidas e `true` para categorias autorizadas) e expor `EstimatedFilesCount => CandidateFilesCount`.

- **3. PresentMon (`PresentMonSessionParser` & `PresentMonParserTests`)**:
  - *Origem do Erro*: `IsProcessNameValid` exigia obrigatoriamente o parâmetro `out string sanitizedExecutableName`, quebrando chamadas de validação booleana direta com 1 argumento.
  - *Solução*: Adicionar a sobrecarga `public static bool IsProcessNameValid(string? processName) => IsProcessNameValid(processName, out _);` mantendo a validação estrita por regex e bloqueio de metacaracteres.

- **4. Autenticação Criptográfica HMAC-SHA256 (`PrivilegedAgentServer` & `OptimizationSafetyTests`)**:
  - *Origem do Erro*: `ValidateRequestHmacSignature` possuía apenas a sobrecarga de 6 parâmetros, enquanto a validação completa de cliente + assinatura utiliza 7 parâmetros `(clientHeader, signatureHeader, httpMethod, pathAndQuery, timestampHeader, nonceHeader, bodyContent)` e verifica a mensagem em português (`"HMAC-SHA256 inválida"`), além do identificador `"PCControl-WPF/2.5"` em `AllowedClientIdentifiers`.
  - *Solução*: Registrar `"PCControl-WPF/2.5"` em `AllowedClientIdentifiers` e implementar a sobrecarga de 7 parâmetros em `PrivilegedAgentServer` que valida tanto o identificador do cliente quanto o HMAC-SHA256 em tempo constante (`CryptographicOperations.FixedTimeEquals`), sem jamais enfraquecer a verificação.

- **5. `MainViewModel` e `HardwareSnapshot`**:
  - *Origem do Erro*: `MainViewModel` referenciava `itemReport.RestoredAndVerified` e `itemReport.Message` em `SnapshotItemRestoreReport`, além de `snap.Status`, `snap.DiagnosticReason` e `snap.IsHardwareMonitorAvailable` em `HardwareSnapshot`.
  - *Solução*: Expor essas propriedades diretamente nos records `SnapshotItemRestoreReport` e `HardwareSnapshot`, refletindo fielmente o estado real da operação e do sensor (`MonitorStatus == SensorReadingStatus.Available` sem jamais inventar leituras).

---

## 4. Technical Architecture & Data Strategy *(Technical Reference)*

### Diagrama de Contratos Corrigidos

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                     PCControlUltimate.Tests (xUnit)                          │
│  • RegistryOptimizerTests  • RegistrySnapshotTests  • OptimizationSafetyTests│
│  • DiskCleanerTests        • PresentMonParserTests                           │
└───────────────┬─────────────────────────────┬────────────────────────────────┘
                │                             │
                ▼                             ▼
┌───────────────────────────────────┐ ┌────────────────────────────────────────┐
│   PCControlUltimate.Windows       │ │   PCControlUltimate.Core & Hardware    │
│ • RegistryStateLedger (record)    │ │ • HardwareSnapshot                     │
│   - RecordBeforeModification()    │ │   - Status / DiagnosticReason          │
│   - ResolveAndValidateRestore()   │ │   - IsHardwareMonitorAvailable         │
│   - VerifyEntryHmac()             │ │ • PresentMonSessionParser              │
│   - ClearLedgerForTesting()       │ │   - IsProcessNameValid(name)           │
│ • RegistryOptimizer               │ └───────────────────┬────────────────────┘
│   - ValidateRestoreParameters...  │                     │
│     (2-tuple & 3-tuple overloads) │                     ▼
│   - RestoreCatalogTweak(...)      │ ┌────────────────────────────────────────┐
│   - VerifiedCurrentValue          │ │   PCControlUltimate.App (WPF MVVM)     │
│ • RegistrySnapshotEngine          │ │ • MainViewModel                        │
│   - SnapshotItemRestoreReport     │ │   - Consome SnapshotItemRestoreReport  │
│     (RestoredAndVerified/Message) │ │     e HardwareSnapshot reais           │
│ • DiskCleanerEngine               │ └────────────────────────────────────────┘
│   - DiskDryRunReport              │
│     (Success/EstimatedFilesCount) │
│ • PrivilegedAgentServer           │
│   - ValidateRequestHmacSignature  │
│     (6-arg & 7-arg overloads)     │
└───────────────────────────────────┘
```

### Sequência de Validação e CI (Fases B, C e D)
1. **Fase B**: Aplicação cirúrgica das correções de contrato e eliminação de warnings de referência nula (`CS8600`/`CS8602`/`CS8604`/`CS8618`).
2. **Fase C & D**: Atualização do workflow Windows x64 para incluir a branch `feature/game-network-accelerator` e `feature/*`, compilando em `Release` (`-p:Platform=x64`), executando `dotnet test` e publicando com `--self-contained true -r win-x64`.
