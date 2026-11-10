# PC Control Ultimate — Fase de Correção Crítica e Consolidação

Este plano estabelece a consolidação técnica, correção de assinaturas de compilação, blindagem de segurança do agente privilegiado, preservação integral de tipos de Registro, remoção de telemetria simulada e validação automatizada da fundação Windows nativa (.NET 10 WPF) e da interface integrada.

## User Review & Critical Decisions

> [!IMPORTANT]
> Como as perguntas de clarificação iniciais foram puladas, adotamos as opções recomendadas de máxima segurança e autenticidade técnica para garantir conformidade imediata com todos os requisitos da auditoria.

- **Entrega Segura do Token de Sessão (Recomendado)**: Proteção via DPAPI (`ProtectedData` no escopo `CurrentUser`) combinada com ACL restrita ao usuário/administrador local e validação de processo/handshake local autenticado, eliminando a exposição aberta do token em `/api/auth/token`.
- **Telemetria Sem Dados Simulados (Recomendado)**: Remoção total de valores fictícios de demonstração quando o agente Windows estiver desconectado; todos os sensores sem leitura real exibirão explicitamente `"Não disponível"` (PT-BR) / `"Unavailable"` (EN-US).
- **Configuração e Compatibilidade .NET 10 (Recomendado)**: Padronização da solução Visual Studio para `net10.0-windows` / `net10.0` em plataforma `x64`, eliminando referências a ícones ou recursos inexistentes e alinhando todas as assinaturas entre projetos e testes.

---

## 1. Overview & Core Concept

- **What It Does**: Plataforma desktop profissional para Windows 10 e Windows 11 dedicada à otimização segura de jogos, diagnóstico térmico, limpeza controlada de caches, análise de frametime via Intel PresentMon e gerenciamento reversível do Registro do Windows.
- **Target Audience / Persona**: Entusiastas de hardware, jogadores competitivos e técnicos de sistemas que exigem transparência absoluta sobre cada ajuste aplicado no sistema operacional, sem efeitos colaterais ocultos ou métricas falsas.
- **Key Value**: Execução verificada por releitura imediata, reversibilidade fiel de todos os tipos de dados do Registro (`DWORD`, `QWORD`, `STRING`, `EXPAND_SZ`, `MULTI_SZ`, `BINARY`) com exclusão limpa de valores originalmente inexistentes e isolamento rigoroso de segurança.

---

## 2. User Experience & Visual Design

- **Key User Flows**:
  1. **Inicialização Controlada & Ciclo de Vida**: Ao abrir o aplicativo, o sistema verifica se o Agente Privilegiado já está ativo em instância única (via Mutex global), valida a porta local e realiza a autenticação segura por token protegido antes de exibir o status `"Agente Conectado"`. Ao fechar a janela principal, tarefas pendentes são concluídas, o servidor local é encerrado de forma limpa e os logs são persistidos.
  2. **Aplicação de Otimizações por Catálogo Autorizado**: O usuário seleciona um ajuste catalogado por identificador único. O sistema faz backup prévio do estado original, aplica o valor esperado, relê a chave no Registro para comprovar a gravação e registra a operação na auditoria.
  3. **Backup & Restauração dos Ajustes Suportados**: O usuário cria um backup assinado por hash SHA-256 canônico contendo tipos exatos e marcadores de valores originalmente ausentes. Antes de restaurar, o sistema valida estrutura, hash e whitelist, exibe o escopo ao usuário para confirmação e restaura item a item reportando contagens reais de sucesso e falha.
  4. **Limpeza Controlada & Diagnóstico de Rede/Display**: O módulo de limpeza separa claramente a varredura de estimativa da contagem real de bytes removidos (bloqueando junctions, symlinks e pastas críticas). O módulo de rede distingue latência ICMP de handshake TCP e realiza testes contra origens reais.

- **Visual Identity & Theme**:
  - *Aesthetic Direction*: Console técnico de engenharia de performance gamer de alta precisão, sóbrio e funcional, sem elementos decorativos artificiais.
  - *Color Palette & Mood*:
    - Superfície dominante (60%): Preto profundo e grafite escuro (`#07090E` e `#0B0E17`).
    - Superfícies estruturais (30%): Painéis de instrumentos em ardósia escura (`#131722`) com divisórias sutis de `1px` (`#1E2333`) e texto secundário em cinza frio (`#94A3B8`).
    - Orçamento de destaque (10%): Roxo/violeta neon (`#A855F7` / `#C084FC`) para ações primárias e estados ativos, combinado com cores semânticas acompanhadas de rótulo textual: Verde (`#16A34A` Nominal/Verificado), Âmbar (`#D97706` Alerta Térmico/Elevação Necessária) e Carmesim (`#DC2626` Falha/Bloqueio).
  - *Typography & Hierarchy*:
    - Tipografia estrutural limpa (`Plus Jakarta Sans` / `Segoe UI Variable Display` no WPF) para cabeçalhos e navegação.
    - Fonte monoespaçada com numerais tabulares (`JetBrains Mono` / `Cascadia Mono`, `tabular-nums`) obrigatória em todas as leituras de sensores, frequências, tempos de quadro (ms), contagens de bytes e hashes SHA-256.
  - *Component Styling & Layout*:
    - Layout desktop em tela cheia (baseline `1440px`) com barra lateral fixa (`260px`), barra superior enxuta e área de trabalho estruturada em grades de alta densidade e elevação única.
    - Zero pílulas decorativas em metadados estáticos; uso de separadores tipográficos limpos (`·`) e botões funcionais de linha única (`whitespace-nowrap`).

