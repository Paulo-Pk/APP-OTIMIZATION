# PC Control Ultimate — Fundação Segura, Navegação Modular WPF e Integrações Reais

Entrega da consolidação de segurança, arquitetura desktop nativa WPF (.NET 10) e verificação de integridade para o **PC Control Ultimate**, eliminando qualquer bypass do catálogo de Registro, endurecendo a autenticação do Agente Privilegiado e implementando navegação modular real com internacionalização completa PT-BR / EN-US.

## User Review & Critical Decisions

> [!IMPORTANT]
> As decisões abaixo foram confirmadas na etapa de esclarecimento e governam a execução técnica em ordem estrita de prioridade (Segurança → Aplicação Desktop → Integrações → Validação).

- **Confirmed Decision 1 — Restauração Estritamente Vinculada ao Catálogo e Snapshots Validados**: Toda aplicação e reversão de ajustes no Registro opera exclusivamente por `TweakId` pertencente ao catálogo fechado do backend ou por entradas validadas de um snapshot íntegro. Fica eliminado qualquer caminho público ou rota HTTP que aceite `keyPath`, `valueName`, `valueKind` ou `targetValue` arbitrários enviados pelo cliente.
- **Confirmed Decision 2 — Política de Origem e Anti-Replay para Clientes Locais Legítimos**: Chamadas de navegadores exigem cabeçalho `Origin` estrito na lista autorizada (sem wildcard CORS). Chamadas locais legítimas do cliente desktop WPF (que não enviam `Origin`) devem apresentar obrigatoriamente o cabeçalho `X-PCControl-Client: WPF-Native-v2.4`, timestamp recente (`X-PCControl-Timestamp` dentro de janela de 60 segundos) e nonce único (`X-PCControl-Nonce`) validado contra cache anti-replay no servidor.
- **Confirmed Decision 3 — Integração Contínua (CI) Windows x64**: Criação de workflow automatizado do GitHub Actions em `windows-latest` executando `dotnet restore`, `dotnet build -c Release` e `dotnet test -c Release` com empacotamento x64.

---

## 1. Overview & Core Concept

- **What It Does**: Software desktop nativo Windows 10/11 (`.NET 10` WPF) apoiado por Agente Local Privilegiado (`127.0.0.1:52194`) para telemetria física (`LibreHardwareMonitor`), análise real de quadros (`Intel PresentMon`), otimização auditada do Registro por catálogo fechado, backup/restauração verificada com rollback, limpeza segura de caches com proteção anti-symlink, diagnóstico de rede e inventário de drivers assinados. O projeto React existente é mantido sincronizado como referência visual e funcional sem contradizer as regras de segurança do backend.
- **Target Audience / Persona**: Entusiastas de hardware, jogadores competitivos e engenheiros de suporte Windows que exigem diagnósticos transparentes, reversibilidade comprovada por releitura e zero telemetria fictícia.
- **Key Value**: Nenhuma operação de sistema é simulada ou aceita parâmetros arbitrários não catalogados; cada mutação registra auditoria completa (usuário Windows, horário UTC, `TweakId`, valor anterior, valor solicitado, confirmação pós-leitura) e pode ser revertida com segurança.

---

## 2. User Experience & Visual Design

