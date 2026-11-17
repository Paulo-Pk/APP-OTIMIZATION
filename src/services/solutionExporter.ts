import JSZip from 'jszip';
import { dotnetSolutionFiles } from '../data/solutionFiles';

export async function exportCompleteSolutionZip(): Promise<Blob> {
  const zip = new JSZip();

  // Root README
  const rootReadme = `# PC CONTROL ULTIMATE
## Solução Visual Studio .NET 10 LTS x64

Este pacote contém o código-fonte integral e a estrutura da solução para o software profissional de otimização gamer **PC CONTROL ULTIMATE**, em conformidade com as diretrizes do Master Development Prompt.

### Estrutura dos Projetos
1. **PCControlUltimate.App** — Interface gráfica WPF (MVVM), temas gamer escuro com neon roxo, gráficos de telemetria.
2. **PCControlUltimate.Core** — Entidades de domínio (OptimizationAction, SensorSnapshot, GameConfig), interfaces e regras.
3. **PCControlUltimate.Infrastructure** — Persistência SQLite local via Entity Framework Core 10 e logs com Serilog.
4. **PCControlUltimate.Hardware** — Wrapper de baixo nível para LibreHardwareMonitorLib com tratamento resiliente de sensores ausentes.
5. **PCControlUltimate.Windows** — Integração com Win32 P/Invoke, Registro do Windows, esquemas de energia powercfg e processos.
6. **PCControlUltimate.Gaming** — Detecção de jogos (Steam, Epic, Xbox), perfis e integração com Intel PresentMon.
7. **PCControlUltimate.Diagnostics** — Auditor térmico de GPU (Hotspot Delta ΔT), diagnóstico de estrangulamento térmico.
8. **PCControlUltimate.Security** — Bridge de elevação UAC, isolamento IPC e políticas estritas de reversão de tweaks.
9. **PCControlUltimate.Tests** — Testes unitários automatizados xUnit.

### Como Compilar no Windows
1. Pré-requisitos:
   - Windows 10 (Build 19041+) ou Windows 11.
   - .NET 10 SDK (LTS) instalado.
   - Visual Studio 2025 ou Visual Studio Code com extensão C# Dev Kit.
2. Compilação rápida via PowerShell:
   \`\`\`powershell
   cd PCControlUltimate
   powershell -ExecutionPolicy Bypass -File scripts/build.ps1
   \`\`\`
3. Para abrir no Visual Studio:
   Abra o arquivo \`PCControlUltimate.sln\` e selecione a configuração **Release | x64**.
`;

  zip.file('README.md', rootReadme);

  // Add all project files
  for (const file of dotnetSolutionFiles) {
    zip.file(file.path, file.content);
  }

  // Generate binary zip
  const blob = await zip.generateAsync({
    type: 'blob',
    compression: 'DEFLATE',
    compressionOptions: { level: 9 },
  });

  return blob;
}

export function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  setTimeout(() => URL.revokeObjectURL(url), 10000);
}