- **Interactive Feedback & Motion**:
  - Resposta imediata (`<= 150ms`) em transições de abas e modais de confirmação.
  - Estados vazios e desconectados explícitos: quando um sensor não é fornecido pelo hardware ou o agente não está ativo, os cartões exibem `"Não disponível"` sem números simulados.

---

## 3. Key Product Decisions & Trade-Offs

- **Decision 1: Catálogo Fechado de Ajustes (`TweakDefinition`) em vez de Caminhos Livres**
  - *Chosen Approach*: O frontend e o cliente WPF enviam apenas o identificador interno do ajuste (`tweakId`) e a ação desejada. O motor nativo consulta seu catálogo imutável contendo caminho permitido, nome do valor, `RegistryValueKind` esperado, valores permitidos e regra de reversão.
  - *Why*: Elimina completamente o vetor de escrita arbitrária no Registro caso uma requisição local maliciosa tente manipular o payload JSON.
  - *Alternatives Considered*: Validar apenas prefixos de chave enviados pelo cliente foi descartado por ainda permitir criação de valores arbitrários dentro de subchaves permitidas.

- **Decision 2: Preservação Fiel de Tipos e Marcadores de Inexistência no Snapshot**
  - *Chosen Approach*: Armazenar no manifesto de backup o `RegistryValueKind` exato (`DWord`, `QWord`, `String`, `ExpandString`, `MultiString`, `Binary`), a representação serializada tipada, a flag `WasInitiallyAbsent` e a flag `KeyCreatedByApp`, calculando o SHA-256 sobre uma representação canônica ordenada por chave e nome.
  - *Why*: Garante que valores ausentes antes da otimização sejam efetivamente excluídos na restauração (e verificados como ausentes), e que strings expandidas ou arrays multi-string nunca sejam corrompidos por conversões numéricas forçadas.
  - *Alternatives Considered*: Exportação simples em texto sem metadados de ausência prévia foi descartada pois não sabe distinguir entre um valor que era `0` e um valor que não existia antes do ajuste.

- **Decision 3: Autenticação Local Protegida por DPAPI e Hardening HTTP**
  - *Chosen Approach*: O agente gera um segredo criptográfico na inicialização, protege-o com DPAPI (`ProtectedData.Protect` no escopo do usuário atual) em diretório com ACL restrita e exige cabeçalho de autorização e proteção anti-CSRF em todas as rotas sensíveis e de telemetria privada, além de aplicar rate limiting, limite de payload (`64 KB`), validação de `Host`/`Origin` e semáforo de exclusão mútua para operações concorrentes.
  - *Why*: Aplicações rodando na mesma máquina não devem obter privilégios administrativos apenas requisitando um endpoint aberto em `127.0.0.1`.
  - *Alternatives Considered*: Expor `/api/auth/token` sem credencial prévia foi removido por permitir que qualquer processo local obtenha o token livremente.

---

## 4. Technical Architecture & Data Strategy *(Technical Reference)*

### Architecture & Component Diagram

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                     CAMADA DE APRESENTAÇÃO (UI CLIENTS)                      │
│  ┌────────────────────────────────────┐  ┌────────────────────────────────┐  │
│  │   Cliente Desktop Nativo (WPF)     │  │   Interface Web / Híbrida      │  │
│  │   MainWindow + MainViewModel       │  │   React + TypeScript (PT/EN)   │  │
│  │   Ciclo de Vida + Comandos MVVM    │  │   Vistas Reais Sem Simulação   │  │
│  └─────────────────┬──────────────────┘  └────────────────┬───────────────┘  │
└────────────────────┼──────────────────────────────────────┼──────────────────┘
                     │ IPC Autenticado (DPAPI + Bearer)     │ Loopback Seguro
                     ▼                                      ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│                AGENTE WINDOWS PRIVILEGIADO (127.0.0.1:52194)                 │
