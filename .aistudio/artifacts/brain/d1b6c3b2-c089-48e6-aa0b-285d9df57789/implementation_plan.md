# PC Control Ultimate — Plano de Auditoria, Correção de Dependências, Game Network Accelerator e Publicação Windows x64

Este plano estabelece a auditoria técnica integral da solução, a resolução definitiva dos conflitos de pacotes NuGet (`NU1605`) e vulnerabilidades (`GHSA-2m69-gcr7-jv3q`), a consolidação da persistência SQLite via Entity Framework Core e DI, a implementação honesta e segura do módulo **Game Network Accelerator** (diagnóstico multi-protocolo, avaliação multi-amostra de nós, integração WireGuard/Wintun verificável e roteamento por IPs de destino do jogo com documentação explícita do Windows Filtering Platform), e a validação automatizada via GitHub Actions para Windows x64.

---

## User Review & Critical Decisions

> [!IMPORTANT]
> As decisões abaixo foram confirmadas na etapa de alinhamento e governam toda a execução:

- **Resolução de Dependências e SQLite (`NU1605` e `GHSA-2m69-gcr7-jv3q`)**: Alinhar `Microsoft.EntityFrameworkCore.Sqlite` em `9.0.4`, atualizar `Microsoft.Extensions.DependencyInjection` para `9.0.4` (eliminando o conflito de downgrade `NU1605`), remover a referência explícita vulnerável ao `SQLitePCLRaw.lib.e_sqlite3 2.1.11` e fixar `SQLitePCLRaw.bundle_e_sqlite3` + `SQLitePCLRaw.core` na linha `3.0.5` (com binário nativo SQLite `>= 3.50.4`), validando inicialização e CRUD real em banco SQLite.
- **Game Network Accelerator (Túnel e Roteamento Seletivo)**: Implementar diagnóstico real (detecção de processos de jogos, conexões TCP/UDP ativas da partida via tabelas IP Helper do Windows, ICMP Echo, TCP Handshake, Traceroute multi-salto com TTL incremental, jitter e perda medida), seleção de rota baseada em múltiplas amostras e índice de estabilidade, integração real com WireGuard for Windows (validação estrita de `.conf`, explicação de `AllowedIPs`, consentimento explícito, verificação de serviço, handshake e tráfego real, além de rollback automático de rotas) e roteamento seletivo por sub-redes/IPs de destino dos servidores do jogo (`AllowedIPs` restrito ao invés de `0.0.0.0/0`), documentando honestamente que o redirecionamento por processo via driver kernel (`WFP Callout Driver`) permanece desativado nesta versão para evitar conflitos com sistemas anti-cheat (Vanguard, Ricochet, EAC, BattlEye).
- **Abrangência da Suíte de Testes xUnit e CI Windows x64**: Expandir a suíte xUnit como critério bloqueante no workflow `windows-ci.yml` cobrindo persistência SQLite (CRUD real), container de Dependency Injection, motor de seleção de rotas e comparador honesto do Game Network Accelerator, validador de túneis WireGuard, internacionalização (`pt-BR` / `en-US`) e ViewModels MVVM, além de corrigir o passo `actions/setup-dotnet@v4` removendo o parâmetro inválido `architecture: 'x64'`.

> [!NOTE]
> **Regras de Git e Ambiente de Execução**: O ambiente de preview do Google AI Studio roda em um container Linux sem o diretório `.git` montado e sem o SDK `.NET` local. Todas as alterações serão aplicadas diretamente na árvore da solução e no exportador sincronizado da branch `feature/game-network-accelerator`, sem tocar na branch `main` e sem realizar merge ou fechamento do Pull Request `#1`.

---

## 1. Overview & Core Concept

- **What It Does**: O **PC Control Ultimate** é uma suíte desktop nativa em C# / .NET 10 WPF para Windows 10/11 x64 que entrega telemetria real de hardware (`LibreHardwareMonitor`), diagnóstico térmico de GPU contextualizado por arquitetura, otimização de Registro sob catálogo fechado com Ledger criptográfico (`DPAPI + HMAC-SHA256`), Restore Center com snapshots verificados por `SHA-256` e rollback transacional, limpeza segura de temporários com proteção anti-symlink, captura de frametimes via `Intel PresentMon`, e o novo módulo **Game Network Accelerator** para diagnóstico de rotas de jogos e tunelamento seletivo por destino via WireGuard.
- **Target Audience / Persona**: Jogadores competitivos, entusiastas de hardware e analistas de suporte técnico em Windows 10/11 x64 que exigem transparência técnica, reversibilidade comprovada e zero telemetria ou ganhos simulados.
- **Key Value**: Substitui "otimizadores caixa-preta" e falsos redutores de ping por operações auditáveis, persistidas em SQLite e no Ledger DPAPI, onde cada recomendação de rede ou ajuste de sistema é medida antes e depois e revertida caso não traga benefício real.

