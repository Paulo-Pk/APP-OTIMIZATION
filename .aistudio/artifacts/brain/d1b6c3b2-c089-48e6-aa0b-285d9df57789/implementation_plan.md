# PC Control Ultimate — Master Upgrade (Ultimate Edition)
## Relatório Técnico Inicial (Auditoria do Repositório) & Plano de Execução em 10 Fases

---

## 1. Relatório Técnico Inicial (Auditoria Completa do Projeto)

### 1.1 Arquitetura e Módulos Existentes
- **`PCControlUltimate.Core`**: Contratos, modelos de telemetria (`HardwareTelemetrySnapshot`, `TweakDefinition`, `GameSessionSummary`), interfaces de serviços e ledger.
- **`PCControlUltimate.Hardware`**: `LibreHardwareMonitorWrapper` (sensores de CPU/GPU/RAM/Armazenamento com fallback WMI) e `SmartStorageMonitor` (saúde SMART/NVMe).
- **`PCControlUltimate.Windows`**: `RegistryOptimizer` (tweaks com backup DPAPI+HMAC e verificação pós-escrita), `StartupManager`, `PowerPlanController`, `ProcessGovernor`, `StorageMaintenanceService`, `NetworkDiagnosticsService`, `WindowsUpdatePauseManager`, `BcdEditWrapper`.
- **`PCControlUltimate.Diagnostics`**: `SystemStabilityAuditor` (WHEA / Kernel-Power 41 / Display 4101), `GpuThermalAuditor`, `CpuWheaDetector`, `RamStabilityAnalyzer`, `BenchmarkEngine`, ` BottleneckCalculator`, `DriverInventoryService`, `ThermalAlertMonitor`.
- **`PCControlUltimate.Gaming`**: `GameProfileManager`, `PresentMonWrapper` (1% / 0.1% Low FPS reais via ETW/PresentMon), `FpsOverlayWindow`, `GameNetworkAcceleratorService` e `WireGuardTunnelManager`.
- **`PCControlUltimate.Storage`**: Persistência SQLite (`TelemetryDbContext`, `RegistryStateLedger`, `ProfileRepository`, `RollbackHistoryManager`).
- **`PCControlUltimate.PrivilegedAgent`**: Serviço Windows via Named Pipe com validação de ACL (`CreatorsOwnerSid`, `BuiltinAdministratorsSid`, `LocalSystemSid`) e verificação de assinatura Authenticode.
- **`PCControlUltimate.App` + Web Preview (`src/*`, `server.ts`)**: Interface unificada de 17 módulos em tema grafite/violeta escuro.

### 1.2 Acoplamentos de Hardware Identificados (Dependência Global de `RX 7900 XTX`)
1. **`src/PCControlUltimate.Diagnostics/GpuThermalAuditor.cs` (linhas 23–30)**:
   - Contém lógica hardcoded `bool isRdna3Flagship = gpuName.Contains("7900 XTX") || gpuName.Contains("7900 XT")` como fluxo principal de avaliação de delta térmico.
2. **`src/PCControlUltimate.Hardware/LibreHardwareMonitorWrapper.cs` (linhas 111, 365–369)**:
   - O método `GetHardwarePriorityScore` atribui pontuação máxima (`return 100`) especificamente para `name.Contains("7900 XTX")`, privilegiando um único modelo de GPU em vez de classificar genericamente GPUs dedicadas (AMD Radeon, NVIDIA GeForce/RTX, Intel Arc) vs. GPUs integradas (iGPU).
3. **`src/PCControlUltimate.App/Services/LocalizationService.cs` (linhas 73, 195) e `MainViewModel.cs` (linha 1251)**:
   - Textos de subtítulo e alertas térmicos citam explicitamente `AMD Radeon RX 7900 XTX` como se fosse a GPU fixa do sistema.
4. **`src/services/hardwareService.ts`**:
   - Fallback do Web Preview usa strings fixas da `RX 7900 XTX` quando executado fora do host Windows.

### 1.3 Causa Exata da Perda Artificial de 50% de Pacotes no Diagnóstico de Rede
- **Arquivo**: `src/PCControlUltimate.Windows/Services/NetworkDiagnosticsService.cs` (linhas 61–67 e 103–134).
- **Causa Raiz**:
  1. O array `targets` testa conexões **TCP** (`MeasureTcpHandshakeMsAsync`) contra 4 alvos:
     - `155.133.248.34:27015` (porta **UDP** de servidor de jogo Valve — conexões TCP na porta 27015 sofrem timeout/recusa).
     - `104.160.152.3:443` (IP de borda anycast da Riot Direct que não aceita handshake TCP 443).
     - `1.1.1.1:443` (Cloudflare — responde TCP 443).
     - `8.8.8.8:443` (Google DNS — responde TCP 443).
  2. Como 2 dos 4 alvos sempre falham no handshake TCP (6 falhas em 12 tentativas), a linha 134 (`double packetLoss = (double)failedProbes / totalProbes * 100.0`) calcula **exatamente 50,0% de perda de pacotes artificial**, além de confundir falha/recusa de porta TCP (`ConnectionRefused` / `FilteredPort` / `Timeout`) com perda real de pacotes ICMP (`PacketLossPercent`).

