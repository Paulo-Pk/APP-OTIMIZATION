[CmdletBinding()]
param(
    [switch]$SkipFrontend,
    [switch]$SkipDotNet,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Invoke-CheckedStep {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][scriptblock]$Command)
    Write-Host ""
    Write-Host "=== $Name ===" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Etapa '$Name' falhou com código $LASTEXITCODE."
    }
}

if (-not $SkipFrontend) {
    if (-not (Get-Command bun -ErrorAction SilentlyContinue)) {
        throw "Bun não encontrado. Instale Bun e abra um novo PowerShell."
    }
    Invoke-CheckedStep "Instalar dependências do frontend" { bun install --frozen-lockfile }
    Invoke-CheckedStep "Verificar TypeScript" { bun run lint }
    Invoke-CheckedStep "Compilar frontend" { bun run build }
}

if (-not $SkipDotNet) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw ".NET SDK não encontrado. Instale o .NET 10 SDK e abra um novo PowerShell."
    }
    Invoke-CheckedStep "Restaurar solução .NET" { dotnet restore PCControlUltimate.sln }
    Invoke-CheckedStep "Compilar solução Release x64" { dotnet build PCControlUltimate.sln -c Release -p:Platform=x64 --no-restore }
    Invoke-CheckedStep "Executar testes automatizados" { dotnet test PCControlUltimate.sln -c Release -p:Platform=x64 --no-build --logger "console;verbosity=normal" }

    if ($Publish) {
        Invoke-CheckedStep "Publicar aplicativo Windows x64" {
            dotnet publish src/PCControlUltimate.App/PCControlUltimate.App.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -o artifacts/publish/win-x64
        }
        $exe = Join-Path $repoRoot "artifacts/publish/win-x64/PCControlUltimate.App.exe"
        if (-not (Test-Path $exe)) {
            throw "Publicação terminou sem gerar PCControlUltimate.App.exe."
        }
        Write-Host ""
        Write-Host "Executável pronto: $exe" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "Todas as etapas selecionadas foram concluídas com sucesso." -ForegroundColor Green