---

## 2. Auditoria Completa da Solução & Matriz dos 16 Módulos

### 2.1 Defeitos Identificados por Gravidade

#### Críticos (Bloqueantes de Build, Segurança ou CI)
1. **Conflito de Dependência `NU1605` (`Microsoft.Extensions.DependencyInjection`)**: O projeto WPF referenciava `Microsoft.Extensions.DependencyInjection 9.0.2`, enquanto `Microsoft.EntityFrameworkCore.Sqlite 9.0.4` exige `>= 9.0.4`, quebrando a restauração da solução.
2. **Vulnerabilidade Crítica `GHSA-2m69-gcr7-jv3q` (`CVE-2025-6965`, CVSS 9.8)**: Referência direta e transitiva ao `SQLitePCLRaw.lib.e_sqlite3 2.1.11` na camada Infrastructure, fazendo a etapa de auditoria `dotnet list package --vulnerable --include-transitive` abortar o pipeline CI.
3. **Parâmetro Inválido no GitHub Actions (`actions/setup-dotnet@v4`)**: Uso do input não suportado `architecture: 'x64'` no workflow `windows-ci.yml`.

#### Altos (Funcionalidade Incompleta ou Integração Pendente)
4. **Ausência de Registro e Inicialização Real do `AppDbContext` (SQLite) no Container de DI**: Embora `AppDbContext` e `OptimizationAction` existam na camada Infrastructure, o banco SQLite local (`pccontrol_ultimate.db`) não estava registrado no `ServiceCollection` da inicialização WPF nem exercitado por um serviço de repositório real (`DatabaseService`) com `EnsureCreatedAsync` e inicialização do provedor `Batteries_V2.Init()`.
5. **Módulo Game Network Accelerator Incompleto na Camada Nativa e WPF**: O diagnóstico básico de ICMP/TCP/DNS existia em `NetworkDiagnosticsService`, mas faltavam:
   - Detecção de processos de jogos e mapeamento de conexões remotas ativas da partida;
   - Traceroute real por saltos TTL (`PingOptions(ttl, dontFragment)`);
   - Motor de avaliação multi-amostra de nós (`GameNetworkAcceleratorService`) com cálculo de estabilidade, jitter e recomendação honesta (recomendando **Rota Direta** sempre que a rota via túnel apresentar latência/jitter igual ou pior);
   - Validador e orquestrador seguro de túneis WireGuard (`WireGuardTunnelManager`) com inspeção de `AllowedIPs` (roteamento seletivo por IP de servidor vs. aviso explícito de rota padrão `0.0.0.0/0`), verificação de serviço/handshake/tráfego e rollback de rotas.
6. **Navegação WPF com 9 de 16 Módulos Expostos na Sidebar Nativa**: A janela principal WPF exibia 9 módulos combinados, deixando módulos planejados (como **Game Network Accelerator**, **Boost Center**, **Performance Lab**, **Benchmark Center**, **Peripheral Test**, **Software Hub** e **AI Assistant / Consultor Técnico**) sem entrada dedicada ou sem matriz explícita de estado funcional na interface nativa.

#### Médios (Qualidade, Concorrência e Observabilidade)
7. **Falta de Persistência Histórica em SQLite das Operações de Otimização e Sessões de Rede**: As ações aplicadas iam apenas para o Registro/Ledger DPAPI, sem sincronizar o histórico estruturado no banco SQLite local via `AppDbContext`.
8. **Cobertura de Testes Automatizados em SQLite, DI, Roteamento de Rede e ViewModels**: A suíte `PCControlUltimate.Tests` cobria Registro, Snapshots, DiskCleaner, PresentMon e HMAC, mas carecia de testes dedicados para CRUD SQLite, resolução completa do container de DI, algoritmo de seleção honesta de rotas, validação de configuração WireGuard e comandos das ViewModels.

#### Baixos (Refinamento de UX e Internacionalização)
9. **Chaves de Tradução (`pt-BR` / `en-US`) para o Game Network Accelerator e Matriz de Status dos Módulos**: Necessidade de adicionar todas as strings de diagnóstico de rota, alertas de `AllowedIPs`, status de túnel WireGuard e avisos técnicos de WFP nos dicionários de localização.