---

## 2. Plano de Implementação em 10 Fases

### Fase 1: Universal Hardware Engine & Remoção de Dependência Global da RX 7900 XTX
- Criar `src/PCControlUltimate.Core/Models/UniversalHardwareModels.cs`:
  - `DetectedMetric<T>` com metadados completos: `Value`, `Source` (`LibreHardwareMonitor`, `WMI`, `DXGI`, `SMART`, `NetworkInterface`), `UpdatedAtUtc`, `Reliability`, `Availability`, `SensorStatus` (`Available`, `Unavailable`, `BlockedByDriver`, `RequiresAdmin`, `NotSupported`) e `Limitations`.
  - `HardwareComponentCapabilityMatrix` com flags explícitas: `Identified`, `Monitorable`, `SensorAvailable`, `Adjustable`, `BenchmarkCompatible`, `DiagnosticCompatible`, `RequiresDriver`, `RequiresAdmin`, `NotSupported`.
  - `SystemHardwareProfile` (`Gamer`, `Notebook`, `Corporate`, `Basic`, `Workstation`, `HighPerformance`) determinado dinamicamente pelas características reais do hardware.
- Criar `src/PCControlUltimate.Hardware/UniversalHardwareEngine.cs`:
  - Pipeline multi-fonte (LibreHardwareMonitor -> WMI -> Win32/DXGI -> DriveInfo/SMART -> NetworkInterface -> SystemPowerStatus) cobrindo CPU (Intel, AMD, Qualcomm), GPU (AMD Radeon, NVIDIA GeForce/RTX Workstation, Intel Arc/iGPU, Qualcomm Adreno, Múltiplas GPUs híbridas), Placa-Mãe/BIOS, RAM, Armazenamento (NVMe/SATA/HDD/USB), Monitores, Rede, Áudio, USB e Bateria/Energia.
  - Adaptadores opcionais por fabricante (`AmdHardwareAdapter`, `NvidiaHardwareAdapter`, `IntelHardwareAdapter`, `QualcommHardwareAdapter`), onde `RX 7900 XTX` é apenas um perfil opcional dentro do `AmdHardwareAdapter`.
- Refatorar `LibreHardwareMonitorWrapper.cs`, `GpuThermalAuditor.cs`, `LocalizationService.cs`, `MainViewModel.cs` e `src/services/hardwareService.ts` para remover toda referência global fixa à `RX 7900 XTX`.

### Fase 2: Windows Compatibility Engine
- Criar `src/PCControlUltimate.Windows/Services/WindowsCompatibilityEngine.cs`:
  - Detectar versão e edição do Windows (Home, Pro, Enterprise, Education, LTSC), build (22H2, 23H2, 24H2+), arquitetura (`x64`, `ARM64`), elevação UAC, políticas de grupo (GPO/MDM), status do Windows Defender/ Seguranças e restrições de Registro/serviços.
  - Classificar qualquer operação nos 6 estados obrigatórios: `Compatible`, `CompatibleWithLimitations`, `RequiresAdmin`, `BlockedByPolicy`, `NotSupported`, `UnexpectedFailure`, sem jamais tentar burlar políticas corporativas.

### Fase 3: Hardware Intelligence & Dashboard Adaptativo
- Integrar a classificação automática de perfil (`Gamer`, `Notebook`, `Corporate`, `Basic`, `Workstation`, `HighPerformance`) e a matriz de capacidades por componente no Dashboard principal (WPF e Web Preview), exibindo badges claros de proveniência do sensor e modo degradado gracioso quando sensores estiverem indisponíveis.

### Fase 4: Windows Tweaks — Catálogo Inteligente em 11 Categorias
- Expandir e auditar `src/PCControlUltimate.Windows/Services/RegistryOptimizer.cs` para cobrir as 11 categorias (`Desempenho`, `Inicialização`, `Serviços`, `Privacidade`, `Interface`, `Jogos`, `Memória`, `Armazenamento`, `Rede`, `Energia`, `Manutenção`):
  - Cada tweak inclui: Nome, Categoria, Descrição Técnica, Impacto Esperado, Nível de Risco (`Seguro`, `Moderado`, `Avançado`), Requisitos de Sistema, Estado Atual (`Ativo`, `Inativo`, `Parcial`, `Não Suportado`), Backup DPAPI+HMAC no `RegistryStateLedger`, Verificação pós-aplicação e Reversão individual.

