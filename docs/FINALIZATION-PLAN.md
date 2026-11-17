# PC Control Ultimate — plano de finalização

Atualizado em 2026-11-17.

## Base preservada

- Repositório: `Paulo-Pk/APP-OTIMIZATION`
- Branch padrão: `main`
- HEAD da `main` observado: `ff031355e60fc188dea6f0efd723a2b1c4e765ee`
- Branch de integração: `integration/selective-mainline-2026-11-16`
- HEAD de integração observado: `4dadaa49f468e39d161b2d404daca6e76351ed22`
- Branch de trabalho sem PR/CI automático: `finalization/pc-control-ultimate-2026-11-17`

A `main` não foi alterada. Nenhuma PR foi mesclada.

## Correções registradas nesta rodada

- `src/App.tsx`: as rotinas periódicas de telemetria e descoberta de jogos capturam falhas transitórias, evitando rejeições não tratadas e preservando a última lista de jogos quando uma consulta falha temporariamente. Commit: `4dadaa49f468e39d161b2d404daca6e76351ed22`.
- `server.ts`: o diagnóstico TCP agora informa 100% de falha de handshake quando todas as três tentativas falham, sem chamar isso de perda ICMP/UDP; rejeita listas de jogos sem IDs válidos em vez de calcular uma porcentagem indefinida; e descreve dinamicamente os endpoints realmente consultados. Commit: `6bd540d2a403a8d74d981b8d95acce38996580b3`.

Essas são correções por inspeção estática; ainda não foram executadas compilação nem testes.

## Auditoria estática complementar — 2026-11-17

- `LibreHardwareMonitorWrapper.cs`: leitura de CPU usa sensores reais do LibreHardwareMonitor e, quando não há clock disponível, consulta `Win32_Processor.CurrentClockSpeed` com cache de 3 segundos. A temperatura da CPU também considera sensores expostos por Motherboard/SuperIO; não há uma fonte universal para notebooks/placas que não exponham sensor. RAM física é isolada pelo identificador `/ram`; os contadores de VRAM aceitos são apenas memória dedicada, sem somar `D3D Shared Memory`. Volumes fixos são enumerados via `DriveInfo`, além dos dispositivos físicos com sensor. Isso reduz confusões de RAM/VRAM e permite listar várias unidades, mas depende do que o Windows/driver expõe.
- `WireGuardTunnelManager.cs` e `GameNetworkAcceleratorService.cs`: o código deixa explícito que roteamento por processo via WFP está desativado, exige CIDRs de destino e valida a configuração WireGuard; rejeita hooks de script e DNS global, além de bloquear cobertura de rota padrão sem consentimento. Não há base para prometer aceleração tipo ExitLag sem relay funcional, túnel instalado e handshake/tráfego confirmados. A auditoria de configuração, por si só, não ativa o túnel.
- `DiskCleanerEngine.cs`: o mecanismo valida caminhos, evita reparse points/symlinks e reporta itens removidos/preservados. A auditoria do agente privilegiado confirmou que o fluxo apenas repassava a confirmação do usuário e não interrompia/reiniciava os serviços do Windows Update. Por segurança, `update-cache` foi bloqueado também quando confirmado; nenhuma exclusão ocorre até que a parada/reinício e a recuperação do serviço sejam implementados e testados. O método legado `PurgeCategory` também não limpa essa categoria.
- `BenchmarkCenterView.tsx`: o histórico armazenava leituras pontuais de temperatura em campos chamados `*TempPeak`, embora não houvesse amostragem contínua durante o benchmark. Corrigido o rótulo da interface para “Temperatura registrada” e incluída explicação de que não representa pico térmico. Commit: `43dd8a3719c956a838490a111dbc1c032df06024`.
- `DiskCleanerEngine.cs` / `DiskCleanerTests.cs`: o fluxo do agente privilegiado apenas encaminhava a confirmação do usuário; não parava nem reiniciava `wuauserv`/serviços relacionados. Para evitar que a UI prometa uma limpeza segura que o código não executa, a limpeza de `update-cache` agora permanece bloqueada mesmo após confirmação, até existir uma rotina de ciclo de serviço com recuperação verificada. Incluído teste para garantir zero arquivos removidos nesse estado. Commits: `94431e326189de478d5bfeb986e991ed95375dc9` e `4f8856d3034e1687e848007c3d5577491e110495`.
- A auditoria do wrapper de hardware confirma que o intervalo de polling é configurável e rejeita valores não positivos. O `MainViewModel` da aplicação WPF usa `DispatcherTimer` de 1 segundo e chama `RefreshTelemetryAndStatusAsync`, que captura um novo snapshot; portanto, a intenção de atualização a cada segundo está implementada no fluxo principal. A atualização real pode atrasar se a leitura de sensores levar mais tempo ou estiver bloqueada.

## Situação das Pull Requests

- PR #10 — Draft, 304 commits; base `fix/cpu-telemetry-and-ux-audit`.
- PR #11 — Draft, 346 commits; empilhada sobre PR #10.
- PR #12 — Draft, 9 commits; integração seletiva de hardware/rede/telemetria, base de recuperação.
- PR #13 — integração seletiva para `main`, 30 commits, 18 arquivos alterados, sem conflitos reportados no último estado consultado.

As linhas #10, #11 e #12 divergem da branch de integração. Não mesclar ou cherry-pick em massa: a comparação indica históricos divergentes e risco de duplicação/conflito.

## Validação — ainda não aprovada

O workflow Windows x64 foi acionado automaticamente ao atualizar a branch da PR #13. O GitHub encerrou a execução em aproximadamente 2–3 segundos, com `runner_id=0` e `steps=[]`. Nenhuma etapa de checkout, instalação, compilação ou teste foi iniciada. Isso é falha de alocação/execução do runner, não um resultado de compilação.

Execuções observadas:
- Run `44084505312`, commit `db50ed14ecb5ff3a0882598363eef391d087f860`.
- Run `44092896654`, commit `4dadaa49f468e39d161b2d404daca6e76351ed22`.

Não repetir Actions nem solicitar aumento de limite/cobrança.

## Etapas obrigatórias antes de declarar versão final

1. Revisar e integrar manualmente as alterações necessárias das PRs #10, #11 e #12 na branch de finalização, resolvendo conflitos por arquivo e preservando a interface existente.
2. Executar localmente no Windows: `.\scripts\test-local.ps1` (Bun + .NET 10 SDK).
3. Corrigir cada falha real de TypeScript, build .NET ou xUnit.
4. Executar publicação: `.\scripts\test-local.ps1 -Publish`.
5. Testar o executável em Windows real: sensores, CPU/GPU/VRAM/discos, drivers, limpeza com relatório, ajustes/restauração do Registro, descoberta de jogos, PresentMon, diagnósticos ICMP/TCP/DNS e rede WireGuard sem alteração de rota sem consentimento.
6. Fazer revisão visual comparando a interface com a Preview original.
7. Somente depois da validação, preparar uma PR final revisável. Não mesclar em `main` sem autorização explícita.

## Critério de conclusão

O projeto só pode ser marcado como finalizado quando o build, os testes automatizados, a publicação x64 e os testes funcionais em Windows forem comprovados. O estado atual é **em consolidação; validação pendente**.