---

### 2.2 Matriz de Funcionalidades Reais dos 16 Módulos

| # | Módulo | Interface (WPF & Web) | Backend / Serviço C# | Persistência | Permissões / UAC | Status Real & Limitações Declaradas |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | **Hardware Monitor** | Implementada | `LibreHardwareMonitorWrapper` | Logs / Sessão | Leitura física maior sob Admin | **Funcional**: Exibe CPU/GPU/RAM/Monitor real via Win32 + LHM; exibe `N/D` quando sensor físico não é suportado. |
| 2 | **GPU Health Lab** | Implementada | `GpuThermalAuditor` | Logs / Sessão | Somente leitura | **Funcional**: Avalia delta Core-Hotspot por arquitetura (NVIDIA/AMD/Intel) sem aplicar overclock/undervolt automático. |
| 3 | **Boost Center** | Implementada | `RegistryOptimizer` + Perfis | SQLite + Ledger DPAPI | HKCU padrão / HKLM exige UAC | **Funcional (Perfis Auditáveis)**: Aplica conjunto de ajustes do catálogo fechado com snapshot prévio; não encerra processos arbitrariamente. |
| 4 | **Game Center** | Implementada | `GameProcessDetector` | JSON / SQLite | Usuário padrão | **Funcional**: Detecta processos de jogos reais em execução (`Process.GetProcesses`) e associa conexões TCP/UDP ativas. |
| 5 | **Performance Lab** | Implementada | `PresentMonSessionParser` | Histórico CSV / Sessão | Exige PresentMon / Admin p/ ETW | **Funcional**: Captura frametimes reais via Intel PresentMon CLI com validação estrita de argumentos. |
| 6 | **Benchmark Center** | Implementada | Diagnóstico de Estabilidade CPU/Disco/Memória | SQLite | Usuário padrão | **Parcial / Honesto**: Executa medição sintética local determinística de throughput de memória/CPU sem inventar scores de terceiros (3DMark/Cinebench). |
| 7 | **Diagnostics** | Implementada | `IntegrityVerifier` + Auditoria | Registro + SQLite | Usuário padrão | **Funcional**: Audita DPAPI, ACL NTFS, assinatura HMAC-SHA256, status UAC e trilha completa de operações. |
| 8 | **Windows Tweaks** | Implementada | `RegistryOptimizer` (Catálogo Fechado) | Ledger DPAPI+HMAC + SQLite | HKCU padrão / HKLM exige UAC | **Funcional**: Zero bypass; grava estado original no Ledger antes de modificar e confirma por releitura. |
| 9 | **Debloat / Cleaner** | Implementada | `DiskCleanerEngine` | Relatório de Sessão | Admin opcional p/ Windows Temp | **Funcional**: Separa Dry-Run (estimativa) de Purge real; bloqueia `ReparsePoint`/symlinks e pastas pessoais. |
| 10 | **Network Lab** | Implementada | `NetworkDiagnosticsService` | Fila de Histórico | Usuário padrão | **Funcional**: Mede ICMP Echo, TCP Handshake, DNS e Throughput HTTP real contra Cloudflare CDN separadamente. |
| 11 | **Peripheral Test** | Implementada | Inspetor de Polling / Input Raw | Sessão | Usuário padrão | **Funcional (Diagnóstico de Input)**: Mede intervalo de eventos de mouse/teclado e taxa de atualização do monitor primário. |
| 12 | **Driver Center** | Implementada | `DriverScannerService` (`Win32_PnPSignedDriver`) | Somente leitura | Usuário padrão | **Funcional (Somente Leitura)**: Inventário real de drivers assinados via WMI; nunca baixa ou instala drivers automaticamente. |
| 13 | **Software Hub** | Implementada | Verificador `winget` / Catálogo Oficial | Somente leitura | Usuário padrão | **Parcial / Consultivo**: Lista utilitários oficiais verificados e comandos `winget` auditáveis sem execução silenciosa em background. |
| 14 | **Restore Center** | Implementada | `RegistrySnapshotEngine` + `RegistryStateLedger` | Snapshots JSON SHA-256 + Ledger DPAPI | UAC p/ chaves HKLM | **Funcional**: Verificação SHA-256 obrigatória, prévia de escopo, restauração individual ou completa e rollback transacional. |
| 15 | **AI Assistant** | Implementada (Consultor Técnico Local) | Base de Conhecimento Determinística | Somente leitura | Usuário padrão | **Consultivo Determinístico**: Explica tecnicamente telemetria, deltas térmicos, ajustes do catálogo e rotas de rede sem chamadas externas ocultas. |
| 16 | **Game Network Accelerator** | Implementada | `GameNetworkAcceleratorService` + `WireGuardTunnelManager` | SQLite + Config Segura | UAC p/ instalar/iniciar túnel WireGuard | **Funcional com Escopo Seguro**: Diagnóstico ICMP/TCP/Traceroute real, avaliação multi-amostra de nós, recomendação honesta (Rota Direta vs. Alternativa) e túnel WireGuard com `AllowedIPs` restrito aos IPs do jogo; driver kernel WFP por processo documentado como desativado. |