│  ┌────────────────────────────────────────────────────────────────────────┐  │
│  │ PrivilegedAgentServer                                                  │  │
│  │ • Validação Loopback + Host + Origin + Anti-CSRF + Rate Limiter        │  │
│  │ • Limite de Payload (64KB) + Timeout + Semáforo de Concorrência        │  │
│  └───────┬───────────────────┬────────────────────┬───────────────┬───────┘  │
│          │                   │                    │               │          │
│          ▼                   ▼                    ▼               ▼          │
│  ┌───────────────┐  ┌─────────────────┐  ┌────────────────┐  ┌────────────┐  │
│  │   Registry    │  │    Registry     │  │  DiskCleaner   │  │ Hardware & │  │
│  │   Optimizer   │  │ SnapshotEngine  │  │     Engine     │  │  Display   │  │
│  │ • Catálogo ID │  │ • JSON Canônico │  │ • Path Canônico│  │ • LHM Real │  │
│  │ • Multi-Tipo  │  │ • Hash SHA-256  │  │ • Anti-Symlink │  │ • Win32 Hz │  │
│  │ • Releitura   │  │ • Restore Fiel  │  │ • Bytes Reais  │  │ • PresentM │  │
│  └───────────────┘  └─────────────────┘  └────────────────┘  └────────────┘  │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Data Model & State

- **`TweakDefinition` & `RegistryValueState`**: Define `TweakId`, `Hive`, `SubKeyPath`, `ValueName`, `ValueKind` (`DWord`, `QWord`, `String`, `ExpandString`, `MultiString`, `Binary`), `AllowedValues`, `RecommendedValue`, `DefaultOriginValue` e `RiskLevel`. O estado capturado registra `Exists`, `ValueKind`, `SerializedData` e `KeyWasCreated`.
- **`RegistrySnapshotManifest` (Versão 2.0)**: Contém `FormatVersion`, `SnapshotId`, `CreatedUtc`, `Description`, `Entries` (ordenadas canonicamente) e `ChecksumSha256` calculado sobre a string canônica determinística das entradas.
- **`HardwareSnapshot` & `DisplayDeviceState`**: Representa leituras reais com suporte a campos anuláveis (`float?`) para que qualquer sensor ausente seja serializado como `null` e exibido na UI como `"Não disponível"`.

### Interactive Component & State Mapping

- **Inicialização e Ciclo de Vida (`App.xaml.cs` / `MainViewModel.cs`)**:
  - No evento `OnStartup`: instancia o container de dependências, verifica instância única do agente via Mutex, inicia o `PrivilegedAgentServer` de forma controlada, compartilha o token de sessão em memória/DPAPI com o `AgentClientService`, valida `/api/status` autenticado e atualiza a propriedade `AgentConnectionState`.
  - No evento `OnExit`: aciona `CancellationTokenSource`, aguarda encerramento de operações em andamento, chama `Dispose()` no agente e no monitor de hardware e grava o log final de auditoria.
- **Execução e Restauração de Ajustes (`RegistryOptimizer` <-> UI)**:
  - Aplicação valida o `tweakId` no catálogo, captura o valor/tipo atual (ou marca `WasInitiallyAbsent = true`), grava o novo valor com o `RegistryValueKind` correto, relê imediatamente e retorna o estado comprovado.
  - Restauração verifica se `WasInitiallyAbsent == true`: caso positivo, remove o valor (`DeleteValue`) e relê para confirmar que `GetValue(name) == null`; caso contrário, grava o valor original preservando seu `RegistryValueKind` original.
- **Alinhamento Completo de Rotas Frontend <-> Agente**:
  - Todas as rotas consumidas por `nativeBridge.ts` e `AgentClientService.cs` estarão implementadas de ponta a ponta no `PrivilegedAgentServer.cs`:
    - `GET /api/status` (estado, versão, elevação)
    - `POST /api/auth/handshake` (troca autenticada via prova DPAPI/local)
    - `GET /api/telemetry` (sensores reais LHM + WMI)
    - `GET /api/display/info` (monitores reais via `EnumDisplaySettingsW` / `EnumDisplayDevicesW`)
    - `GET /api/drivers` (consulta real `Win32_PnPSignedDriver`)
    - `GET /api/network/diagnostics` (testes reais ICMP, TCP handshake, DNS UDP e throughput)
    - `POST /api/presentmon/capture` (verificação de binário PresentMon, captura real de processo e parser dinâmico)
    - `GET /api/debloat/scan` e `POST /api/debloat/purge` (estimativa vs limpeza real com bloqueio de ReparsePoints/Symlinks)
    - `POST /api/tweaks/apply` e `POST /api/tweaks/restore` (operações por catálogo de ajustes)
    - `POST /api/snapshot/create`, `GET /api/snapshot/verify` e `POST /api/snapshot/restore` (backup e restauração verificada dos ajustes suportados)
