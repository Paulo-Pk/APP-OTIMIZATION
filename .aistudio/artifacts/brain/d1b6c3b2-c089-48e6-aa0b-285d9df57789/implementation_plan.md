# PC Control Ultimate — Plano de Correção Crítica Pós-Auditoria GitHub

---

## 1. Correção Imediata dos 4 Erros de Compilação (Sem Dependência Circular)

### Causa Raiz Identificada
- `PCControlUltimate.Windows.csproj` já referencia `PCControlUltimate.Diagnostics.csproj`, enquanto `PCControlUltimate.Diagnostics.csproj` referencia apenas `PCControlUltimate.Core.csproj`.
- Em `RecommendationEngine.cs` (`PCControlUltimate.Diagnostics`), havia `using PCControlUltimate.Windows.Services;` e referência a `NetworkDiagnosticsReport` (definido em `PCControlUltimate.Windows`), causando os erros:
  1. `RecommendationEngine.cs: namespace PCControlUltimate.Windows não encontrado`
  2. `RecommendationEngine.cs: NetworkDiagnosticsReport não encontrado`
- Em `SystemDoctorService.cs` (`PCControlUltimate.Diagnostics`), havia duas referências a `SystemStabilityAuditor`, classe que ainda não estava implementada dentro de `PCControlUltimate.Diagnostics`:
  3. `SystemDoctorService.cs: SystemStabilityAuditor não encontrado` (propriedade/construtor)
  4. `SystemDoctorService.cs: SystemStabilityAuditor não encontrado` (instanciação default)

### Solução Arquitetural
1. Criar `src/PCControlUltimate.Core/Models/NetworkDiagnosticsModels.cs` em `PCControlUltimate.Core.Models` contendo os contratos compartilhados:
   - `IcmpProbeStatus` (`Responded`, `PartialLoss`, `FilteredOrBlocked`, `Unreachable`, `NotTested`)
   - `TcpConnectionProbeState` (`Connected`, `Timeout`, `ConnectionRefused`, `FilteredPort`, `ConnectionFailure`)
   - `DnsResolutionState` (`Resolved`, `DnsResolutionFailure`, `CacheHit`)
   - `StructuredNetworkDiagnosticFinding`, `NetworkTargetProbeResult`, `NetworkDiagnosticsReport` e `stringTargetSummary`.
2. Atualizar `RecommendationEngine.cs` para usar exclusivamente `PCControlUltimate.Core.Models` (removendo `using PCControlUltimate.Windows.Services;`).
3. Implementar `src/PCControlUltimate.Diagnostics/SystemStabilityAuditor.cs` (e adicionar `System.Diagnostics.EventLog` em `PCControlUltimate.Diagnostics.csproj`) para ler eventos reais do Windows Event Log (`System` — `Kernel-Power` ID 41, `WHEA-Logger`, `Display` ID 4101) com degradação segura caso o log esteja inacessível.

---

## 2. Universal Hardware Engine — Enumeração Independente Multi-GPU e Zero Dados Estimados

### Correções em `HardwareSnapshot.cs`, `LibreHardwareMonitorWrapper.cs` e `UniversalHardwareEngine.cs`
1. **Enumeração Independente de Todas as GPUs Instaladas**:
   - Expandir `HardwareSnapshot` com `IReadOnlyList<GpuAdapterTelemetryPoint>? GpuAdapters = null` para preservar cada adaptador gráfico detectado individualmente (com nome, tipo de hardware, temperatura Core, Hotspot, carga, clock, potência, VRAM usada/total e RPM de ventoinha próprios).
   - Em `UniversalHardwareEngine.cs`, enumerar todas as GPUs combinando `snapshot.GpuAdapters`, consulta WMI (`Win32_VideoController` para AdapterRAM, DriverVersion, PNPDeviceID e múltiplos adaptadores AMD, NVIDIA, Intel e Qualcomm) e Registro de classe de vídeo (`HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}`), mantendo identificação, sensores, capacidades e status independentes por GPU (dedicada, integrada, híbrida ou múltiplas GPUs).
2. **Eliminação de Dados Estimados ou Fixos**:
   - **Núcleos Físicos (`int? PhysicalCores`)**: Consultar `Win32_Processor` (`NumberOfCores` e `NumberOfLogicalProcessors`) no Windows; quando WMI não estiver disponível, retornar `null` (indisponível) em vez de dividir `Environment.ProcessorCount / 2`.
   - **Geração e Velocidade de Memória RAM (`string? MemoryGeneration`, `int? ConfiguredSpeedMt`)**: Consultar `Win32_PhysicalMemory` (`SMBIOSMemoryType` -> DDR3/DDR4/DDR5/LPDDR4/LPDDR5 e `ConfiguredClockSpeed`); quando não reportado pelo SMBIOS/WMI, retornar `"Indisponível"` e `null` em vez da string genérica `"DDR4 / DDR5 / LPDDR"`.
   - **Bateria (`DetectedBatteryPowerInfo`)**: Ler `SYSTEM_POWER_STATUS.BatteryLifePercent` real via Win32 `GetSystemPowerStatus` (quando `<= 100`, usar o percentual real; quando `255` / desconhecido ou sem bateria, retornar `null` em vez de `100` fixo) e `ACLineStatus` real (`0` = Bateria, `1` = AC Conectado).
   - **Zero Prioridade para RX 7900 XTX**: Garantir que nenhuma lógica global ou adaptador trate a `RX 7900 XTX` como padrão global.