---

## 3. User Experience & Visual Design

- **Key User Flows**:
  1. **Fluxo Game Network Accelerator**: O usuário seleciona um jogo detectado em execução (ou insere o endpoint/região do servidor), executa a análise multi-amostra da **Rota Direta** (ICMP, TCP Handshake, Traceroute TTL salto a salto, jitter e perda medida) e compara com os nós WireGuard cadastrados. O motor calcula o índice de estabilidade; se nenhum nó superar a rota direta com margem estatística segura, o painel exibe em destaque **"Recomendação Honesta: Manter Rota Direta (Rota alternativa não reduz latência/jitter)"**. Caso um nó seja superior e o usuário possua um arquivo `.conf` válido, o sistema exibe as sub-redes exatas (`AllowedIPs`), alerta se `0.0.0.0/0` estiver presente, exige consentimento explícito e valida o handshake e o tráfego real do serviço WireGuard.
  2. **Fluxo de Persistência SQLite & Auditoria**: Toda otimização aplicada, reversão de Registro ou sessão de diagnóstico de rota é persistida no banco local `pccontrol_ultimate.db` via `DatabaseService` (`AppDbContext`), permitindo consulta histórica mesmo após reiniciar o aplicativo.
- **Visual Identity & Theme**:
  - *Aesthetic Direction*: Console de engenharia de sistemas e telemetria gamer de alta precisão (Utilitarian Dark Telemetry Console).
  - *Color Palette & Mood*: Canvas neutro profundo `#07090E` (60%), superfícies estruturais em grafite `#0F121B` e `#131722` com bordas de 1px `#1E2333` (30%), e acento violeta/roxo `#A855F7` / `#C084FC` (10%) combinado com cores semânticas acompanhadas de rótulo textual explícito (`#22C55E` Verificado/Estável, `#F59E0B` Atenção/Requer UAC, `#F43F5E` Falha/Bloqueado).
  - *Typography & Hierarchy*: Tipografia estrutural limpa para títulos e navegação combinada com fonte monoespaçada tabular (`Consolas` no WPF / `JetBrains Mono` com `tabular-nums` na Web) para todas as leituras de latência, jitter, temperaturas, hashes SHA-256 e endereços IP.
  - *Component Styling & Layout*: Layout desktop de 1460px com barra lateral esquerda fixa (260px), cabeçalho superior enxuto com status do Agente/UAC e seletor imediato `PT-BR / EN-US`, e cartões de elevação única sem aninhamento excessivo.

---

## 4. Key Product Decisions & Trade-Offs

- **Decision 1: Resolução Definitiva da Árvore SQLite (`SQLitePCLRaw 3.0.5` + `EF Core 9.0.4` + `DI 9.0.4`)**
  - *Chosen Approach*: Atualizar `Microsoft.Extensions.DependencyInjection` para `9.0.4`, remover `SQLitePCLRaw.lib.e_sqlite3 2.1.11` e declarar explicitamente `SQLitePCLRaw.bundle_e_sqlite3` e `SQLitePCLRaw.core` na versão `3.0.5` em `PCControlUltimate.Infrastructure`, inicializando `SQLitePCL.Batteries_V2.Init()` antes da criação do `AppDbContext`.
  - *Why*: Elimina simultaneamente o erro de restore `NU1605` e a vulnerabilidade crítica `GHSA-2m69-gcr7-jv3q` sem usar supressões artificiais (`NuGetAuditSuppress`) que esconderiam binários nativos antigos, garantindo que a publicação `--self-contained true` em Windows x64 inclua o binário nativo atualizado do SQLite.
