import express, { Request, Response } from 'express';
import { createServer as createViteServer } from 'vite';
import dotenv from 'dotenv';
import path from 'path';
import { fileURLToPath } from 'url';
import { GoogleGenAI } from '@google/genai';

dotenv.config();

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

async function startServer() {
  const app = express();
  const PORT = process.env.PORT ? parseInt(process.env.PORT, 10) : 3000;

  app.use(express.json({ limit: '64kb' }));

  const geminiApiKey = process.env.GEMINI_API_KEY;
  let ai: GoogleGenAI | null = null;
  if (geminiApiKey) {
    ai = new GoogleGenAI({
      apiKey: geminiApiKey,
      httpOptions: {
        headers: {
          'User-Agent': 'aistudio-build',
        },
      },
    });
  }

  // Local server IPC latency check (explicitly distinguished from external internet ping)
  app.get('/api/network/ping', (_req: Request, res: Response) => {
    res.json({
      timestamp: Date.now(),
      measurementType: 'LOCAL_HTTP_LOOPBACK',
      status: 'ok',
    });
  });

  // Real external TCP connection handshake latency, jitter, and packet loss measurement
  app.get('/api/network/measure-targets', async (_req: Request, res: Response) => {
    const net = await import('net');

    interface TargetConfig {
      id: string;
      server: string;
      location: string;
      host: string;
      port: number;
    }

    const targets: TargetConfig[] = [
      { id: 'sp', server: 'Valve CS2 / Steam Cluster (América do Sul)', location: 'São Paulo, BR', host: 'steampowered.com', port: 443 },
      { id: 'santiago', server: 'Riot Games Valorant Cluster (América do Sul)', location: 'Santiago, CL', host: 'riotgames.com', port: 443 },
      { id: 'miami', server: 'Epic Games / AWS us-east-1 (América do Norte)', location: 'Miami / Virginia, EUA', host: 'epicgames.com', port: 443 },
      { id: 'frankfurt', server: 'Europe Gaming Hub (Frankfurt)', location: 'Frankfurt, Alemanha', host: 'cloudflare.com', port: 443 },
      { id: 'cf-dns', server: 'Cloudflare Anycast Node', location: 'Anycast Global', host: '1.1.1.1', port: 53 },
      { id: 'google-dns', server: 'Google Public DNS Node', location: 'Anycast Global', host: '8.8.8.8', port: 53 },
    ];

    const measureHost = (target: TargetConfig) => {
      return new Promise<{
        id: string;
        server: string;
        location: string;
        host: string;
        protocol: string;
        pingMs: number | null;
        jitterMs: number | null;
        packetLossPercent: number;
        samples: number[];
        isSuccess: boolean;
      }>(async (resolve) => {
        const samples: number[] = [];
        let failedCount = 0;
        const totalProbes = 3;

        for (let i = 0; i < totalProbes; i++) {
          const sample = await new Promise<number | null>((probeResolve) => {
            const start = performance.now();
            const socket = new net.Socket();
            socket.setTimeout(1800);

            socket.connect(target.port, target.host, () => {
              const elapsed = performance.now() - start;
              socket.destroy();
              probeResolve(elapsed);
            });

            socket.on('error', () => {
              socket.destroy();
              probeResolve(null);
            });

            socket.on('timeout', () => {
              socket.destroy();
              probeResolve(null);
            });
          });

          if (sample !== null) {
            samples.push(sample);
          } else {
            failedCount++;
          }
          await new Promise((r) => setTimeout(r, 40));
        }

        if (samples.length === 0) {
          return resolve({
            id: target.id,
            server: target.server,
            location: target.location,
            host: target.host,
            protocol: `TCP:${target.port} Handshake`,
            pingMs: null,
            jitterMs: null,
            packetLossPercent: 100,
            samples: [],
            isSuccess: false,
          });
        }

        const avg = samples.reduce((a, b) => a + b, 0) / samples.length;
        const variance = samples.reduce((a, b) => a + Math.pow(b - avg, 2), 0) / samples.length;
        const jitter = Math.sqrt(variance);
        const lossPercent = Math.round((failedCount / totalProbes) * 100);

        resolve({
          id: target.id,
          server: target.server,
          location: target.location,
          host: target.host,
          protocol: `TCP:${target.port} Handshake`,
          pingMs: Math.round(avg),
          jitterMs: parseFloat(jitter.toFixed(1)),
          packetLossPercent: lossPercent,
          samples: samples.map((s) => Math.round(s)),
          isSuccess: true,
        });
      });
    };

    try {
      const results = await Promise.all(targets.map(measureHost));
      res.json({ measurementMethod: 'TCP_HANDSHAKE_RTT', results });
    } catch {
      res.status(500).json({ error: 'Erro ao executar medições TCP reais.' });
    }
  });

  // Real DNS Resolution benchmark endpoint (UDP Port 53)
  app.get('/api/network/dns-benchmark', async (_req: Request, res: Response) => {
    const dns = await import('dns').then((m) => m.promises);
    const results: {
      provider: string;
      dnsIp: string;
      resolveTimeMs: number | null;
      status: string;
      isSuccess: boolean;
    }[] = [];

    const tests = [
      { provider: 'Cloudflare Gaming DNS', dnsIp: '1.1.1.1', host: 'steampowered.com' },
      { provider: 'Google Public DNS', dnsIp: '8.8.8.8', host: 'riotgames.com' },
      { provider: 'Quad9 Security DNS', dnsIp: '9.9.9.9', host: 'epicgames.com' },
    ];

    for (const test of tests) {
      const start = performance.now();
      try {
        const resolver = new dns.Resolver();
        resolver.setServers([test.dnsIp]);
        await resolver.resolve4(test.host);
        const elapsed = performance.now() - start;
        results.push({
          provider: test.provider,
          dnsIp: test.dnsIp,
          resolveTimeMs: Math.max(1, Math.round(elapsed)),
          status: 'Resolvido com Sucesso (UDP 53)',
          isSuccess: true,
        });
      } catch {
        results.push({
          provider: test.provider,
          dnsIp: test.dnsIp,
          resolveTimeMs: null,
          status: 'Não disponível / Bloqueado',
          isSuccess: false,
        });
      }
    }

    res.json({ results });
  });

  // Real External Download Throughput Test against trusted Cloudflare Speed CDN
  // Never presents localhost buffer transfer as internet speed!
  app.get('/api/network/external-throughput', async (_req: Request, res: Response) => {
    try {
      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(), 8000);
      const start = performance.now();

      const response = await fetch('https://speed.cloudflare.com/__down?bytes=2000000', {
        signal: controller.signal,
        headers: { 'Cache-Control': 'no-cache' },
      });

      if (!response.ok) {
        clearTimeout(timeout);
        return res.json({
          available: false,
          downloadMbps: null,
          source: 'speed.cloudflare.com',
          error: `HTTP ${response.status}`,
        });
      }

      const arrayBuf = await response.arrayBuffer();
      clearTimeout(timeout);
      const elapsedSeconds = (performance.now() - start) / 1000;

      if (elapsedSeconds <= 0 || arrayBuf.byteLength === 0) {
        return res.json({
          available: false,
          downloadMbps: null,
          source: 'speed.cloudflare.com',
        });
      }

      const bits = arrayBuf.byteLength * 8;
      const mbps = parseFloat((bits / (elapsedSeconds * 1_000_000)).toFixed(1));

      return res.json({
        available: true,
        downloadMbps: mbps,
        bytesTransferred: arrayBuf.byteLength,
        durationMs: Math.round(elapsedSeconds * 1000),
        source: 'https://speed.cloudflare.com/__down',
      });
    } catch (err: any) {
      return res.json({
        available: false,
        downloadMbps: null,
        source: 'speed.cloudflare.com',
        error: err?.message || 'Origem externa inacessível neste ambiente',
      });
    }
  });

  // Display hardware refresh rate query (Native agent required for Win32 EnumDisplaySettingsW)
  app.get('/api/display/info', (_req: Request, res: Response) => {
    res.json({
      available: false,
      nativeRefreshRateHz: null,
      monitorName: 'Não detectado',
      resolution: 'Não detectado',
      message: 'Não detectado (requer Agente Windows com EnumDisplaySettingsW Win32)',
    });
  });

  // Intelligent PC Assistant endpoint
  app.post('/api/ai/diagnose', async (req: Request, res: Response) => {
    try {
      const { hardwareData, userQuestion, diagnosticLogs } = req.body;

      if (!geminiApiKey || !ai) {
        const cpuDesc = hardwareData?.cpu?.name || 'Não disponível (Agente desconectado)';
        const gpuDesc = hardwareData?.gpu?.name || 'Não disponível (Agente desconectado)';
        return res.status(200).json({
          success: true,
          isOffline: true,
          response: `[MODO OFFLINE LOCAL - Sem chave de API Gemini configurada]
Diagnóstico Técnico Baseado no Estado Real do PC Control Ultimate:
• CPU detectada: ${cpuDesc}
• GPU detectada: ${gpuDesc}
• Nota de Segurança: O PC Control Ultimate nunca simula leituras de sensores nem retorna falso sucesso quando o Agente Windows não está autenticado.
• Recomendações Técnicas:
  1. Crie um Backup dos Ajustes Suportados na aba Restore Center antes de aplicar mudanças.
  2. Utilize o catálogo autorizado de Registro no Windows Tweaks para otimizar MMCSS e Game DVR com verificação pós-escrita.`,
        });
      }

      const systemPrompt = `Você é o PC CONTROL ULTIMATE INTELLIGENT ASSISTANT, especialista em engenharia de sistemas Windows 10/11, telemetria real LibreHardwareMonitor e análise de frametime Intel PresentMon.
Regras:
1. Responda no idioma solicitado pelo usuário com rigor técnico.
2. Nunca invente leituras de temperatura, clock ou FPS caso os sensores estejam marcados como indisponíveis (0 ou vazios).
3. Nunca recomende desativar Windows Defender, Firewall ou atualizações críticas de segurança.`;

      const prompt = `TELEMETRIA ATUAL DO SISTEMA:
${JSON.stringify(hardwareData, null, 2)}

DIAGNÓSTICOS E LOGS:
${JSON.stringify(diagnosticLogs || [], null, 2)}

PERGUNTA DO USUÁRIO:
${userQuestion || 'Analise o estado atual do sistema e recomende ações seguras.'}`;

      const response = await ai.models.generateContent({
        model: 'gemini-2.5-flash',
        contents: prompt,
        config: {
          systemInstruction: systemPrompt,
        },
      });

      return res.json({
        success: true,
        isOffline: false,
        response: response.text || 'Nenhuma resposta gerada.',
      });
    } catch {
      return res.status(200).json({
        success: true,
        isOffline: true,
        response:
          'Serviço de IA indisponível no momento. Verifique a conexão com o Agente Windows para diagnósticos locais.',
      });
    }
  });

  if (process.env.NODE_ENV === 'production') {
    app.use(express.static(path.resolve(__dirname, 'dist')));
    app.get('*', (_req: Request, res: Response) => {
      res.sendFile(path.resolve(__dirname, 'dist', 'index.html'));
    });
  } else {
    const isAiStudioPreview =
      process.env.DISABLE_HMR === 'true' ||
      Boolean(process.env.K_SERVICE) ||
      Boolean(process.env.APP_URL?.includes('.run.app')) ||
      Boolean(process.env.GOOGLE_RUNTIME);

    const vite = await createViteServer({
      server: {
        middlewareMode: true,
        hmr: !isAiStudioPreview,
        watch: isAiStudioPreview ? null : {},
      },
      appType: 'spa',
    });
    app.use(vite.middlewares);
  }

  app.listen(PORT, '0.0.0.0', () => {
    console.log(`[PC CONTROL ULTIMATE] Server running on http://localhost:${PORT}`);
  });
}

startServer().catch((err) => {
  console.error('[PC CONTROL ULTIMATE] Fatal server start error:', err);
});
