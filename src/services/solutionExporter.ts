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

export async function downloadCompiledWindowsApp(
  onProgress?: (percent: number, loadedBytes: number, totalBytes: number) => void
): Promise<void> {
  const manifestRes = await fetch('/api/app-package/manifest', { cache: 'no-store' });
  if (!manifestRes.ok) {
    throw new Error(`Não foi possível consultar o pacote no servidor (HTTP ${manifestRes.status}).`);
  }
  const contentType = manifestRes.headers.get('content-type') || '';
  if (!contentType.includes('application/json')) {
    throw new Error('O servidor retornou uma resposta inválida ao consultar o pacote compilado.');
  }

  const manifest = (await manifestRes.json()) as {
    available: boolean;
    filename: string;
    totalBytes: number;
    chunkSizeBytes: number;
    totalChunks: number;
    error?: string;
  };

  if (!manifest.available || !manifest.totalChunks || !manifest.totalBytes) {
    throw new Error(manifest.error || 'Pacote compilado indisponível.');
  }

  const chunks: ArrayBuffer[] = [];
  let loadedBytes = 0;

  for (let i = 0; i < manifest.totalChunks; i++) {
    const chunkRes = await fetch(`/api/app-package/chunk/${i}`, { cache: 'no-store' });
    if (!chunkRes.ok) {
      throw new Error(`Falha ao baixar parte ${i + 1} de ${manifest.totalChunks} (HTTP ${chunkRes.status}).`);
    }
    const chunkType = chunkRes.headers.get('content-type') || '';
    if (chunkType.includes('text/html')) {
      throw new Error('Download interceptado por página HTML inesperada.');
    }

    const buf = await chunkRes.arrayBuffer();
    if (i === 0) {
      const header = new Uint8Array(buf.slice(0, 4));
      if (header[0] !== 0x50 || header[1] !== 0x4b || header[2] !== 0x03 || header[3] !== 0x04) {
        throw new Error('Cabeçalho ZIP inválido detectado durante o download.');
      }
    }

    chunks.push(buf);
    loadedBytes += buf.byteLength;
    const percent = Math.min(100, Math.round((loadedBytes / manifest.totalBytes) * 100));
    onProgress?.(percent, loadedBytes, manifest.totalBytes);
  }

  if (loadedBytes !== manifest.totalBytes) {
    throw new Error(`Tamanho incompleto: recebido ${loadedBytes} bytes de ${manifest.totalBytes} bytes.`);
  }

  const blob = new Blob(chunks, { type: 'application/zip' });
  downloadBlob(blob, manifest.filename || 'PCControlUltimate-win-x64-Pronto.zip');
}