- **Key User Flows**:
  1. **Inicialização e Verificação de Segurança**: Ao abrir o aplicativo WPF, o `PrivilegedAgentServer` valida a proteção DPAPI e ACL NTFS do token de sessão. Se DPAPI ou ACL falharem, o status reporta explicitamente a falha de proteção em vez de declarar segurança.
  2. **Navegação Modular Real no WPF**: Clicar em cada um dos 9 módulos da barra lateral (`Dashboard`, `Hardware Monitor`, `Registry Optimizer`, `Restore Center`, `Debloat/Cleaner`, `Network Lab`, `Gaming/PresentMon`, `Diagnostics`, `Driver Center`) alterna o `ContentControl` central para a tela específica do módulo com seu respectivo estado e comandos.
  3. **Troca Instantânea de Idioma (PT-BR / EN-US)**: O seletor no cabeçalho alterna imediatamente todos os textos de navegação, cabeçalhos, botões, tooltips, alertas e relatórios entre Português Brasileiro (padrão) e Inglês Americano, persistindo a preferência do usuário em disco.
  4. **Fluxo de Backup, Inspecão e Restauração**: No `Restore Center`, o usuário visualiza todos os backups existentes com data/hora, cobertura de itens capturados vs. falhas de leitura, status de verificação SHA-256 (explicado claramente como verificação de integridade contra corrupção, e não assinatura digital criptográfica), inspeciona os valores antes de restaurar e executa restauração completa ou individual com rollback em caso de inconsistência.
- **Visual Identity & Theme**:
  - *Aesthetic Direction*: Console de engenharia de sistemas de alta precisão em tema escuro (`#07090E` canvas principal, `#0F121B` superfícies estruturais, `#131722` cartões de dados com borda de `1px` `#1E2333`).
  - *Color Palette & Mood*: Distribuição 60-30-10 com acento violeta/roxo (`#A855F7` / `#C084FC`) para ações primárias e estados ativos, verde esmeralda (`#16A34A`) para leituras verificadas, âmbar (`#D97706`) para alertas ou sensores indisponíveis (`N/D`) e vermelho (`#DC2626`) para bloqueios de segurança.
  - *Typography & Hierarchy*: Tipografia limpa sem serifa para títulos e rótulos combinada com fonte monoespaçada tabular (`Consolas` / `JetBrains Mono`) para leituras de sensores, hashes SHA-256, latências e contadores de quadros.

---

## 3. Key Product Decisions & Trade-Offs

- **Decision 1 — Catálogo Estrito por `TweakId` e Par `(Hive, SubKeyPath, ValueName)`**:
  - *Chosen Approach*: Tornar privados os métodos de escrita de baixo nível no `RegistryOptimizer` e expor publicamente apenas operações que validam o `TweakId` contra `RegistryOptimizer.Catalog` (para aplicação) ou que validam o trio exato `(Hive, SubKeyPath, ValueName)` e `RegistryValueKind` contra o catálogo fechado (para restauração).
  - *Why*: Impede que uma subchave autorizada (como `SystemProfile`) seja usada para modificar outros valores não previstos dentro da mesma chave ou em subchaves filhas.
  - *Alternatives Considered*: Validar apenas o prefixo da chave (`StartsWith`), o que foi descartado por permitir escrita em subchaves ou valores não auditados.
- **Decision 2 — Proteção DPAPI + ACL Estrita e Anti-Replay no Agente**:
  - *Chosen Approach*: Retornar objeto estruturado `TokenProtectionResult(bool Success, bool DpapiApplied, bool AclApplied, string? ErrorMessage)` ao salvar o token. Exigir cabeçalho `Origin` válido para navegadores ou o conjunto `X-PCControl-Client` + `X-PCControl-Timestamp` + `X-PCControl-Nonce` para o cliente WPF.
  - *Why*: Garante que falhas de criptografia ou permissão de arquivo nunca sejam mascaradas como sucesso e protege o agente local contra ataques de replay e CSRF.
- **Decision 3 — Rollback Transacional em Restauração de Snapshot**:
  - *Chosen Approach*: Antes de aplicar a restauração de um snapshot no `RegistrySnapshotEngine`, capturar em memória o estado atual das chaves que serão alteradas; caso uma falha crítica interrompa a restauração e o usuário opte por reverter ou a política de consistência exija, restaurar o estado pré-operação e registrar o resultado item a item.
  - *Why*: Evita deixar o Registro do Windows em estado híbrido não documentado quando ocorre falha de permissão no meio de um lote.

---

## 4. Technical Architecture & Data Strategy *(Technical Reference)*

