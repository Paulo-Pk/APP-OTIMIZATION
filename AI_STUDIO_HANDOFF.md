# PC Control Ultimate — entrega para Google AI Studio

## Objetivo desta etapa
Usar esta branch como base de trabalho no Google AI Studio para revisar a solução, integrar o backend já preparado e executar a compilação. Esta entrega NÃO está declarada como compilada ou validada.

## Branch correta
`recovery/pc-control-ultimate-integration-2026-11-16`

PR de acompanhamento: https://github.com/Paulo-Pk/APP-OTIMIZATION/pull/12

## Instrução para o Google AI Studio

Copie o texto abaixo no AI Studio depois de abrir/sincronizar a branch indicada:

---

Você está trabalhando no projeto existente **PC Control Ultimate (PCControlUltimate)**. NÃO crie outro projeto e NÃO reconstrua a aplicação do zero.

### Regras obrigatórias
1. Trabalhe sobre a branch `recovery/pc-control-ultimate-integration-2026-11-16` deste repositório. Não altere `main), não faça merge de PR e não apague branches.
2. Preserve integralmente a interface atual, o layout React/WPF, os componentes, os estilos e os recursos visuais já existentes. Não substitua a Preview por uma interface genérica.
3. Não invente sensores, temperaturas, velocidades, resultados de limpeza, diagnósticos, drivers ou otimizações. Quando o Windows/hardware não fornecer um dado, mostre “Não disponível” e explique o motivo.
4. Não remova APIs, métodos públicos, telas ou funcionalidades existentes para fazer a compilação passar. Corrija a causa real e mantenha compatibilidade.
5. Não execute ações destrutivas no Windows do usuário. Operações de limpeza, Registro, rede, drivers e ajustes devem ter confirmação, validação, logs e possibilidade de restauração quando aplicável.
6. Não use GitHub Actions, workflows pagos ou serviços externos que gerem cobrança. Faça a validação no ambiente disponível do AI Studio; se ele não tiver Windows/.NET Desktop, informe claramente essa limitação e não simule uma compilação bem-sucedida.
7. Não faça commit nem push sem apresentar um resumo das alterações e aguardar confirmação.

### Etapas de trabalho
A. Antes de editar, examine a estrutura completa do repositório, os arquivos de solução/projetos, dependências, scripts e estado atual do Git. Identifique o framework e SDK exigidos.
B. Leia `scripts/test-local.ps1` e os testes adicionados nesta branch. Verifique se o script é seguro e se corresponde aos projetos reais.
C. Integre os serviços de telemetria universal, diagnóstico, limpeza, drivers, PresentMon e rede aos pontos de entrada já existentes, sem substituir a interface. Verifique o fluxo completo: serviço C# → ViewModel/bridge → tipos TypeScript → componentes React.
D. Para sensores, mantenha CPU, GPU, hotspot, VRAM, RAM e cada unidade de disco separados. CPU temperature e CPU clock devem vir de sensores/fontes reais compatíveis; não use uso de CPU como substituto de temperatura ou frequência. Atualização-alvo de 1 segundo, sem travar a UI.
E. Para rede, diferencie falha de ICMP filtrado, perda real e resultado inconclusivo. Não apresente 50% de perda fixa ou valores simulados. Não prometa reduzir ping sem evidência.
F. Para limpeza, faça primeiro simulação (Dry-Run), mostre arquivos/categorias e estimativa, solicite confirmação e depois apresente relatório real do que foi removido e espaço efetivamente liberado.
G. Revise segurança, permissões, cancelamento, tratamento de exceções, caminhos protegidos, symlinks e restauração. Não integre mudanças acopladas de Registro/HMAC/agente privilegiado parcialmente; se precisar delas, analise e integre o conjunto completo com testes.
H. Execute testes e compilação apropriados. Registre os comandos exatos, SDKs/ambiente, resultado por projeto e erros remanescentes. Se o ambiente for Linux/sandbox e não suportar WPF/Windows Desktop, não declare o build Windows aprovado: faça apenas verificações compatíveis e forneça os comandos para validação em Windows.
I. Ao final, entregue um relatório objetivo: arquivos alterados, funcionalidades realmente conectadas, testes executados e aprovados/reprovados, limitações e próximos passos. Não marque a tarefa como concluída enquanto houver erros de compilação.

### Critérios de aceite
- Solução original preservada; nenhuma tela removida.
- Sem dados fictícios e sem botões sem ação.
- Sem erros de TypeScript/testes frontend que o ambiente suporte.
- Sem erros .NET nos projetos que o ambiente suporte.
- WPF validado em Windows com SDK compatível antes de declarar compilação final.
- Nenhuma alteração em `main` e nenhum uso de Actions pagas.

Comece pela auditoria e compilação, corrija os erros encontrados e só então faça a integração incremental. Não faça uma reformulação visual.

---

## Estado conhecido desta branch
Esta branch contém a integração seletiva de 18 arquivos de backend e testes. A interface React/Preview, `App.tsx`, componentes visuais e `nativeBridge.ts` foram deliberadamente preservados e ainda precisam de auditoria/ligação. As mudanças acopladas de Registro/HMAC/agente privilegiado também não foram integradas nesta etapa.

**Importante:** o PR #12 está em Draft. A existência desta branch não significa que o aplicativo já compilou. A validação Windows/.NET ainda precisa ser feita em ambiente Windows com Bun e .NET 10 (ou SDK efetivamente exigido pela solução).