---

## 3. Network Diagnostics — Separação Estrita entre Perda ICMP e Resultado TCP

### Correções em `NetworkDiagnosticsService.cs`
- Nunca usar o sucesso do handshake TCP para definir `PacketLossPercent = 0.0` do ICMP.
- Quando todas as sondagens ICMP falharem (`icmpSamples.Count == 0`):
  - Se o handshake TCP:443 conectar (`TcpConnectionProbeState.Connected`), classificar `IcmpStatus = IcmpProbeStatus.FilteredOrBlocked` e `IcmpPacketLossPercent = null` (Inconclusivo / Filtrado na borda ou firewall), mantendo o resultado TCP (`Connected` + latência TCP) completamente independente!
  - Se tanto ICMP quanto TCP falharem, reportar `IcmpStatus = IcmpProbeStatus.Unreachable` e `IcmpPacketLossPercent = 100.0`.
- Separar em propriedades explícitas no relatório:
  - `IcmpStatus` (`Responded`, `PartialLoss`, `FilteredOrBlocked`, `Unreachable`)
  - `IcmpPacketLossPercent` (`double?` — `null` quando ICMP está filtrado/inconclusivo)
  - `TcpState` (`Connected`, `Timeout`, `ConnectionRefused`, `FilteredPort`, `ConnectionFailure`)
  - `DnsState` (`Resolved`, `DnsResolutionFailure`, `CacheHit`)
- Adicionar testes automatizados garantindo que:
  1. O erro anterior de 50% de perda artificial nunca retorne.
  2. Quando ICMP está bloqueado mas TCP conecta, o estado ICMP seja `FilteredOrBlocked` (inconclusivo) sem forçar perda ICMP para 0% ou 100% com base no TCP.

---

## 4. Software Center — Pesquisa Dinâmica WinGet, Versões e Assinatura Digital Real

### Evolução em `WingetSoftwareCenterService.cs`
- Substituir o booleano fixo `VerifiedDigitalSignature = true` por um enum real `SignatureVerificationState` (`NotVerifiedYet`, `VerifiedAuthenticode`, `VerifiedWingetManifestHash`, `UnsignedOrInvalid`, `VerificationUnavailable`) e método real `VerifyFileAuthenticodeSignature(string filePath)` usando `X509Certificate.CreateFromSignedFile` / `X509Certificate2`.
- Adicionar métodos dinâmicos reais via CLI `winget`:
  - `SearchDynamicWingetAsync(string query, CancellationToken ct)`: executa `winget search` e faz o parse das linhas retornadas (Nome, ID, Versão, Origem).
  - `GetInstalledPackagesAsync(string? filterQuery, CancellationToken ct)`: executa `winget list` e identifica versão instalada e versão disponível para atualização.
  - Progresso real via `IProgress<string>` capturando o stream de saída do `winget` linha a linha, validação estrita da origem (`--source winget`) e bloqueio total de instalação sem confirmação explícita (`userConfirmed == true`).

---

## 5. GPU Thermal Auditor — Limites Justificados e Transparência de Dados

### Refinamento em `GpuThermalAuditor.cs`
- Não utilizar a `RX 7900 XTX` como padrão global em nenhum método.
- Não inventar limites térmicos arbitrários por fabricante quando o hardware não informar o limite; avaliar com base em faixas técnicas conservadoras e informar explicitamente `"Avaliação térmica limitada: sensor de Hotspot/Junction não disponibilizado por esta GPU/driver"` quando apenas a temperatura Core existir, ou `"Avaliação térmica indisponível"` quando nenhum sensor térmico estiver acessível.

---

## 6. Testes Automatizados e Verificação de Build
- Atualizar e expandir `UniversalHardwareAndCompatibilityTests.cs` e `GameNetworkAcceleratorTests.cs` cobrindo:
  - Enumeração multi-GPU independente (AMD + Radeon, Intel + NVIDIA, Intel Arc, GPU Integrada, Notebook Híbrido iGPU + dGPU, Múltiplas GPUs dedicadas).
  - Bateria real vs. desconhecida (nunca 100% fixo sem leitura real), núcleos físicos reais vs. indisponíveis (`null`), geração de RAM real vs. `"Indisponível"`.
  - Separação estrita entre `IcmpProbeStatus.FilteredOrBlocked`, `TcpConnectionProbeState` e `DnsResolutionState`, e regressão contra os 50% de perda artificial.
  - Verificação dinâmica e de assinatura no `WingetSoftwareCenterService`.
- Verificar se o SDK `.NET` está instalado no container Linux atual (`dotnet restore`, `dotnet build`, `dotnet test`, `dotnet publish`) e relatar com 100% de transparência e evidência real o resultado de cada comando executado no ambiente.