### Architecture & Component Diagram

```
┌─────────────────────────────────────────────────────────────────────────────────────┐
│                   PC CONTROL ULTIMATE — DESKTOP WPF (.NET 10 x64)                   │
│                                                                                     │
│  ┌──────────────────────┐    ┌───────────────────────────────────────────────────┐  │
│  │  LocalizationService │───▶│             MainWindow + MainViewModel            │  │
│  │   (PT-BR / EN-US +   │    │  • Navegação Modular (ContentControl / 9 Views)   │  │
│  │   JSON Persistence)  │    │  • Seletores de Idioma, Elevação UAC & Status     │  │
│  └──────────────────────┘    └─────────────────────────┬─────────────────────────┘  │
│                                                        │                            │
│       ┌────────────────────────┬───────────────────────┼───────────────────────┐    │
│       ▼                        ▼                       ▼                       ▼    │
│  ┌──────────────┐      ┌───────────────┐      ┌─────────────────┐     ┌───────────┐ │
│  │ Dashboard /  │      │   Registry    │      │ Restore Center  │     │ Gaming /  │ │
│  │  Hardware /  │      │   Optimizer   │      │  (Lista, Diff,  │     │ Network / │ │
│  │ GPU Health / │      │ (Catálogo por │      │  Item/Full +    │     │ Debloat / │ │
│  │ Diagnostics  │      │   TweakId)    │      │   Rollback)     │     │  Drivers  │ │
│  └──────┬───────┘      └───────┬───────┘      └────────┬────────┘     └─────┬─────┘ │
└─────────┼──────────────────────┼───────────────────────┼────────────────────┼───────┘
          │                      │                       │                    │
          ▼                      ▼                       ▼                    ▼
┌─────────────────────────────────────────────────────────────────────────────────────┐
│          AgentClientService (Bearer Token DPAPI + Nonce + Timestamp Headers)        │
└──────────────────────────────────────────┬──────────────────────────────────────────┘
                                           │ Loopback HTTP (127.0.0.1:52194)
                                           ▼
┌─────────────────────────────────────────────────────────────────────────────────────┐
│                         PrivilegedAgentServer (Windows Service/Host)                │
│  • Loopback & Host Check  • Strict Origin / Client Header Policy  • Nonce Cache     │
│  • 64KB Payload Cap       • SemaphoreSlim Concurrency Lock        • Audit Logger    │
├──────────────────┬──────────────────┬───────────────────┬───────────────────────────┤
│ RegistryOptimizer│ RegistrySnapshot │ PresentMonSession │ LibreHardwareMonitor      │
│ (Closed Catalog  │ Engine (SHA-256  │ Parser (Argument  │ Wrapper + DiskCleaner +   │
│  Only + Re-read) │ + Rollback)      │ List + Timeout)   │ NetworkDiag + DriverScan  │
└──────────────────┴──────────────────┴───────────────────┴───────────────────────────┘
```

### Interactive Component & State Mapping

1. **Prioridade 1 — Segurança do Registro e Agente (`RegistryOptimizer`, `PrivilegedAgentServer`, `IntegrityVerifier`, `RegistrySnapshotEngine`)**:
   - Remoção completa do fallback que aceitava `keyPath`/`valueName`/`valueKind` genéricos em `/api/tweaks/apply` e no `AgentClientService`.
   - Validação estrita por `(Hive, SubKeyPath, ValueName)` exato (sem `StartsWith` permissivo que abra subchaves filhas não listadas).
   - Registro detalhado de auditoria por operação: usuário Windows (`Environment.UserDomainName\Environment.UserName`), timestamp UTC, `TweakId`, valor anterior, valor solicitado, valor relido pós-gravação e erro (se houver).
   - Proteção DPAPI e ACL verificada com retorno explícito de status; política explícita para requisições sem `Origin` exigindo `X-PCControl-Client`, `X-PCControl-Timestamp` e `X-PCControl-Nonce`.
   - Snapshot v2.1 registrando itens capturados, falhas de leitura individuais (`UnreadableKeys`), verificação de integridade pós-gravação imediata, listagem de backups existentes, visualização prévia dos itens, restauração individual ou completa e rollback seguro.

