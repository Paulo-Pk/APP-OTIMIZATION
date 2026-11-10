# PC Control Ultimate — Enterprise Windows Workstation & Gaming Telemetry Suite

Aplicativo desktop nativo para Windows 10/11 construído em **C# / .NET 10 LTS (WPF + MVVM)** para monitoramento de hardware em tempo real, auditoria térmica de GPU, telemetria de frametime com Intel PresentMon, otimização auditável do Registro do Windows via catálogo fechado, backups transacionais com verificação SHA-256, limpeza segura de caches e diagnóstico de rede.

> **Nota Arquitetural:** A aplicação principal deste repositório é o aplicativo desktop nativo **WPF (`PCControlUltimate.sln`)**. O projeto React + TypeScript (`src/` na raiz web) é mantido como referência visual/funcional e cliente secundário compatível com o mesmo Agente Local do Windows.

---

## 1. Arquitetura Atual do Projeto

A solução `PCControlUltimate.sln` é dividida em 9 projetos modulares com responsabilidade única:

| Projeto | Caminho | Responsabilidade |
| :--- | :--- | :--- |
| **PCControlUltimate.App** | `src/PCControlUltimate.App/` | Aplicativo desktop nativo **WPF (.NET 10 Windows)** com arquitetura MVVM (`CommunityToolkit.Mvvm`), navegação dinâmica entre os 9 módulos e internacionalização reativa (PT-BR padrão / EN-US). |
| **PCControlUltimate.Core** | `src/PCControlUltimate.Core/` | Contratos, interfaces (`IHardwareMonitorService`, `IGpuHealthService`) e modelos de domínio imutáveis. |
| **PCControlUltimate.Infrastructure** | `src/PCControlUltimate.Infrastructure/` | Persistência local SQLite (`AppDbContext`), `AgentClientService` com leitura de token DPAPI e cabeçalhos anti-replay (`X-PCControl-Client`, `X-PCControl-Timestamp`, `X-PCControl-Nonce`). |
| **PCControlUltimate.Hardware** | `src/PCControlUltimate.Hardware/` | Integração real com **LibreHardwareMonitorLib** para leitura de sensores físicos de CPU, GPU (Core, Hotspot, VRAM, Fan RPM, Power) e RAM, sem valores simulados. |
| **PCControlUltimate.Windows** | `src/PCControlUltimate.Windows/` | `PrivilegedAgentServer` (`127.0.0.1:5005`), `RegistryOptimizer` (catálogo fechado de `TweakId`), `RegistrySnapshotEngine` (backups JSON canônicos + SHA-256 + rollback), `DiskCleanerEngine` (Dry-Run + proteção contra Symlinks/Reparse Points), `NetworkDiagnosticsService` (ICMP, TCP, DNS, Throughput) e `DriverScannerService` (WMI `Win32_PnPSignedDriver` somente leitura). |
| **PCControlUltimate.Gaming** | `src/PCControlUltimate.Gaming/` | Integração com **Intel PresentMon** via `ProcessStartInfo.ArgumentList` seguro, validação estrita de nome de processo e cálculo de FPS médio, 1% Low, 0.1% Low e frametime (ms). |
| **PCControlUltimate.Diagnostics** | `src/PCControlUltimate.Diagnostics/` | `GpuThermalAuditor` para análise contextualizada do gradiente térmico Core vs. Hotspot e tratamento seguro de sensores ausentes (`SensorUnavailable`). |
| **PCControlUltimate.Security** | `src/PCControlUltimate.Security/` | `IntegrityVerifier` para cálculo de hash SHA-256 canônico e proteção do token de sessão via **Windows DPAPI (`DataProtectionScope.CurrentUser`)** + **ACL NTFS** restrita ao SID do usuário atual. |
| **PCControlUltimate.Tests** | `tests/PCControlUltimate.Tests/` | Suíte de testes automatizados **xUnit** cobrindo segurança do Registro, integridade de snapshots, autenticação/anti-replay do agente, validação do PresentMon e segurança do limpador de disco. |

---

## 2. Dependências Necessárias

### .NET / NuGet
- **.NET 10.0 SDK** (`net10.0-windows` para os projetos nativos e de testes)
- `CommunityToolkit.Mvvm` (8.4.0)
- `Microsoft.Extensions.DependencyInjection` (10.0.0)
- `Microsoft.EntityFrameworkCore.Sqlite` (10.0.0)
- `LibreHardwareMonitorLib` (0.9.4)
- `System.Management` (10.0.0) — consultas WMI (`Win32_PnPSignedDriver`)
- `System.Security.Cryptography.ProtectedData` (10.0.0) — DPAPI
- `xunit` (2.9.3) e `Microsoft.NET.Test.Sdk` (17.12.0)

