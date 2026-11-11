# PC Control Ultimate — Reconstrução Completa da Interface (17 Módulos) e Sincronização Segura

Este plano detalha a reconstrução visual e estrutural completa da interface do **PC Control Ultimate** em C# / .NET 10 WPF (tendo o Preview escuro grafite/violeta como referência visual obrigatória), alinhada com a sincronização segura de hardware (prioridade de GPU dedicada RX 7900 XTX sobre iGPU) e endurecimento de segurança do módulo **Game Network Accelerator**.

---

## User Review & Critical Decisions

> [!IMPORTANT]
> As decisões abaixo foram confirmadas na etapa de esclarecimento e guiam toda a reconstrução visual e técnica sem perda de funcionalidades reais:

- **Decisão Confirmada 1 — Paridade Total dos 17 Módulos na Sidebar (WPF e Preview)**:
  Tanto a aplicação nativa Windows (WPF) quanto o Preview exibirão todos os **17 módulos** organizados na barra de navegação lateral com ícones vetoriais, nomes claros, indicadores de módulo ativo em violeta (`#A855F7`) e suporte bilíngue (`PT-BR` / `EN-US`).
- **Decisão Confirmada 2 — Detalhes Técnicos sob Demanda (`Expander` Recolhido por Padrão)**:
  Os cartões principais de cada módulo apresentarão indicadores visuais limpos (KPIs em fonte monoespaçada tabular, barras de progresso em gradiente e badges semânticos), mantendo tabelas brutas de sensores, caminhos completos de chaves de Registro e logs de auditoria recolhidos por padrão em seções expansíveis (`Ver detalhes técnicos`).

---

## 1. Overview & Core Concept

- **O que o sistema entrega**: Uma suíte profissional de gerenciamento, telemetria física, diagnóstico de GPU (incluindo Delta Hotspot da AMD Radeon RX 7900 XTX), otimização reversível do Windows com backup DPAPI+HMAC e aceleração de rota de rede para jogos (WireGuard + ICMP/TCP), com visual comercial de alto padrão inspirado no Preview.
- **Público-Alvo**: Entusiastas de hardware, jogadores competitivos e engenheiros de sistemas Windows 10/11 x64 que exigem leitura física fidedigna (sem métricas inventadas) combinada com uma experiência visual moderna, responsiva e intuitiva.
- **Valor Principal**: Eliminar a aparência de tabelas brutas e formulários improvisados no WPF, substituindo-os por cartões modernos em tema escuro grafite (`#07090E` / `#0F121B`), destaques em roxo/violeta (`#A855F7` / `#7C3AED`), gráficos vetoriais limpos e controles de ação com feedback imediato.

---

## 2. User Experience & Visual Design

### Fluxos Principais do Usuário
1. **Navegação Unificada nos 17 Módulos**:
   - Na barra lateral esquerda (`Sidebar`), o usuário alterna instantaneamente entre os 17 módulos:
     1. *Dashboard*
     2. *Hardware Monitor*
     3. *GPU Health Lab*
     4. *Boost Center*
     5. *Game Center*
     6. *Performance Lab*
     7. *Benchmark Center*
     8. *Diagnostics*
     9. *Windows Tweaks*
     10. *Debloat & Cleaner*
     11. *Network Lab*
     12. *Game Network Accelerator*
     13. *Peripheral Test*
     14. *Driver Center*
     15. *Software Hub*
     16. *Restore Center*
     17. *AI Assistant*
2. **Leitura de Sensores e Prioridade de GPU Dedicada**:
   - Em sistemas com gráficos integrados (iGPU no processador) e placa de vídeo dedicada (ex.: *ASRock Radeon RX 7900 XTX Taichi White 24G OC*), o coletor prioriza automaticamente a GPU discreta para alimentar os cartões principais do *Dashboard*, *Hardware Monitor* e *GPU Health Lab* (temperatura Core, Hotspot, Delta $\Delta T$, clock, potência e VRAM), mantendo a iGPU visível nos sensores detalhados.
   - Sensores indisponíveis exibem claramente `"Não disponível"` / `"N/D"` sem inventar números falsos.
3. **Inspeção Técnica Sob Demanda**:
   - Cada módulo apresenta primeiro os cartões executivos de status, métricas e botões de ação; ao clicar em **Ver detalhes técnicos**, um painel expansível revela os sensores brutos, caminhos de Registro (`HKCU`/`HKLM`), snapshots ou saltos de traceroute.

