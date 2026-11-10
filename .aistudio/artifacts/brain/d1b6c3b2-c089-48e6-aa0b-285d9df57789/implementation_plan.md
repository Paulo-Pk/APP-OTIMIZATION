# Plano de Implementação — Correção Definitiva, Auditoria e Validação (PC Control Ultimate)

## 1. Visão Geral e Diagnóstico da Auditoria
Na última execução da pipeline Windows x64 (`feature/game-network-accelerator`), foram identificados dois problemas pontuais que impediram a conclusão limpa do build:
1. **Erro de Compilação `CS1739` em `src/PCControlUltimate.Gaming/Network/WireGuardTunnelManager.cs` (linhas 243 e 268)**:
   - O record `WireGuardConfigAuditResult` declarou o parâmetro posicional como `bool AltersDefaultWindowsRoute`, enquanto as chamadas nomeadas nas linhas 243 e 268 passaram `AltersDefaultRoute: ...`.
2. **Aviso NuGet `NU1510` em `src/PCControlUltimate.Windows/PCControlUltimate.Windows.csproj`**:
   - O projeto referencia explicitamente `<PackageReference Include="Microsoft.Win32.Registry" Version="5.0.0" />`, que já faz parte nativamente do framework base `.NET 10.0` (`Microsoft.NETCore.App`).

---

## 2. Alterações Técnicas Planejadas

### Etapa 1 — Correção de `WireGuardConfigAuditResult` e Detecção de Rota Padrão IPv4/IPv6 (`CS1739`)
- **Arquivo**: `/src/PCControlUltimate.Gaming/Network/WireGuardTunnelManager.cs`
- Padronizar o parâmetro do construtor primário de `WireGuardConfigAuditResult` para `bool AltersDefaultRoute`.
- Adicionar propriedades explícitas e calculadas no record `WireGuardConfigAuditResult`:
  - `public bool AltersDefaultWindowsRoute => AltersDefaultRoute;` (mantendo total compatibilidade com código e testes existentes).
  - `public bool AltersIpv4DefaultRoute { get; init; }` (verdadeiro quando `AllowedIPs` contém `0.0.0.0/0`, `0.0.0.0/1` ou `128.0.0.0/1`).
  - `public bool AltersIpv6DefaultRoute { get; init; }` (verdadeiro quando `AllowedIPs` contém `::/0`, `::/1` ou `8000::/1`).
- Atualizar `ValidateAndAuditConfig` para calcular separadamente `altersIpv4Default` e `altersIpv6Default`, preenchendo `AltersDefaultRoute: altersDefaultRoute` e as propriedades `AltersIpv4DefaultRoute` e `AltersIpv6DefaultRoute` em todas as instanciações de `WireGuardConfigAuditResult`, além de detalhar na explicação (`RouteImpactExplanationPt` / `RouteImpactExplanationEn`) se o impacto afeta IPv4 (`0.0.0.0/0`), IPv6 (`::/0`) ou ambos.

### Etapa 2 — Remoção da Referência Redundante `Microsoft.Win32.Registry` (`NU1510`)
- **Arquivo**: `/src/PCControlUltimate.Windows/PCControlUltimate.Windows.csproj`
- Remover `<PackageReference Include="Microsoft.Win32.Registry" Version="5.0.0" />`, utilizando a API `Microsoft.Win32.Registry` nativa do `.NET 10.0` e preservando integralmente as versões auditadas do SQLite e DI (`Microsoft.EntityFrameworkCore.Sqlite 9.0.4`, `Microsoft.Extensions.DependencyInjection 9.0.4`, `SQLitePCLRaw.bundle_e_sqlite3 3.0.5` e `SQLitePCLRaw.core 3.0.5`).

### Etapa 3 — Ampliação da Suíte de Testes Automatizados xUnit
- **Arquivo**: `/tests/PCControlUltimate.Tests/GameNetworkAcceleratorTests.cs`
- Adicionar testes unitários cobrindo todos os cenários exigidos para `WireGuardConfigAuditResult`:
  1. `AllowedIPs` com `0.0.0.0/0` (rota padrão IPv4 isolada) — bloqueado por padrão (`IsValid == false`, `AltersDefaultRoute == true`, `AltersIpv4DefaultRoute == true`, `AltersIpv6DefaultRoute == false`).
  2. `AllowedIPs` com `::/0` (rota padrão IPv6 isolada) — bloqueado por padrão (`IsValid == false`, `AltersDefaultRoute == true`, `AltersIpv4DefaultRoute == false`, `AltersIpv6DefaultRoute == true`).
  3. `AllowedIPs` com `0.0.0.0/0, ::/0` (ambas as rotas padrão presentes) — bloqueado por padrão e permitido apenas quando `allowDefaultRouteOverride: true`, sempre sinalizando `AltersDefaultRoute == true`, `AltersIpv4DefaultRoute == true`, `AltersIpv6DefaultRoute == true` e `IsSelectiveGameTargetRouting == false`.
  4. `AllowedIPs` sem rota padrão (ex.: sub-redes específicas de servidores de jogos `155.133.248.0/24, 2001:db8::/32`) — aprovado (`IsValid == true`, `AltersDefaultRoute == false`, `AltersIpv4DefaultRoute == false`, `AltersIpv6DefaultRoute == false`, `IsSelectiveGameTargetRouting == true`).
  5. Validação de host/porta, disponibilidade do cliente WireGuard e histórico de medições de rota.

### Etapa 4 — Auditoria de Segurança, CI Windows x64 e Relatório Final (Seções A–G)
- Revisar `.github/workflows/windows-ci.yml` garantindo todos os passos obrigatórios (Restore, Vulnerability Audit, Build Release x64, xUnit Tests, Self-Contained Publish `win-x64`, verificação de DLLs/EXE/SQLite, criação do ZIP e upload do artefato).
- Entregar o Relatório Técnico Final estruturado nas seções **A a G** conforme especificado.