### Ferramentas Externas Opcionais
- **Intel PresentMon CLI** (`PresentMon.exe` no `PATH` ou em `%ProgramFiles%\Intel\PresentMon\PresentMon.exe`) — necessário apenas para sessões de captura de frametime/FPS em jogos. Caso não esteja instalado, o módulo exibe aviso explícito e não simula métricas.

---

## 3. Requisitos do Windows

- **Sistema Operacional:** Windows 10 (21H2 ou superior) ou Windows 11 (x64).
- **Arquitetura:** x64.
- **Runtime:** .NET 10.0 Desktop Runtime (Windows x64) — ou compilação `self-contained`.
- **Privilégios:** Execução como Administrador (UAC) recomendada para leitura de sensores de baixo nível (Ring0 / LibreHardwareMonitor), captura ETW do PresentMon e escrita em chaves `HKEY_LOCAL_MACHINE` do catálogo fechado.

---

## 4. Como Compilar

Em um terminal PowerShell ou Prompt de Comando no Windows com o **.NET 10 SDK** instalado:

```powershell
# 1. Restaurar pacotes NuGet da solução completa
dotnet restore PCControlUltimate.sln

# 2. Compilar toda a solução em modo Release
dotnet build PCControlUltimate.sln -c Release

# 3. Executar a suíte de testes automatizados xUnit
dotnet test PCControlUltimate.sln -c Release --no-build --verbosity normal
```

---

## 5. Como Executar em Desenvolvimento e Produção

### Aplicativo Desktop WPF (Principal)
```powershell
# Executar em modo de desenvolvimento
dotnet run --project src/PCControlUltimate.App/PCControlUltimate.App.csproj

# Executar binário compilado em Release
.\src\PCControlUltimate.App\bin\Release\net10.0-windows\PCControlUltimate.App.exe
```

### Interface Web de Referência (Opcional)
```bash
npm install
npm run dev
```

---

## 6. Como Iniciar o Agente Local e Fluxo de Segurança

1. Ao iniciar o `PCControlUltimate.App.exe`, o `PrivilegedAgentServer` é inicializado automaticamente em background escutando exclusivamente em **`http://127.0.0.1:5005/`**.
2. Na inicialização, o agente gera um token criptográfico de 256 bits (`RandomNumberGenerator.GetBytes(32)`) exclusivo para a sessão atual.
3. O token é gravado em `%LOCALAPPDATA%\PCControlUltimate\agent_session.token` criptografado com **Windows DPAPI (`DataProtectionScope.CurrentUser`)** e protegido por **ACL NTFS** restrita ao SID do usuário logado (`SaveProtectedSessionTokenWithReport`).
4. O endpoint de token **nunca** é exposto via HTTP. O `AgentClientService` lê e descriptografa o arquivo local via DPAPI.
5. Todas as requisições ao agente exigem:
   - `Authorization: Bearer <token>` validado em tempo constante (`CryptographicOperations.FixedTimeEquals`).
   - `Host: 127.0.0.1:5005` ou `localhost:5005`.
   - Validação de `Origin` (sem CORS curinga `*`) ou identificação explícita do cliente local (`X-PCControl-Client`) acompanhada de timestamp UTC (`X-PCControl-Timestamp`) e nonce único anti-replay (`X-PCControl-Nonce`).
   - Payload máximo de 64 KB e rate limiting por minuto.

---

## 7. Operações que Exigem UAC (Administrador) e Por Quê

O manifesto `src/PCControlUltimate.App/app.manifest` solicita elevação UAC (`requireAdministrator`) pelas seguintes razões técnicas:

