# PC Control Ultimate — sincronização com Google AI Studio

Este arquivo registra uma atualização segura para que integrações conectadas ao GitHub possam detectar um novo commit.

## Branch de trabalho

`integration/selective-mainline-2026-11-16`

## Estado da validação

- A atualização de código desta branch ainda precisa de validação no ambiente local/AI Studio.
- Não declarar build, lint ou testes como aprovados sem executar os comandos e conferir a saída.
- O script de validação disponível é `scripts/test-local.ps1`.
- A compilação Windows/.NET depende da disponibilidade do SDK .NET no ambiente utilizado.

## Cuidados

- Não fazer merge automático em `main`.
- Não iniciar GitHub Actions para evitar consumo de minutos/uso faturável.
- Preservar as alterações existentes do PC Control Ultimate.
