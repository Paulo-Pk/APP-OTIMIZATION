# PC Control Ultimate — Plano de Correção Crítica da Última Atualização (`9feb7d6`)

---

## 1. Correção Imediata dos Erros de Compilação (`PrivilegedAgentServer.cs` & `MainViewModel.cs`)

### Causa Raiz Identificada
- Em `src/PCControlUltimate.Windows/Services/PrivilegedAgentServer.cs` (linhas 496 e 503) e `src/PCControlUltimate.App/ViewModels/MainViewModel.cs` (linhas 900, 925, 937 e 948):
  1. `NetworkDiagnosticsService.RunRealDiagnosticsAsync()` foi chamado estaticamente após ter sido convertido em método de instância.
  2. `NetworkDiagnosticsService.MeasureExternalThroughputAsync()` e `GetDiagnosticHistory()` (e os records `NetworkEndpointTestResult`, `DnsQueryResult` e `ExternalThroughputResult`) foram omitidos na refatoração anterior de `NetworkDiagnosticsService.cs`.

### Solução Arquitetural
1. **`NetworkDiagnosticsService.cs` & `NetworkDiagnosticsModels.cs`**:
   - Restaurar e implementar de forma 100% real (nunca simulada):
     - `MeasureExternalThroughputAsync(CancellationToken)`: realiza download HTTPS real contra `https://speed.cloudflare.com/__down?bytes=2000000` usando `HttpClient` com `HttpCompletionOption.ResponseHeadersRead` e `Stopwatch`, retornando `ExternalThroughputResult(bool Available, double? DownloadMbps, long BytesTransferred, long DurationMs, string SourceUrl, string? ErrorReason)`.
     - `GetDiagnosticHistory()`: retorna o histórico real de diagnósticos e medições de throughput executados na sessão.
     - Records de compatibilidade com a UI (`NetworkEndpointTestResult` com `Name`, `IcmpRoundtripMs`, `TcpConnectMs`, `Note`, e `DnsQueryResult`) populados diretamente pelos resultados reais em `NetworkDiagnosticsReport`.
2. **Injeção de Dependência (`PrivilegedAgentServer.cs`, `App.xaml.cs` e `MainViewModel.cs`)**:
   - Registrar `services.AddSingleton<NetworkDiagnosticsService>()` em `App.xaml.cs`.
   - Receber `NetworkDiagnosticsService` via construtor em `PrivilegedAgentServer` e `MainViewModel` (mantendo sobrecarga default segura para testes existentes de `PrivilegedAgentServer`), eliminando as chamadas estáticas quebradas nas linhas 496 e 503 de `PrivilegedAgentServer.cs` e nas linhas 900–948 de `MainViewModel.cs`.

---

## 2. Correção da Enumeração e Mesclagem Multi-GPU (`UniversalHardwareEngine.cs`)

### Causa Raiz Identificada
- Em `UniversalHardwareEngine.cs`, a consulta complementar WMI/Registro só executava quando `snapshot.GpuAdapters == null && string.IsNullOrWhiteSpace(snapshot.GpuModelName)`. Em sistemas híbridos (Intel + NVIDIA, AMD + NVIDIA, AMD iGPU + AMD dGPU) ou multi-GPU onde o LibreHardwareMonitor detecta apenas parte dos adaptadores, os demais adaptadores do Windows eram ignorados.

### Solução Arquitetural
- Executar **sempre** a enumeração dos adaptadores de `snapshot.GpuAdapters` (LibreHardwareMonitor) **e** a consulta complementar de adaptadores de vídeo do Windows (`Win32_VideoController` + Registro de Classe Display `{4d36e968-e325-11ce-bfc1-08002be10318}` ou lista complementar injetável para testes determinísticos).
- Implementar `MergeGpuAdapters(IReadOnlyList<GpuAdapterTelemetryPoint> lhmAdapters, IReadOnlyList<GpuAdapterTelemetryPoint> wmiAdapters)`:
  - Mesclar por `PNPDeviceID` normalizado (PCI Vendor/Device/Subsys) ou nome canônico do adaptador, evitando duplicações.
  - Preservar integralmente os sensores válidos do LibreHardwareMonitor (`CoreTempC`, `HotspotTempC`, `LoadPercent`, `CoreClockMhz`, `PowerWatts`, `FanRpm`, `VramUsedMb`) e enriquecer com metadados do WMI/Registro (`DriverVersion`, `PnpDeviceId`, `VramTotalMb` 64-bit) sem jamais sobrescrever um sensor válido com `null`.
  - Nunca presumir que uma GPU integrada é a principal quando houver GPU dedicada presente.