| Operação | Exige UAC? | Justificativa Técnica |
| :--- | :---: | :--- |
| **Sensores Físicos (LibreHardwareMonitor)** | **Sim** | Leitura de MSRs da CPU, sensores térmicos de GPU (Hotspot/VRAM) e controladores Super I/O requer acesso a drivers de kernel/ WMI privilegiado. |
| **Ajustes do Catálogo em `HKLM`** | **Sim** | Chaves como `HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl` (`Win32PrioritySeparation`) e `Multimedia\SystemProfile` exigem permissão de Administrador para escrita. |
| **Ajustes do Catálogo em `HKCU`** | Não | Chaves do perfil do usuário (`HKCU\System\GameConfigStore`) podem ser lidas e restauradas no contexto do próprio usuário. |
| **Captura ETW (Intel PresentMon)** | **Sim** | A captura de eventos de apresentação DirectX/DXGI em nível de sistema via Event Tracing for Windows (ETW) requer privilégio administrativo ou associação ao grupo *Performance Log Users*. |
| **Limpeza de `%WINDIR%\Temp` e `SoftwareDistribution\Download`** | **Sim** | Diretórios temporários de sistema e cache do Windows Update pertencem ao `SYSTEM` / `Administrators`. |
| **Limpeza de `%LOCALAPPDATA%\Temp` e Cache DirectX** | Não | Pertencem ao perfil do usuário atual. |
| **Inventário de Drivers (WMI) e Diagnóstico de Rede** | Não | Consultas somente leitura em `Win32_PnPSignedDriver` e testes ICMP/TCP/DNS/HTTPS funcionam sem elevação. |

---

## 8. Como Gerar Build Release x64 e Instalador Windows

Utilize o script automatizado em PowerShell ou os comandos diretos abaixo:

```powershell
# Publicação Release win-x64 via script incluído no repositório
powershell -ExecutionPolicy Bypass -File .\scripts\publish-release-x64.ps1
```

Ou manualmente:
```powershell
dotnet publish src/PCControlUltimate.App/PCControlUltimate.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o publish/win-x64
```

### Gerar Instalador com Inno Setup
1. Instale o [Inno Setup 6](https://jrsoftware.org/isinfo.php).
2. Após gerar a pasta `publish/win-x64`, compile o script de instalador:
```powershell
iscc .\installer\PCControlUltimate.iss
```
O instalador executável será gerado com suporte a solicitação de UAC, criação de atalhos e desinstalação limpa.

---

## 9. Game Network Accelerator

O módulo experimental de aceleração de rede oferece medição TCP repetida, traceroute ICMP, detecção heurística de jogos e integração opcional com perfis WireGuard compatíveis. A instalação do túnel exige confirmação e UAC; as rotas seguem o campo `AllowedIPs` e podem afetar todo o Windows. Roteamento seletivo por processo/jogo e integrações proprietárias com provedores comerciais ainda não estão implementados. Consulte [docs/game-network-accelerator.md](docs/game-network-accelerator.md) para escopo, limitações e operação segura.

## 10. Limitações Conhecidas

1. **Integridade de Snapshots (SHA-256 vs. Assinatura Digital):** Os backups do Registro em `%LOCALAPPDATA%\PCControlUltimate\Backups` possuem hash SHA-256 canônico verificado imediatamente após a gravação e antes de qualquer restauração. O SHA-256 garante **detecção de corrupção ou truncamento de arquivo**, mas não substitui uma assinatura criptográfica assimétrica contra modificação intencional por um atacante que já possua privilégios de escrita no perfil local.
2. **Escopo do Backup do Registro:** O `RegistrySnapshotEngine` realiza backup **exclusivamente das chaves e valores catalogados e suportados pelo aplicativo** (`RegistryOptimizer.Catalog`). Ele não realiza dump completo da colmeia (`hive`) do Windows; para backup integral do sistema operacional, utilize um Ponto de Restauração do Sistema do Windows (`Restore-Computer`).
3. **Sensores Dependentes de Hardware:** Determinadas GPUs integradas ou notebooks OEM bloqueiam a exposição do sensor de *GPU Hotspot* ou *VRAM Temperature*. Nesses casos, o aplicativo exibe explicitamente `N/D` / `Sensor indisponível` e não infere degradação térmica.
4. **Driver Center Somente Leitura:** Por segurança operacional e estabilidade do sistema operacional, o módulo Driver Center **apenas inventaria** drivers instalados e seu status de assinatura WHQL via WMI (`Win32_PnPSignedDriver`), nunca baixando ou instalando drivers de fontes externas.

---

## 10. Funcionalidades Não Implementadas / Fora de Escopo

- **Download e Instalação Automática de Drivers:** Deliberadamente bloqueado por política de segurança para evitar instalação de pacotes incompatíveis ou não assinados.
- **Edição Livre de Chaves Arbitrárias do Registro (`regedit` genérico):** Removido por segurança; apenas ajustes presentes em `RegistryOptimizer.Catalog` com tipos e valores estritamente validados pelo backend podem ser aplicados ou restaurados.
- **Overclocking / Alteração de Tensão (Undervolting/Overvolting):** O aplicativo não modifica frequências, curvas de tensão ou BIOS/VBIOS de hardware.