2. **Prioridade 2 — Navegação Modular e Internacionalização WPF (`LocalizationService`, `MainWindow.xaml`, `MainViewModel.cs`)**:
   - Criação do `LocalizationService` centralizado com dicionários completos `pt-BR` (padrão) e `en-US`, notificação reativa (`INotifyPropertyChanged`) e persistência em `%LOCALAPPDATA%\PCControlUltimate\user_preferences.json`.
   - Implementação de navegação real em `MainWindow.xaml` com 9 visões dedicadas renderizadas dinamicamente conforme `CurrentTab`:
     1. `dashboard`: Visão geral de saúde, resumo de sensores reais, estado do agente/UAC e atalhos rápidos.
     2. `hardware`: Monitoramento detalhado de CPU, GPU (incluindo Hotspot e VRAM), RAM, armazenamento e ventoinhas com indicação explícita de `N/D` / `Sensor indisponível` quando o hardware não expuser o sensor, além do diagnóstico contextualizado do GPU Health Lab.
     3. `tweaks`: Catálogo fechado de otimizações do Registro com filtro, nível de risco, valor recomendado, estado atual verificado e botões individuais de Aplicar e Restaurar.
     4. `restore`: Listagem de backups existentes em disco, contagem de itens capturados e falhas de leitura, verificação SHA-256, inspeção detalhada das entradas do backup selecionado e restauração individual ou completa com confirmação explícita.
     5. `debloat`: Categorias autorizadas de limpeza (`cache-dx-shaders`, `temp-windows`, `temp-user`, `update-cache`), simulação prévia (`Dry-Run` / Estimativa), confirmação explícita antes da exclusão, medição real de bytes liberados, contagem de arquivos bloqueados/symlinks ignorados e log de itens afetados.
     6. `network`: Diagnóstico separado em ICMP Echo, TCP Handshake, Consulta DNS, Latência para destinos configurados e Throughput externo real (`speed.cloudflare.com`), com histórico de medições.
     7. `gaming`: Verificação do executável Intel PresentMon, listagem de processos ativos no Windows, seleção de processo, duração configurável, início/cancelamento de captura com `ArgumentList` seguro, leitura de `stdout`/`stderr`, código de saída e exibição de FPS médio, 1% Low, gráfico de frametimes e histórico de sessões.
     8. `diagnostics`: Diagnóstico de integridade do sistema, status de elevação UAC, estado de proteção DPAPI/ACL e visualizador da trilha de auditoria real.
     9. `drivers`: Inventário somente-leitura de drivers assinados (`Win32_PnPSignedDriver`) exibindo dispositivo, fabricante, versão, data e status de assinatura, deixando explícito que se trata de inventário/verificação e nunca baixando ou instalando drivers automaticamente.

3. **Prioridade 3 & 4 — Testes Automatizados, CI Windows e Documentação**:
   - Expansão da suíte `tests/PCControlUltimate.Tests` cobrindo rejeição de IDs fora do catálogo, rejeição de valores não permitidos, bloqueio de subchaves filhas não autorizadas, preservação de todos os 6 tipos do Registro, cobertura e falhas de leitura de backup, restauração parcial/completa com rollback, validação de nonce anti-replay e cabeçalhos do agente, tratamento de sensores ausentes, validação segura de nome de processo no PresentMon e proteção contra reparse points no limpador.
   - Adição de `.github/workflows/windows-ci.yml` para compilação e execução automática de testes em `windows-latest` (x64 Release).
   - Atualização completa do `README.md` documentando arquitetura, requisitos, comandos de build/execução, matriz de permissões UAC, geração de instalador Windows x64 e limitações conhecidas.
