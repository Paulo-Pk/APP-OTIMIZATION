# Plano de Implementação — Correção das 3 Falhas de Teste Restantes (99/99)

## 1. Diagnóstico das 3 Falhas no GitHub Actions
A execução anterior do workflow Windows x64 compilou toda a solução em `Release | x64` sem erros e aprovou 96 de 99 testes. As 3 falhas restantes têm causas exatas identificadas:

1. **`RegistryStateLedger_RestoreWithoutTrustedHistory_MustBeBlocked` (`RegistryOptimizerTests.cs` linha 226)**:
   - `RegistryStateLedger.ResolveTrustedRestoreEntry` retorna `ErrorCode = "NO_TRUSTED_BACKEND_HISTORY"` e `ErrorReason = "NO_TRUSTED_BACKEND_HISTORY: Restauração bloqueada: ..."`.
   - Porém, em `RegistryOptimizer.RestoreCatalogTweakFromTrustedSource` (linha 743), o método atribuía apenas `ledgerValidation.ErrorMessage` (a frase localizada em português sem o código `NO_TRUSTED_BACKEND_HISTORY`) ao `RegistryOperationResult.ErrorMessage`.
2. **`ValidateHostHeader_EnforcesStrictLoopbackHost` — 2 casos (`OptimizationSafetyTests.cs` linhas 65–66)**:
   - O teste instancia `new PrivilegedAgentServer(19878)`, mas passava `[InlineData("127.0.0.1:5005", true)]` e `[InlineData("localhost:5005", true)]` nos parâmetros da teoria. Como o servidor valida estritamente a porta configurada (`19878`), ambos retornavam `false`.

---

## 2. Alterações Planejadas

### Etapa 1 — Contrato Estável de Erro em `RegistryOperationResult` e `RegistryOptimizer.cs`
- **Arquivo**: `/src/PCControlUltimate.Windows/Services/RegistryOptimizer.cs`
- Adicionar a propriedade opcional `string? ErrorCode = null` em `RegistryOperationResult`.
- Em `RestoreCatalogTweakFromTrustedSource` (linhas 740–749), propagar `ledgerValidation.ErrorCode` para `RegistryOperationResult.ErrorCode` e usar `ledgerValidation.ErrorReason` (que inclui o identificador estável `NO_TRUSTED_BACKEND_HISTORY: ...`) em `ErrorMessage`, garantindo que qualquer consumidor ou teste verifique tanto `result.ErrorCode == "NO_TRUSTED_BACKEND_HISTORY"` quanto `result.ErrorMessage`.
- Em `/tests/PCControlUltimate.Tests/RegistryOptimizerTests.cs`, validar explicitamente `Assert.Equal("NO_TRUSTED_BACKEND_HISTORY", validation.ErrorCode)`, `Assert.Equal("NO_TRUSTED_BACKEND_HISTORY", restoreResult.ErrorCode)` e `Assert.Contains("NO_TRUSTED_BACKEND_HISTORY", restoreResult.ErrorMessage)`.

### Etapa 2 — Correção e Ampliação do Teste `ValidateHostHeader_EnforcesStrictLoopbackHost`
- **Arquivo**: `/tests/PCControlUltimate.Tests/OptimizationSafetyTests.cs`
- Atualizar os casos `[InlineData]` do teste `ValidateHostHeader_EnforcesStrictLoopbackHost` (servidor instanciado na porta `19878`) para cobrir todos os cenários exigidos sem alterar nem enfraquecer `PrivilegedAgentServer.ValidateHostHeader`:
  - `"127.0.0.1:19878"` -> `true` (loopback IPv4 na porta ativa)
  - `"localhost:19878"` -> `true` (loopback hostname na porta ativa)
  - `"127.0.0.1"` -> `true` (loopback sem porta explícita, permitido pelo contrato atual)
  - `"localhost"` -> `true` (loopback sem porta explícita, permitido pelo contrato atual)
  - `"127.0.0.1:5005"` -> `false` (porta incorreta rejeitada)
  - `"localhost:5005"` -> `false` (porta incorreta rejeitada)
  - `"evil.attacker.com:19878"` -> `false` (domínio externo / DNS rebinding rejeitado)
  - `""` -> `false` (vazio rejeitado)
  - `null` -> `false` (nulo rejeitado)

### Etapa 3 — Verificação de Integridade de `WireGuardConfigAuditResult`, Dependências e CI
- Confirmar a permanência de `WireGuardConfigAuditResult` (`AltersDefaultRoute`, `AltersDefaultWindowsRoute`, `AltersIpv4DefaultRoute`, `AltersIpv6DefaultRoute`), a ausência de `Microsoft.Win32.Registry` em `PCControlUltimate.Windows.csproj` e o alinhamento de `EF Core 9.0.4`, `DI 9.0.4` e `SQLitePCLRaw 3.0.5`.