### Identidade Visual e Tema
- **Direção Estética**: Utilitário gamer de engenharia de precisão — superfície grafite profunda, bordas sutis de 1px, hierarquia tipográfica clara e acentos luminosos contidos.
- **Paleta de Cores (Distribuição 60-30-10)**:
  - **60% Canvas Dominante**: Grafite profundo `#07090E` (fundo principal) e `#0A0C13` (barra lateral e cabeçalho).
  - **30% Superfícies Estruturais**: Cartões em `#0F121B` e `#131722` com bordas `#1E2436` e `#2E1065` em cartões destacados.
  - **10% Orçamento de Acento e Semântica**:
    - Roxo/Violeta Primário: `#A855F7` / `#9333EA` (módulo ativo, botões primários, barras de progresso de CPU/sistema).
    - Verde Esmeralda: `#10B981` / `#22C55E` (saúde excelente, rota direta ou túnel verificado com handshake).
    - Âmbar: `#F59E0B` (atenção térmica, modo não elevado ou aviso de rota).
    - Rosa/Vermelho: `#F43F5E` / `#EF4444` (alerta crítico de Hotspot $\Delta T \ge 25^\circ\text{C}$, erro ou bloqueio fail-closed).
- **Tipografia e Hierarquia**:
  - Títulos de seção e cabeçalhos com peso SemiBold/Black e alto contraste (`#FFFFFF`).
  - Rótulos secundários e descrições em cinza ardósia (`#94A3B8`).
  - Valores numéricos, temperaturas, latências (ms), clocks (MHz) e contadores em fonte monoespaçada tabular (`JetBrains Mono` / `Consolas`) para alinhamento vertical perfeito sem oscilação visual.
- **Responsividade Multi-Resolução**:
  - Layout fluido adaptável a `1920x1080` (Full HD), `2560x1440` (Quad HD) e `3840x2160` (4K) usando `Grid` proporcional, `UniformGrid` para fileiras de KPIs e `ScrollViewer` vertical suave sem cortes de texto ou sobreposição.

---

## 3. Key Product Decisions & Trade-Offs

- **Decisão 1 — Reconstrução Nativa em XAML + MVVM (Sem WebView)**
  - *Abordagem Escolhida*: Reconstruir os templates, estilos, ícones vetoriais (`Path` Geometry), barras de progresso e cartões diretamente em WPF XAML nativo acoplado aos `ViewModels` dos 17 módulos, mantendo também o Preview Web sincronizado com os mesmos 17 módulos.
  - *Por quê*: Garante latência de interface próxima de zero, integração direta com as APIs do Windows/UAC/LibreHardwareMonitor e fidelidade visual ao Preview sem o consumo de memória de um navegador embutido.
- **Decisão 2 — Priorização Determinística de GPU Dedicada sobre iGPU**
  - *Abordagem Escolhida*: Ordenar e selecionar dispositivos gráficos em `LibreHardwareMonitorWrapper` priorizando GPUs discretas (identificando modelos dedicados Radeon RX / GeForce RTX / Arc discreta ou maior VRAM/sensores de Hotspot) sobre GPUs integradas (`Radeon(TM) Graphics`, `Intel(R) UHD/Iris`).
  - *Por quê*: Em processadores modernos (ex.: Ryzen 7000/9000 com iGPU RDNA2), a enumeração PCIe pode listar a iGPU antes da RX 7900 XTX, o que ocultaria o Hotspot e o consumo real da placa de vídeo principal no Dashboard e no GPU Health Lab.
- **Decisão 3 — Endurecimento Fail-Closed do Túnel WireGuard e Verificação Real de Rotas**
  - *Abordagem Escolhida*: Proteger o diretório e o arquivo `.conf` temporário com ACLs restritas ao usuário atual (`TryRestrictDirectoryAclToCurrentUser` / `TryRestrictFileAclToCurrentUser`), remover o arquivo de configuração em texto puro do disco logo após a instalação do serviço e verificar efetivamente o estado do serviço e da tabela de rotas antes de reportar `RouteTableRestored = true`.
  - *Por quê*: Evita exposição da `PrivateKey` em `%LOCALAPPDATA%` e garante que o aplicativo nunca declare restauração de rotas sem comprovação real no sistema operacional.

---

## 4. Technical Architecture & Data Strategy