### Fase 5: Inteligência de Rede & Correção Definitiva do Bug de 50% de Perda TCP
- Refatorar `src/PCControlUltimate.Windows/Services/NetworkDiagnosticsService.cs` e `/api/network/measure-targets` em `server.ts`:
  - Separar rigorosamente **Perda de Pacotes ICMP** (`Ping` multi-amostra), **Handshake TCP** (`Connected`, `Timeout`, `ConnectionRefused`, `FilteredPort`, `ConnectionFailure`) e **Resolução DNS** (`Resolved`, `DnsResolutionFailure`, `CacheHit`).
  - Substituir alvos UDP/filtrados (`155.133.248.34:27015` e `104.160.152.3:443`) por múltiplos endpoints TCP/HTTPS reais e verificáveis (`1.1.1.1:443`, `8.8.8.8:443`, `9.9.9.9:443`, `208.67.222.222:443`) + testes ICMP separados.
  - Estruturar cada diagnóstico com os 6 campos obrigatórios: **O que foi testado**, **O que falhou**, **Evidência encontrada**, **Causa provável**, **Solução recomendada** e **Como verificar se foi resolvido**.

### Fase 6: Game Network Accelerator Evoluído
- Expandir `src/PCControlUltimate.Gaming/Network/GameNetworkAcceleratorService.cs`:
  - Medição real de Latência, Jitter, Perda de Pacotes ICMP, Tempo de DNS, Estabilidade, Tipo de Interface (Ethernet vs. Wi-Fi), Velocidade Negociada do Link (Mbps) e Tráfego RX/TX.
  - Suporte aos 5 modos de operação (`Automático`, `Gaming`, `Baixa latência`, `Estabilidade`, `Personalizado`) com comparativo real antes/depois e reversão segura.

### Fase 7: Software Center (Central de Aplicativos via WinGet)
- Criar `src/PCControlUltimate.Windows/Services/WingetSoftwareCenterService.cs`:
  - Catálogo curado nas 14 categorias solicitadas (Navegadores, Comunicação, Produtividade, Compactadores, Segurança, Drivers, Monitoramento, Hardware, Gaming, Benchmark, Áudio/Vídeo, Desenvolvimento, Ferramentas de Sistema, Personalização).
  - Busca, verificação de pacote instalado, instalação, atualização e desinstalação via `winget` (`--accept-package-agreements --accept-source-agreements --disable-interactivity`), fila de execução com cancelamento, indicação de requisito UAC e validação de código de saída.

### Fase 8: System Doctor, Gaming Center, Driver Center, Recovery Center & Recommendation Engine
- Criar `src/PCControlUltimate.Diagnostics/SystemDoctorService.cs`:
  - Diagnóstico imediato em modo leitura (Integridade do sistema, Armazenamento/SMART, Drivers, Inicialização, Memória, Rede, Temperaturas e Eventos Críticos do Event Viewer) classificados em `Crítico`, `Alto`, `Moderado`, `Baixo` e `Informativo`.
  - Ações de reparo assistidas com explicação e confirmação UAC (`SFC /scannow`, `DISM /Online /Cleanup-Image /CheckHealth`, `DISM /Online /Cleanup-Image /ScanHealth`, `CHKDSK` somente leitura, limpeza segura de cache).
- Criar `src/PCControlUltimate.Diagnostics/RecommendationEngine.cs`:
  - Motor de recomendações baseadas exclusivamente em evidências reais coletadas (ex.: espaço livre < 15%, delta térmico elevado, inicialização lenta, rede Wi-Fi com jitter alto, erros WHEA/EventLog), sem qualquer viés para hardware específico.
- Expandir `GameProfileManager` (detecção automática de jogos Steam, Epic Games, Xbox App e adição manual com inicialização segura) e `RecoveryCenter` (histórico unificado de otimizações, snapshots de estado, pontos de restauração e reversão individual).

### Fase 9: Integração Completa na Interface WPF e no Web Preview
- Conectar os novos serviços em `App.xaml.cs`, `MainViewModel.cs`, `MainWindow.xaml` e nas views correspondentes do Web Preview (`src/components/views/*`, `server.ts`, `src/services/solutionExporter.ts`), preservando os 17 módulos na Sidebar e o padrão de detalhes técnicos recolhidos em Expanders.

### Fase 10: Testes Automatizados Multi-Hardware (Cenários A a N)
- Criar `tests/PCControlUltimate.Tests/UniversalHardwareAndCompatibilityTests.cs` e expandir `NetworkDiagnosticsTests`:
  - Validar os **Cenários A a N** (Desktop AMD+Radeon, Intel+NVIDIA, Intel+Arc, Intel+iGPU, AMD+NVIDIA, Notebook Híbrido, Notebook Corporativo Restrito, PC sem GPU dedicada, Workstation, PC Antigo, Usuário sem Admin, PC Offline, Sensores Indisponíveis, Drivers Incompletos).
  - Validar que falhas de porta TCP (`ConnectionRefused`/`Timeout`) nunca geram perda artificial de 50% de pacotes ICMP.
- Executar verificação completa de build e lint (`compile_applet` e `lint_applet`).