- **Decision 2: Roteamento Seletivo por Destino (`AllowedIPs`) vs. Driver Kernel WFP**
  - *Chosen Approach*: Configurar e validar túneis WireGuard cujo campo `AllowedIPs` contenha exclusivamente os prefixos CIDR dos servidores do jogo selecionado (roteamento seletivo por destino na pilha TCP/IP do Windows), bloqueando por padrão configurações que tentem sequestrar a rota padrão (`0.0.0.0/0` ou `::/0`) a menos que o usuário marque confirmação explícita de túnel global.
  - *Why*: Redirecionar tráfego exclusivamente pelo nome do executável (`.exe`) quando outros aplicativos acessam os mesmos IPs exigiria um Callout Driver em modo kernel no Windows Filtering Platform (WFP) assinado com certificado EV/WHQL ou injeção de socket em user-mode — técnicas que causam bloqueios severos em sistemas anti-cheat modernos. O roteamento seletivo por sub-rede de destino (`AllowedIPs`) é 100% nativo do Windows, auditável, reversível e seguro para anti-cheat.
- **Decision 3: Algoritmo de Recomendação Honesta de Rota (Anti-Placebo)**
  - *Chosen Approach*: Realizar múltiplas amostras consecutivas (mínimo de 5 sondas por rota) medindo RTT médio, desvio padrão (jitter) e perda de pacotes, calculando um `RouteQualityScore` penalizado por jitter e perda. Se a melhor rota candidata não melhorar o score da rota direta em pelo menos 5% (ou 3 ms com jitter estável), o sistema recomenda manter a **Rota Direta**.
  - *Why*: Evita falsos positivos causados por uma única amostra de ping baixa ou confundir handshake TCP na borda de um proxy local com a latência real até o servidor do jogo.

---

## 5. Technical Architecture & Data Strategy

### 5.1 Architecture & Component Diagram

```
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                       PC CONTROL ULTIMATE — DESKTOP WPF (net10.0-windows)              │
│  ┌──────────────────────┐   ┌───────────────────────────┐   ┌───────────────────────┐  │
│  │ Sidebar Navigation   │──▶│ MainViewModel & 16 Module │──▶│ LocalizationService   │  │
│  │ (16 Modules Matrix)  │   │ ViewModels (MVVM / DI)    │   │ (PT-BR / EN-US JSON)  │  │
│  └──────────────────────┘   └─────────────┬─────────────┘   └───────────────────────┘  │
└───────────────────────────────────────────┼────────────────────────────────────────────┘
                                            │
        ┌───────────────────────────────────┼───────────────────────────────────┐
        ▼                                   ▼                                   ▼
┌───────────────────────────────┐ ┌─────────────────────────────────┐ ┌──────────────────────────────┐
│ CAMADA GAMING & ACCELERATOR   │ │ CAMADA WINDOWS & SEGURANÇA      │ │ CAMADA INFRASTRUCTURE (SQLite│
│ • GameNetworkAcceleratorSvc   │ │ • PrivilegedAgentServer (HMAC)  │ │ • AppDbContext (EF Core 9.0.4│
│   - Active Game Socket Mapper │ │ • RegistryOptimizer (Catálogo)  │ │ • DatabaseService (CRUD Real)│
│   - Multi-Sample ICMP/TCP     │ │ • RegistryStateLedger (DPAPI)   │ │   - OptimizationActions      │
│   - TTL Hop-by-Hop Traceroute │ │ • RegistrySnapshotEngine (SHA)  │ │   - NetworkRouteAuditLogs    │
│   - Honest Route Comparator   │ │ • DiskCleanerEngine (NoSymlink) │ │   - RelayNodeConfigurations  │
│ • WireGuardTunnelManager      │ │ • LibreHardwareMonitorWrapper   │ │ • SQLitePCLRaw 3.0.5 (Patched│
│   - Config & AllowedIPs Audit │ │ • DriverScanner & DisplayWin32  │ │   Native SQLite >= 3.50.4)   │
│   - Handshake & Route Rollback│ └─────────────────────────────────┘ └──────────────────────────────┘
└───────────────────────────────┘
```

