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
