# PC Control Ultimate — Correção Final dos 17 Erros de Compilação do CI

Este plano detalha a resolução definitiva dos 17 erros de compilação identificados no workflow Windows x64 (`UniversalHardwareAndCompatibilityTests.cs`), alinhando os contratos reais de diagnóstico de rede e verificação criptográfica Authenticode (`WinVerifyTrust`) entre os serviços de produção e a suíte de testes automatizados sem remover nenhum teste ou validação de segurança.

## User Review & Critical Decisions

> [!IMPORTANT]
> As decisões abaixo refletem as escolhas confirmadas para eliminar os 17 erros de compilação mantendo 100% da cobertura de testes de rede e segurança Authenticode.

- **Decisão Confirmada 1 — Expansão de Contratos Reais + Alinhamento de Assinaturas nos Testes**: Expandiremos os contratos reais em `NetworkDiagnosticsModels`, `NetworkDiagnosticsService` e `WingetSoftwareCenterService` (incluindo propriedades criptográficas `IsCryptographicallyValid` e `IsTimestamped`, constantes públicas de HRESULT do `WinVerifyTrust`, sobrecarga determinística de `MapWinTrustHResultToVerificationResult`, estados explícitos de rede `TimedOut`, `NetworkUnreachable`, `ServerUnreachable` e parâmetro opcional `explicitDestinationUnreachableCount`) e alinharemos as chamadas em `UniversalHardwareAndCompatibilityTests.cs`.
- **Decisão Confirmada 2 — Sincronização de Artefatos para Branch de Correção**: Além de atualizar os arquivos C# de produção e testes, sincronizaremos o manifesto de exportação da solução (`solutionFiles.ts`) para permitir aplicação imediata na branch de correção rumo ao workflow Windows x64.

---

## 1. Overview & Core Concept

- **What It Does**: Elimina a divergência de contratos entre os serviços recém-atualizados (`NetworkDiagnosticsService` e `WingetSoftwareCenterService`) e a suíte `UniversalHardwareAndCompatibilityTests`, garantindo que todos os cenários de rede (ICMP filtrado, perda parcial, inconclusivo, destino inacessível, falha DNS) e todos os estados criptográficos do `WinVerifyTrust` (`VerifiedAuthenticode`, `TamperedOrBadDigest`, `ExpiredCertificate`, `UntrustedRoot`, `InconclusiveRevocationOffline`, `UnsignedOrInvalid`) compilem e passem nos testes em `Release | x64`.
- **Target Audience / Persona**: Engenheiros de software, auditores de segurança e usuários avançados do PC Control Ultimate em Windows 10 e Windows 11.
- **Key Value**: Restaura a compilação limpa (`0 errors`) de toda a solução `.sln` no GitHub Actions sem sacrificar nenhuma validação real de hardware, rede ou assinatura digital.

---

## 2. User Experience & Visual Design

- **Key User Flows**:
  - **Diagnóstico de Rede Transparente**: O usuário visualiza separadamente o estado ICMP (`Responded`, `PartialLoss`, `FilteredOrBlocked`, `Inconclusive`, `Unreachable`), o estado do handshake TCP (`Connected`, `Timeout`/`TimedOut`, `ConnectionRefused`, `FilteredPort`, `NetworkUnreachable`, `ConnectionFailure`) e o estado DNS (`Resolved`, `CacheHit`, `DnsResolutionFailure`, `ServerUnreachable`).
  - **Verificação Criptográfica no Software Center**: Antes de qualquer execução ou validação de binário local, o usuário recebe o veredito criptográfico completo do `WinVerifyTrust`, incluindo se a assinatura é criptograficamente válida (`IsCryptographicallyValid`) e se possui carimbo de tempo (`IsTimestamped`).
- **Visual Identity & Theme**:
  - Preserva integralmente a identidade visual escura de engenharia de precisão do PC Control Ultimate (`#0A0D14` background, `#00F0FF` ciano para telemetria verificada, `#10B981` esmeralda para integridade confirmada, `#F59E0B` âmbar para estados inconclusivos/filtrados e `#EF4444` vermelho para adulteração de hash ou falha crítica).
- **Interactive Feedback & Motion**:
  - Indicadores claros de status para testes inconclusivos ou filtrados por firewall, evitando falsos alertas de `100%` ou `0%` de perda de pacotes.

---

## 3. Key Product Decisions & Trade-Offs

- **Decision 1 — Exposição Pública das Constantes HRESULT do `WinVerifyTrust` e Propriedades de `AuthenticodeVerificationResult`**
  - *Chosen Approach*: Tornar públicas as constantes `HResultSuccess` (`0`), `TrustENoSignature` (`0x800B0100`), `TrustEExplicitDistrust` (`0x800B0111`), `TrustESubjectNotTrusted` (`0x800B0004`), `TrustEBadDigest` (`0x80096010`), `CertEExpired` (`0x800B0101`), `CertEUntrustedRoot` (`0x800B0109`), `CertERevoked` (`0x800B010C`), `CertERevocationFailure` (`0x800B010E`) e `CryptESecuritySettings` (`0x80092026`), além de adicionar `IsCryptographicallyValid` e `IsTimestamped` em `AuthenticodeVerificationResult` e suportar a sobrecarga de `MapWinTrustHResultToVerificationResult` usada pelos testes.
  - *Why*: Permite que a suíte de testes valide diretamente os códigos nativos do Windows SDK sem duplicar números mágicos nos testes e sem depender de mocks artificiais.
  - *Alternatives Considered*: Remover as asserções de HRESULT dos testes foi descartado porque reduziria a garantia contra regressões na validação Authenticode.