---

## 3. Correção Definitiva dos Diagnósticos de Rede (`NetworkDiagnosticsService.cs`)

### Causa Raiz Identificada
- Em `NetworkDiagnosticsReport`, o campo `PacketLossPercent` era `double` não-anulável e usava `overallIcmpLossPercent ?? 0.0`, transformando um estado ICMP nulo/inconclusivo (`FilteredOrBlocked`) em `0.0%` de perda!
- Além disso, ausência de resposta ICMP sem evidência complementar precisa diferenciar entre:
  - `IcmpProbeStatus.Responded` (0% perda comprovada por amostras)
  - `IcmpProbeStatus.PartialLoss` (perda parcial comprovada por amostras)
  - `IcmpProbeStatus.FilteredOrBlocked` (ICMP sem resposta, mas host alcançável em TCP/DNS -> perda ICMP inconclusiva / `null`)
  - `IcmpProbeStatus.Unreachable` (destino comprovadamente inacessível em múltiplas sondagens e protocolos)
  - `IcmpProbeStatus.Inconclusive` (teste inconclusivo / sem evidência suficiente para afirmar 100% de perda)

### Solução Arquitetural
- Alterar `PacketLossPercent` em `NetworkDiagnosticsReport` para `double?` (anulável) e adicionar `string FormattedPacketLoss` que retorna `"Não mensurável (ICMP Filtrado/Inconclusivo)"` quando `PacketLossPercent == null`, nunca inventando `0.0%` ou `100.0%` sem evidência.
- Separar `AverageIcmpLatencyMs` (`double?`) de `AverageTcpHandshakeMs` (`double?`) para que latência ICMP e tempo de handshake TCP jamais sejam misturados na mesma métrica.

---

## 4. Validação Criptográfica Authenticode Real via `WinVerifyTrust` (`WingetSoftwareCenterService.cs`)

### Causa Raiz Identificada
- `X509Certificate.CreateFromSignedFile` apenas extrai o certificado embutido sem validar o hash criptográfico Authenticode do binário PE/MSI contra adulteração, além de gerar aviso de API obsoleta (`SYSLIB0057`).

### Solução Arquitetural
- Implementar P/Invoke nativo para `WinVerifyTrust` (`wintrust.dll`) com `WINTRUST_ACTION_GENERIC_VERIFY_V2` (`{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}`), `WINTRUST_DATA` e `WINTRUST_FILE_INFO`:
  - Validar integridade criptográfica do hash do arquivo, cadeia de confiança, validade temporal e política de revogação (`WTD_REVOKE_WHOLECHAIN` quando online, ou retorno explícito `InconclusiveRevocationOffline` quando sem rede).
  - Extrair dados do certificado signatário sem usar `X509Certificate.CreateFromSignedFile` obsoleto, eliminando avisos de compilação e operações assíncronas inadequadas.

---

## 5. Testes Automatizados Obrigatórios
- Expandir `tests/PCControlUltimate.Tests/UniversalHardwareAndCompatibilityTests.cs` com testes cobrindo:
  1. Mesclagem multi-GPU LHM + WMI por `PNPDeviceID` sem duplicar e sem sobrescrever sensores válidos nos cenários:
     - Notebook híbrido Intel iGPU + NVIDIA dGPU
     - Notebook híbrido AMD iGPU + NVIDIA dGPU
     - Sistema AMD iGPU + AMD Radeon dGPU
     - Workstation com múltiplas GPUs dedicadas
  2. Diagnóstico de rede nos 5 cenários:
     - Firewall bloqueando ICMP com TCP funcionando (`FilteredOrBlocked`, `PacketLossPercent == null`, `"Não mensurável"`)
     - ICMP funcionando com TCP bloqueado/recusado (`Responded`, `PacketLossPercent == 0.0`, `TcpState == ConnectionRefused`)
     - Perda parcial real de pacotes ICMP (`PartialLoss`)
     - Teste inconclusivo (`Inconclusive`)
     - Ausência total de conectividade (`Unreachable`)
  3. Verificação Authenticode via `WinVerifyTrust` e endpoints de `PrivilegedAgentServer` (`/api/network/diagnostics` e `/api/network/throughput`).