### Arquitetura de Componentes e Fluxo de Dados

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│                         PC CONTROL ULTIMATE (WPF & PREVIEW)                          │
├──────────────────────────┬───────────────────────────────────────────────────────────┤
│   SIDEBAR (17 Módulos)   │              WORKSPACE CONTENT AREA                       │
│  ┌────────────────────┐  │  ┌─────────────────────────────────────────────────────┐  │
│  │ • Dashboard        │  │  │ Contextual Header Bar (Status, Score, Boost, Lang)  │  │
│  │ • Hardware Monitor │  │  └──────────────────────────┬──────────────────────────┘  │
│  │ • GPU Health Lab   │  │                             ▼                             │
│  │ • Boost Center     │  │  ┌─────────────────────────────────────────────────────┐  │
│  │ • Game Center      │  │  │ Primary KPI Cards Grid (4-Col UniformGrid / Bento)  │  │
│  │ • Performance Lab  │  │  │  • Tabular Metrics • Gradient Progress • Badges     │  │
│  │ • Benchmark Center │  │  └──────────────────────────┬──────────────────────────┘  │
│  │ • Diagnostics      │  │                             ▼                             │
│  │ • Windows Tweaks   │  │  ┌─────────────────────────────────────────────────────┐  │
│  │ • Debloat & Cleaner│  │  │ Interactive Module Controls & Visual Telemetry      │  │
│  │ • Network Lab      │  │  │  • Real-Time Polyline Chart • Safe Action Buttons   │  │
│  │ • Game Net Accel   │  │  └──────────────────────────┬──────────────────────────┘  │
│  │ • Peripheral Test  │  │                             ▼                             │
│  │ • Driver Center    │  │  ┌─────────────────────────────────────────────────────┐  │
│  │ • Software Hub     │  │  │ Expandable Technical Details (Collapsed by Default) │  │
│  │ • Restore Center   │  │  │  • Raw Sensors • Registry Paths • Audit & Route Hops│  │
│  │ • AI Assistant     │  │  └─────────────────────────────────────────────────────┘  │
│  └─────────┬──────────┘  └─────────────────────────────▲─────────────────────────────┘
└────────────┼───────────────────────────────────────────┼─────────────────────────────┘
             │                                           │
             ▼                                           │
┌────────────────────────────────────────────────────────┴─────────────────────────────┐
│                    MVVM VIEWMODELS & CORE SERVICES LAYER (.NET 10)                   │
│  ┌─────────────────────────┐  ┌─────────────────────────┐  ┌──────────────────────┐  │
│  │ LibreHardwareMonitor    │  │ RegistryOptimizer &     │  │ GameNetworkAccel &   │  │
│  │ (Dedicated GPU Priority │  │ DPAPI+HMAC State Ledger │  │ WireGuardTunnelMgr   │  │
│  │  RX 7900 XTX > iGPU)    │  │ (ErrorCode + Snapshots) │  │ (ACL + Route Verify) │  │
│  └─────────────────────────┘  └─────────────────────────┘  └──────────────────────┘  │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

### Mapeamento Interativo dos 17 Módulos e Estado
- **Dashboard**: Banner executivo com anel de pontuação de saúde do sistema, 4 cartões de hardware principais (CPU, GPU dedicada com Core/Hotspot $\Delta T$, RAM e NVMe) com barras de progresso em gradiente, gráfico histórico em tempo real (CPU/GPU/Temp), painel de otimização rápida com backup obrigatório e resumo de sensores da placa-mãe.
- **Hardware Monitor & GPU Health Lab**: Cartões separados por categoria (CPU, GPU, Memória, Placa-Mãe/Fans, Armazenamento) e diagnóstico térmico dedicado da RX 7900 XTX (Core vs. Hotspot, alerta de $\Delta T \ge 25^\circ\text{C}$, clock, potência e VRAM), com lista completa de sensores recolhida em `Expander`.
- **Boost Center, Windows Tweaks & Debloat & Cleaner**: Cartões de otimização com nível de risco semântico (`Seguro`, `Moderado`, `Avançado`), impacto esperado, criação automática de snapshot antes da aplicação e botão de reversão individual ou global, mantendo os caminhos técnicos de Registro (`HKCU`/`HKLM`) em seção expansível.
- **Game Center, Performance Lab & Benchmark Center**: Seleção de processo/jogo, telemetria de FPS / Frame Time / 1% Low via PresentMon ETW, perfis de energia e comparativo de estabilidade.
- **Network Lab & Game Network Accelerator**: Medição real de latência ICMP/TCP, jitter, perda de pacotes, detecção de conexões de jogos por PID, avaliação fail-closed de rota (bloqueando ativação se a rota direta for melhor ou igual), auditoria de configuração WireGuard (bloqueando `0.0.0.0/0` e `::/0` por padrão) e proteção de chave privada com ACL restrita.
- **Diagnostics, Peripheral Test, Driver Center, Software Hub, Restore Center & AI Assistant**: Cada módulo recebe seu `ViewModel`, `UserControl` e `DataTemplate` dedicado no WPF com cartões no padrão grafite/violeta, além da sincronização do item `Game Network Accelerator` na Sidebar do Preview para paridade 1:1 dos 17 módulos.
