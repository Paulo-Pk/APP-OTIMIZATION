# Game Network Accelerator — integração e revisão de segurança

## Decisão de integração

A implementação atual de `main` é a base da integração. Ela já contém o módulo `PCControlUltimate.Gaming.Network`, os contratos de aceleração, o motor registrado no contêiner de DI, a navegação WPF e os testes correspondentes.

Os arquivos da branch histórica `feature/game-network-accelerator` em `PCControlUltimate.Gaming.NetworkAcceleration` não devem ser copiados por cima da arquitetura atual: isso duplicaria contratos/serviços e a branch histórica também contém uma classe `GameNetworkAcceleratorViewModel` com o mesmo nome e namespace de uma classe que já existe em `ModuleViewModels.cs` na versão atual.

## O que foi preservado

- A arquitetura mais recente do acelerador já presente em `main`.
- Detecção de jogos/conexões por observação de processos e tabela de sockets do Windows, sem injeção em processos.
- Diagnóstico multi-amostra e traceroute, com distinção entre ICMP/TCP e latência real da partida.
- Comparação de rota direta e nó intermediário com recomendação conservadora.
- Integração WireGuard com validação de configuração e consentimento explícito/UAC.
- Os commits da branch histórica permanecem preservados como segundo histórico de integração; nenhum commit original foi reescrito.

## Correções realizadas nesta branch

1. Removidas afirmações que davam a entender garantia de compatibilidade com anti-cheat. O código não injeta DLLs, não faz hook de sockets e não manipula memória, mas isso não garante compatibilidade com Vanguard, Ricochet, EAC, BattlEye ou outros sistemas.
2. A remoção do serviço WireGuard não é mais descrita como restauração comprovada da tabela de rotas. A captura anterior é apenas diagnóstica; o aplicativo não reaplica essa tabela nem verifica integralmente o estado de rede após a remoção. Por isso, `RouteTableRestored` permanece falso até existir uma verificação real.

## Bloqueadores antes de uso em produção

- **Proteção da chave privada:** foi implementado o endurecimento da ACL da pasta de túneis antes de gravar o perfil, permitindo acesso ao usuário atual, LocalSystem (serviço WireGuard) e Administradores. A instalação falha de forma fechada se o `icacls.exe` retornar erro. A CI validou a compilação e os testes dessa implementação. Ainda falta verificar as ACLs efetivas e o comportamento do serviço WireGuard em Windows real, além de definir a limpeza segura dos arquivos.
- **Restauração de rede:** implementar verificação explícita do estado de rotas/adaptadores após remover o túnel, sem tentar reproduzir cegamente uma saída textual de `route print`.
- **Roteamento por processo:** `AllowedIPs` faz roteamento por destino IP/CIDR, não por PID/executável. A opção de roteamento WFP por processo continua desativada.
- **Anti-cheat:** testar cada jogo/anti-cheat suportado e seguir as políticas do fornecedor. Não existe garantia universal.
- **Medições:** ICMP e handshake TCP são diagnósticos de rede; não equivalem necessariamente ao ping/UDP observado dentro da partida. Não prometer redução de ping.
- **Nós intermediários:** validar a conectividade de ponta a ponta e o handshake real antes de recomendar qualquer nó. Um teste até o endpoint do relay não mede, por si só, a latência relay→servidor do jogo.

## Validação

A branch histórica teve uma execução de CI que falhou na etapa de compilação, antes de executar os testes. Os erros registrados incluem incompatibilidades entre testes e APIs de Registry Snapshot/Optimizer, Disk Cleaner, PresentMon, HardwareSnapshot e MainViewModel. Esses erros não devem ser atribuídos exclusivamente ao módulo de rede.

A validação automatizada foi concluída no workflow Windows x64 (run `42918239768`, commit `4f90ffa57d9cd983319699aa38c7f82972199a83`): restore, auditoria NuGet, build Release x64, **105 testes xUnit**, publish self-contained, verificação de dependências e empacotamento terminaram com sucesso. O artefato `PCControlUltimate-win-x64-Release` foi gerado. Isso ainda não substitui teste manual em Windows físico: inicialização WPF, sensores reais, ACL efetiva, instalação/remoção WireGuard, rotas e compatibilidade com jogos/anti-cheat continuam pendentes. Mantenha o PR em rascunho e não faça merge em `main` até concluir essa validação prática.