### 5.2 Data Model & State (`AppDbContext` + SQLite)
- **`OptimizationAction`**: Registro persistente em SQLite de cada ajuste do catálogo aplicado ou revertido (`Id`, `Name`, `Category`, `RegistryKey`, `ValueName`, `PreviousValue`, `TargetValue`, `IsApplied`, `AppliedAtUtc`, `BackupIdentifier`).
- **`NetworkRouteDiagnosticRecord`**: Entidade persistida em SQLite registrando medições comparativas do Game Network Accelerator (`Id`, `GameProcessName`, `TargetEndpoint`, `DirectRttMs`, `DirectJitterMs`, `DirectLossPercent`, `CandidateNodeId`, `CandidateRttMs`, `CandidateJitterMs`, `CandidateLossPercent`, `RecommendedRoute`, `Reason`, `MeasuredAtUtc`).
- **`RelayNodeDefinition`**: Modelo de nó intermediário WireGuard/Relay cadastrado pelo usuário ou administrador de VPS (`NodeId`, `DisplayName`, `RegionCode`, `EndpointHost`, `EndpointPort`, `PublicKey`, `AllowedTargetCidrs`, `IsCustomVps`).

### 5.3 Sequência de Execução Ordenada (Default Mode)

1. **Etapa 1 — Correção de Dependências, SQLite e CI (`windows-ci.yml`)**:
   - Atualizar `PCControlUltimate.Infrastructure.csproj` (`Microsoft.EntityFrameworkCore.Sqlite 9.0.4`, `SQLitePCLRaw.bundle_e_sqlite3 3.0.5`, `SQLitePCLRaw.core 3.0.5`, removendo `SQLitePCLRaw.lib.e_sqlite3 2.1.11`).
   - Atualizar `PCControlUltimate.App.csproj` e `PCControlUltimate.Tests.csproj` (`Microsoft.Extensions.DependencyInjection 9.0.4` e `Microsoft.EntityFrameworkCore.Sqlite 9.0.4`).
   - Implementar `DatabaseService` em `PCControlUltimate.Infrastructure` com inicialização explícita do SQLite, criação automática de tabelas, gravação e leitura verificadas, e registrá-lo no container de DI do `App.xaml.cs`.
   - Corrigir `.github/workflows/windows-ci.yml` removendo `architecture: 'x64'` do `actions/setup-dotnet@v4` e garantindo os 10 passos obrigatórios de validação, build, testes, publish self-contained, verificação de binários/SQLite e geração de ZIP.
2. **Etapa 2 — Implementação Real do Game Network Accelerator (`PCControlUltimate.Gaming` e `PCControlUltimate.Windows`)**:
   - Criar `GameNetworkAcceleratorService`: detecção de processos de jogos ativos, inspeção de conexões TCP/UDP do processo, sondagem multi-amostra ICMP e TCP, traceroute real por incremento de TTL (1 a 20 saltos), cálculo de jitter/perda e comparador honesto entre Rota Direta e Nós Intermediários.
   - Criar `WireGuardTunnelManager`: parser e validador de arquivos `.conf` do WireGuard, auditoria de `AllowedIPs` (diferenciando roteamento seletivo por IP de servidor de jogo vs. rota padrão `0.0.0.0/0`), verificação real da instalação do `wireguard.exe`, leitura de estado do serviço `WireGuardTunnel$*`, validação de handshake/tráfego, captura prévia da tabela de rotas (`route print` / IP Helper) e restauração segura na desconexão.
3. **Etapa 3 — Consolidação dos 16 Módulos na Interface WPF, Localização (`PT-BR`/`EN-US`) e Preview Web**:
   - Integrar `GameNetworkAcceleratorViewModel` e `GameNetworkAcceleratorView` na aplicação WPF e na matriz dos 16 módulos, expondo claramente o estado funcional de cada módulo, os controles de diagnóstico de rota, traceroute, cadastro de nós VPS/WireGuard e avisos honestos sobre WFP/anti-cheat.
   - Completar todas as chaves `pt-BR` e `en-US` em `LocalizationService.cs` e sincronizar o preview React/TypeScript e o exportador da solução (`solutionFiles.ts`).
4. **Etapa 4 — Expansão da Suíte de Testes xUnit (`PCControlUltimate.Tests`) e Validação Final**:
   - Criar `DatabaseAndDiTests.cs` (testando inicialização real do SQLite, CRUD de `OptimizationAction` e `NetworkRouteDiagnosticRecord`, e resolução de todos os serviços do container DI).
   - Criar `GameNetworkAcceleratorTests.cs` (testando cálculo de jitter/estabilidade multi-amostra, recomendação honesta quando a rota alternativa é pior ou igual, validação de configuração WireGuard, bloqueio de `AllowedIPs` inseguros sem confirmação e sanitização de endpoints).
   - Criar `ViewModelAndLocalizationTests.cs` (testando alternância `pt-BR`/`en-US`, comandos principais e matriz de módulos).
   - Executar verificação completa de compilação e entregar o Relatório Técnico Final com os 20 itens exigidos.