- **Decision 2 — Unificação dos Estados de Sondagem TCP/DNS e Evidência Explícita de Inacessibilidade ICMP**
  - *Chosen Approach*: Incluir os membros explícitos `TimedOut` e `NetworkUnreachable` em `TcpConnectionProbeState`, `ServerUnreachable` em `DnsResolutionState`, e o parâmetro opcional `int explicitDestinationUnreachableCount = 0` em `NetworkDiagnosticsService.EvaluateTargetProbeOutcome`.
  - *Why*: Permite distinguir com precisão cirúrgica entre um timeout silencioso de amostra única (`Inconclusive`) e uma inacessibilidade confirmada por ICMP `DestinationUnreachable` + falha de rota TCP (`Unreachable` com `100.0%` de perda).
  - *Alternatives Considered*: Simplificar o enum para um único estado genérico de erro foi descartado por ocultar a causa raiz (timeout vs. rede inacessível).

---

## 4. Technical Architecture & Data Strategy *(Technical Reference)*

### Architecture & Contract Alignment Diagram

```
┌────────────────────────────────────────────────────────────────────────────┐
│                        PCControlUltimate.Core.Models                       │
│  • IcmpProbeStatus: Responded | PartialLoss | FilteredOrBlocked |          │
│                     Inconclusive | Unreachable | NotTested                 │
│  • TcpConnectionProbeState: Connected | Timeout | TimedOut |               │
│                             ConnectionRefused | FilteredPort |             │
│                             NetworkUnreachable | ConnectionFailure         │
│  • DnsResolutionState: Resolved | CacheHit | DnsResolutionFailure |        │
│                        ServerUnreachable                                   │
└─────────────────────────────────────┬──────────────────────────────────────┘
                                      │
                  ┌───────────────────┴───────────────────┐
                  ▼                                       ▼
┌───────────────────────────────────────┐ ┌──────────────────────────────────┐
│      NetworkDiagnosticsService        │ │   WingetSoftwareCenterService    │
│  • EvaluateTargetProbeOutcome(        │ │  • Public WinTrust HRESULT consts│
│      ...,                             │ │    (HResultSuccess,              │
│      explicitDestinationUnreachable   │ │     TrustEBadDigest,             │
│      Count = 0)                       │ │     CertEExpired,                │
│  • BuildAggregateReportFromTargets()  │ │     CertEUntrustedRoot,          │
│  • MeasureExternalThroughputAsync()   │ │     CertERevocationFailure,      │
│  • RunRealDiagnosticsAsync()          │ │     TrustENoSignature)           │
└─────────────────┬─────────────────────┘ │  • AuthenticodeVerificationResult│
                  │                       │    + IsCryptographicallyValid    │
                  │                       │    + IsTimestamped               │
                  │                       │  • MapWinTrustHResultTo          │
                  │                       │    VerificationResult overloads  │
                  └───────────┬───────────┴───────────────┬──────────────────┘
                              ▼                           ▼
┌────────────────────────────────────────────────────────────────────────────┐
│            PCControlUltimate.Tests (Release | x64 Verification)            │
│  • UniversalHardwareAndCompatibilityTests (Multi-GPU, 5 cenários ICMP/TCP, │
│    6 estados criptográficos WinVerifyTrust, validação anti-estimativa)     │
└────────────────────────────────────────────────────────────────────────────┘
```

### Mapeamento Exato dos 17 Erros e Soluções Técnicas

1. **Erros 1 a 5 — Contratos de Rede (`TcpConnectionProbeState`, `DnsResolutionState` e `EvaluateTargetProbeOutcome`)**:
   - Adicionar `TimedOut` e `NetworkUnreachable` ao enum `TcpConnectionProbeState` e tratá-los no `ProbeTcpEndpointAsync` e `EvaluateTargetProbeOutcome`.
   - Adicionar `ServerUnreachable` ao enum `DnsResolutionState` e tratá-lo em `BuildAggregateReportFromTargets` e `BuildStructuredFinding`.
   - Adicionar o parâmetro opcional `int explicitDestinationUnreachableCount = 0` em `NetworkDiagnosticsService.EvaluateTargetProbeOutcome`, garantindo que quando `explicitDestinationUnreachableCount > 0` ou múltiplas amostras falham com `NetworkUnreachable`/`Timeout`/`TimedOut`, o estado seja `IcmpProbeStatus.Unreachable` (`100.0%`), enquanto 1 única amostra sem resposta ICMP (`explicitDestinationUnreachableCount == 0`) resulte em `IcmpProbeStatus.Inconclusive` (`null`).
2. **Erros 6 a 17 — Contratos Authenticode (`WingetSoftwareCenterService` e `AuthenticodeVerificationResult`)**:
   - Alterar a visibilidade das constantes `HResultSuccess`, `TrustENoSignature`, `TrustEExplicitDistrust`, `TrustESubjectNotTrusted`, `TrustEBadDigest`, `CertEExpired`, `CertEUntrustedRoot`, `CertERevoked`, `CertERevocationFailure` e `CryptESecuritySettings` de `private const int` para `public const int`.
   - Adicionar ao record `AuthenticodeVerificationResult` o parâmetro opcional `bool IsTimestamped = false` e a propriedade calculada `public bool IsCryptographicallyValid => State == SignatureVerificationState.VerifiedAuthenticode;`.
   - Extrair detecção real de atributo de contra-assinatura/timestamp PKCS#9 (`1.2.840.113549.1.9.6`) ou presença de carimbo de tempo em `TryReadSignerMetadata`, e prover sobrecarga compatível de `MapWinTrustHResultToVerificationResult(int hResult, string filePath, string? signerSubject = null, string? issuer = null, string? thumbprint = null, bool isTimestamped = false, DateTime? validUntilUtc = null)` para que todas as chamadas dos testes compilem e validem cada estado criptográfico determinísticamente.
