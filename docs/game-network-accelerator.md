# Game Network Accelerator — escopo e operação

## O que está implementado nesta etapa

- Detecção heurística de processos de jogos conhecidos (somente leitura).
- Medição TCP repetida para host/porta informados pelo usuário, com média, jitter entre amostras e taxa de falha de conexão.
- Traceroute ICMP com TTL incremental; saltos que não respondem são exibidos como sem resposta.
- Adaptador para o cliente oficial WireGuard for Windows.
- Importação de arquivo `.conf` fornecido pelo usuário por um provedor compatível ou pelo administrador do próprio VPS.
- Instalação e remoção explícitas do serviço WireGuard, com confirmação e exigência de Administrador.
- Comparação manual de medição direta e medição após a instalação do túnel.

## Limitações importantes

1. A medição TCP é o tempo de estabelecimento de conexão TCP até o endpoint escolhido. Não é o ping ICMP e não é necessariamente a latência do servidor da partida.
2. O traceroute ICMP pode ser filtrado por roteadores e redes. Um salto sem resposta não comprova perda de pacotes no jogo.
3. O WireGuard aplica as rotas descritas em `AllowedIPs`. Se o perfil usar `0.0.0.0/0` ou `::/0`, poderá redirecionar o tráfego geral do Windows. **O roteamento seletivo por processo/jogo ainda não está implementado.**
4. Não existe integração nativa com APIs privadas/proprietárias de ExitLag, NoPing, GearUP ou outros provedores. O adaptador atual aceita perfis WireGuard compatíveis; cada provedor precisa oferecer formalmente esse tipo de configuração ou uma API documentada.
5. O aplicativo não cria, contrata nem provisiona VPS automaticamente. O servidor próprio precisa ser provisionado e protegido separadamente.
6. Não há promessa de redução de ping. Um túnel pode aumentar latência, jitter ou perda dependendo da localização, peering, congestionamento e rota até o destino.

## Usar um servidor próprio

1. Provisione um VPS Linux em uma região próxima do destino do jogo e confirme a política de tráfego, firewall e limites do provedor.
2. Instale WireGuard no servidor e configure uma interface com chaves distintas para servidor e cliente.
3. Habilite encaminhamento IP e configure NAT/forwarding apenas para as redes que pretende transportar.
4. Configure o firewall para permitir a porta UDP escolhida e restrinja a administração SSH.
5. Gere um perfil de cliente com `[Interface]`, chave privada do cliente, endereço e DNS, além de um `[Peer]` com chave pública do servidor, endpoint e `AllowedIPs` intencionalmente limitados.
6. Proteja o arquivo `.conf`: ele contém uma chave privada. Não o envie para chats, repositórios ou serviços não confiáveis.
7. No PC Control Ultimate, faça primeiro uma medição direta do endpoint real do jogo. Depois selecione o perfil e instale o túnel com confirmação UAC.
8. Repita a medição para o mesmo endpoint e compare média, jitter e falhas. Se houver piora ou instabilidade, remova o túnel pelo próprio módulo.

> Não use um endpoint público genérico como substituto do endpoint do jogo para concluir que o jogo ganhou desempenho. Se o jogo não revelar um host/porta mensurável, compare os diagnósticos como referência de rede, não como medição da partida.

## Próximas etapas arquiteturais

- Catálogo de provedores com adaptadores independentes e APIs oficiais documentadas.
- Descoberta e health-check de nós, com histórico de medições por região.
- Comparação repetida de rota direta e rota tunelada até o mesmo destino.
- Roteamento seletivo por processo apenas quando houver solução suportada, auditável e compatível com anti-cheat; sem DLL injection, hooks ou driver kernel próprio.
- Gerenciador de nós próprios com provisionamento explícito e credenciais protegidas.
