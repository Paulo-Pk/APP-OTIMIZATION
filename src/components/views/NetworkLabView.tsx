import React, { useState } from 'react';
import {
  Wifi,
  Play,
  Globe,
  Server,
  Activity,
  AlertCircle,
  ShieldCheck,
} from 'lucide-react';
import { useI18n } from '../../i18n';
import { nativeBridge } from '../../services/nativeBridge';

interface TargetMeasurement {
  id: string;
  server: string;
  location: string;
  host: string;
  protocol?: string;
  pingMs: number | null;
  jitterMs: number | null;
  tcpFailurePercent: number;
  samples: number[];
  isSuccess: boolean;
}

interface DnsMeasurement {
  provider: string;
  dnsIp: string;
  resolveTimeMs: number | null;
  status: string;
  isSuccess: boolean;
}

export const NetworkLabView: React.FC = () => {
  const { t } = useI18n();
  const [isRunning, setIsRunning] = useState<boolean>(false);
  const [localPingMs, setLocalPingMs] = useState<number | null>(null);
  const [targetResults, setTargetResults] = useState<TargetMeasurement[]>([]);
  const [avgExternalPing, setAvgExternalPing] = useState<number | null>(null);
  const [avgExternalJitter, setAvgExternalJitter] = useState<number | null>(null);
  const [downloadMbps, setDownloadMbps] = useState<number | null>(null);
  const [dnsResults, setDnsResults] = useState<DnsMeasurement[]>([]);
  const [hasRun, setHasRun] = useState<boolean>(false);
  const [testError, setTestError] = useState<string | null>(null);

  const runNetworkAudit = async () => {
    setIsRunning(true);
    setHasRun(true);
    setTestError(null);
    setTargetResults([]);
    setDnsResults([]);
    setLocalPingMs(null);
    setAvgExternalPing(null);
    setAvgExternalJitter(null);
    setDownloadMbps(null);

    try {
      // The Windows executable runs inside WebView2 and has no Node/Vite API server.
      // Use the authenticated native agent so the same feature works in the installed app.
      const report = await nativeBridge.runRealNetworkDiagnostics();
      if (!report) {
        setTestError('Não foi possível consultar o agente Windows. Verifique se ele está conectado e autenticado.');
        return;
      }

      const read = (obj: any, key: string) => obj?.[key] ?? obj?.[key.charAt(0).toUpperCase() + key.slice(1)];
      const rawTargets = read(report, 'targetResults');
      const mappedTargets: TargetMeasurement[] = (Array.isArray(rawTargets) ? rawTargets : []).map((item: any, index: number) => {
        const tcpLatency = read(item, 'tcpHandshakeMs');
        const connected = Boolean(read(item, 'isTcpConnected'));
        const targetName = String(read(item, 'targetName') || `Destino ${index + 1}`);
        const host = String(read(item, 'hostOrIp') || 'N/D');
        return {
          id: `${index}-${host}`,
          server: targetName,
          location: 'Endpoint público de referência',
          host,
          protocol: 'TCP',
          pingMs: typeof tcpLatency === 'number' ? tcpLatency : null,
          jitterMs: null,
          tcpFailurePercent: connected ? 0 : 100,
          samples: [],
          isSuccess: connected && typeof tcpLatency === 'number',
        };
      });
      setTargetResults(mappedTargets);

      const icmpLatency = read(report, 'averageIcmpLatencyMs');
      const tcpLatency = read(report, 'averageTcpHandshakeMs');
      const jitter = read(report, 'jitterMs');
      setLocalPingMs(typeof icmpLatency === 'number' ? Math.round(icmpLatency) : null);
      setAvgExternalPing(typeof tcpLatency === 'number' ? Math.round(tcpLatency) : null);
      setAvgExternalJitter(typeof jitter === 'number' ? Number(jitter.toFixed(1)) : null);

      const rawDns = read(report, 'dnsChecks');
      const mappedDns: DnsMeasurement[] = (Array.isArray(rawDns) ? rawDns : []).map((item: any) => {
        const resolution = read(item, 'resolutionMs');
        return {
          provider: 'DNS do Windows',
          dnsIp: 'Servidor configurado no sistema',
          resolveTimeMs: typeof resolution === 'number' && resolution >= 0 ? resolution : null,
          status: String(read(item, 'status') || read(report, 'dnsState') || 'Não informado'),
          isSuccess: typeof resolution === 'number' && resolution >= 0,
        };
      });
      if (mappedDns.length === 0) {
        const dnsLatency = read(report, 'dnsResolutionMs');
        mappedDns.push({
          provider: 'DNS do Windows',
          dnsIp: 'Servidor configurado no sistema',
          resolveTimeMs: typeof dnsLatency === 'number' && dnsLatency >= 0 ? dnsLatency : null,
          status: String(read(report, 'dnsState') || 'Não informado'),
          isSuccess: typeof dnsLatency === 'number' && dnsLatency >= 0,
        });
      }
      setDnsResults(mappedDns);

      const throughput = await nativeBridge.measureExternalThroughput();
      if (throughput?.available && typeof throughput.downloadMbps === 'number') {
        setDownloadMbps(throughput.downloadMbps);
      } else if (throughput?.error) {
        setTestError(`Diagnóstico de rede concluído, mas a medição de velocidade falhou: ${throughput.error}`);
      }

      if (mappedTargets.length === 0) {
        setTestError('O agente respondeu, mas não retornou destinos de rede para exibir.');
      }
    } catch (error) {
      setTestError(error instanceof Error ? error.message : t.networkLab.measurementUnavailable);
    } finally {
      setIsRunning(false);
    }
  };

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Banner */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <Wifi className="w-4 h-4" /> {t.networkLab.subtitle}
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">{t.networkLab.title}</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl leading-relaxed">
            {t.networkLab.description}
          </p>
        </div>

        <button
          onClick={runNetworkAudit}
          disabled={isRunning}
          className="flex items-center gap-2 px-4 py-2.5 rounded-lg bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white font-bold text-xs uppercase tracking-wider shadow-lg shadow-purple-600/30 transition-all cursor-pointer border border-purple-400/40 disabled:opacity-50"
        >
          <Play className="w-4 h-4 fill-current" />
          <span>{isRunning ? t.networkLab.testing : t.networkLab.startTest}</span>
        </button>
      </div>

      {/* Method Distinction Notice */}
      <div className="p-3.5 rounded-xl bg-[#0e121e] border border-purple-500/20 text-xs flex items-center gap-2.5 text-purple-300">
        <ShieldCheck className="w-4 h-4 text-purple-400 shrink-0" />
        <span>
          Em termos simples: medimos quanto tempo alguns serviços levam para aceitar uma conexão, testamos a velocidade de download e comparamos a resposta dos servidores DNS. O tempo de conexão TCP não é o mesmo que o ping dentro de uma partida.
        </span>
      </div>

      {testError && (
        <div className="p-4 rounded-xl bg-[#261316] border border-rose-500/60 text-xs text-rose-300 flex items-center gap-2.5">
          <AlertCircle className="w-5 h-5 text-rose-400 shrink-0" />
          <span>{testError}</span>
        </div>
      )}

      {/* Primary Metrics Row */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 font-mono tabular-nums">
        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-1">
          <span className="text-[10px] text-slate-500 uppercase block font-semibold font-sans">
            Latência ICMP externa
          </span>
          <div className="text-2xl font-black text-cyan-400">
            {localPingMs !== null
              ? `${localPingMs} ms`
              : hasRun
              ? t.networkLab.measurementUnavailable
              : '—'}
          </div>
          <span className="text-[10px] text-slate-400 block font-sans">Tempo de resposta ICMP dos destinos que responderam</span>
        </div>

        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-1">
          <span className="text-[10px] text-slate-500 uppercase block font-semibold font-sans">
            Resposta de serviços externos
          </span>
          <div className="text-2xl font-black text-emerald-400">
            {avgExternalPing !== null
              ? `${avgExternalPing} ms`
              : hasRun
              ? t.networkLab.measurementUnavailable
              : '—'}
          </div>
          <span className="text-[10px] text-slate-400 block font-sans">Média de conexão (não é ping da partida)</span>
        </div>

        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-1">
          <span className="text-[10px] text-slate-500 uppercase block font-semibold font-sans">
            Variação entre as respostas
          </span>
          <div className="text-2xl font-black text-purple-300">
            {avgExternalJitter !== null
              ? `±${avgExternalJitter} ms`
              : hasRun
              ? t.networkLab.measurementUnavailable
              : '—'}
          </div>
          <span className="text-[10px] text-slate-400 block font-sans">{t.networkLab.jitterDesc}</span>
        </div>

        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-1">
          <span className="text-[10px] text-slate-500 uppercase block font-semibold font-sans">
            Velocidade de download
          </span>
          <div className="text-2xl font-black text-amber-300">
            {downloadMbps !== null
              ? `${downloadMbps} Mbps`
              : hasRun
              ? t.networkLab.measurementUnavailable
              : '—'}
          </div>
          <span className="text-[10px] text-slate-400 block font-sans">Teste de download pela internet</span>
        </div>
      </div>

      <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-3">
        <div className="flex items-center justify-between gap-3"><div><h2 className="text-sm font-bold text-white">Prévia da latência dos serviços</h2><p className="text-[11px] text-slate-400">Comparação visual das conexões medidas nesta verificação</p></div><Activity className="w-5 h-5 text-purple-400" /></div>
        {targetResults.length === 0 ? <div className="h-20 flex items-center justify-center rounded-lg bg-[#0a0c13] border border-slate-800 text-xs text-slate-500">Execute o teste para preencher este gráfico com medições reais</div> : (
          <div className="space-y-3">
            {targetResults.map((target) => {
              const valid = target.pingMs !== null && target.isSuccess;
              const max = Math.max(1, ...targetResults.map((item) => item.pingMs || 0));
              const width = valid ? Math.max(2, (target.pingMs! / max) * 100) : 0;
              return <div key={target.id} className="grid grid-cols-[minmax(110px,1fr)_3fr_auto] items-center gap-3 text-xs"><span className="truncate text-slate-300">{target.server}</span><div className="h-2 rounded-full bg-slate-800 overflow-hidden"><div className="h-full rounded-full bg-purple-500" style={{ width: width + '%' }} /></div><span className="font-mono text-slate-200">{valid ? target.pingMs + ' ms' : 'Sem resposta'}</span></div>;
            })}
          </div>
        )}
      </div>
      {/* Real DNS Resolution Benchmark Section */}
      <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-3">
        <div className="flex items-center justify-between pb-2 border-b border-slate-800 text-xs">
          <div className="flex items-center gap-2">
            <Server className="w-4 h-4 text-purple-400" />
            <span className="font-bold text-white uppercase tracking-wider">
              {t.networkLab.dnsTitle}
            </span>
          </div>
          <span className="text-slate-500 font-mono">{t.networkLab.dnsNodeQuery}</span>
        </div>

        {dnsResults.length === 0 ? (
          <div className="text-center py-6 text-xs text-slate-500">
            {hasRun ? t.networkLab.measurementUnavailable : t.networkLab.awaitingTest}
          </div>
        ) : (
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
            {dnsResults.map((d, i) => (
              <div
                key={i}
                className="p-3.5 rounded-lg bg-[#0a0c13] border border-slate-800 space-y-1.5 text-xs"
              >
                <div className="flex items-center justify-between">
                  <span className="font-bold text-white">{d.provider}</span>
                  <span className="text-[10px] text-slate-500 font-mono">{d.dnsIp}</span>
                </div>
                <div className="flex justify-between items-center text-slate-400 font-mono tabular-nums pt-1">
                  <span>{t.networkLab.resolutionTime}</span>
                  {d.resolveTimeMs !== null ? (
                    <span className="text-emerald-400 font-bold">{d.resolveTimeMs} ms</span>
                  ) : (
                    <span className="text-rose-400 font-semibold">{t.networkLab.measurementUnavailable}</span>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* External Gaming Servers Real Matrix */}
      <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-3">
        <div className="flex items-center justify-between pb-2 border-b border-slate-800 text-xs">
          <div className="flex items-center gap-2">
            <Globe className="w-4 h-4 text-purple-400" />
            <span className="font-bold text-white uppercase tracking-wider">
              {t.networkLab.externalGamingTargetsTitle}
            </span>
          </div>
          <span className="text-slate-500 font-mono">{t.networkLab.destinationLabel}</span>
        </div>

        {targetResults.length === 0 ? (
          <div className="text-center py-8 text-xs text-slate-500">
            {hasRun ? t.networkLab.measurementUnavailable : t.networkLab.awaitingTest}
          </div>
        ) : (
          <div className="divide-y divide-slate-800/80">
            {targetResults.map((target, idx) => (
              <div key={idx} className="py-3.5 flex items-center justify-between gap-4 text-xs">
                <div className="space-y-0.5">
                  <div className="font-bold text-white flex items-center gap-2">
                    <span>{target.server}</span>
                    <span className="text-[10px] font-mono text-slate-500 font-normal">
                      ({target.host})
                    </span>
                  </div>
                  <div className="text-[11px] text-slate-400">{target.location}</div>
                </div>

                <div className="flex items-center gap-6 font-mono tabular-nums">
                  <div>
                    <span className="text-slate-500 block text-[9px] uppercase">Conexão TCP</span>
                    {target.pingMs !== null ? (
                      <span
                        className={`text-sm font-bold ${
                          target.pingMs < 60
                            ? 'text-emerald-400'
                            : target.pingMs < 130
                            ? 'text-amber-400'
                            : 'text-slate-300'
                        }`}
                      >
                        {target.pingMs} ms
                      </span>
                    ) : (
                      <span className="text-xs text-rose-400">{t.networkLab.measurementUnavailable}</span>
                    )}
                  </div>

                  <div>
                    <span className="text-slate-500 block text-[9px] uppercase">Jitter</span>
                    <span className="text-slate-300 text-sm">
                      {target.jitterMs !== null ? `±${target.jitterMs} ms` : '—'}
                    </span>
                  </div>

                  <div>
                    <span className="text-slate-500 block text-[9px] uppercase">
                      Falha de conexão TCP
                    </span>
                    <span
                      className={`text-sm font-bold ${
                        target.tcpFailurePercent === 0
                          ? 'text-emerald-400'
                          : target.tcpFailurePercent < 30
                          ? 'text-amber-400'
                          : 'text-rose-400'
                      }`}
                    >
                      {target.tcpFailurePercent}%
                    </span>
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Game Network Accelerator — Honest Route Comparison & Selective WireGuard AllowedIPs */}
      <div className="p-5 rounded-xl bg-[#0f121b] border border-purple-500/30 space-y-4">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 pb-2 border-b border-slate-800">
          <div>
            <span className="text-xs font-bold text-purple-400 uppercase tracking-wider">
              Game Network Accelerator · WireGuard Selective CIDR Routing
            </span>
            <p className="text-[11px] text-slate-400 mt-0.5">
              Avalia múltiplas amostras antes de recomendar túneis. Se o nó intermediário não reduzir a latência/jitter com estabilidade comprovada, recomenda manter a Rota Direta.
            </p>
          </div>
          <span className="text-xs font-mono text-emerald-400 shrink-0">
            AllowedIPs Seletivo · Zero Kernel Hook
          </span>
        </div>

        <div className="p-3.5 rounded-lg bg-[#0a0c13] border border-amber-500/30 text-xs text-amber-200 leading-relaxed">
          <strong className="text-amber-400">Escopo Técnico Real (WFP &amp; Anti-Cheat):</strong> O roteamento seletivo opera por sub-redes de destino dos servidores do jogo (<code className="font-mono text-purple-300">AllowedIPs = 155.133.248.0/24, 104.160.152.0/24</code>), preservando a rota padrão do Windows (<code className="font-mono text-rose-300">0.0.0.0/0</code> bloqueado por padrão). O redirecionamento exclusivo por PID via driver kernel WFP (<code className="font-mono">Callout Driver</code>) permanece desativado nesta versão para evitar hooks invasivos e reduzir o risco de conflito com sistemas anti-cheat (Vanguard, Ricochet, EAC, BattlEye), sem prometer imunidade absoluta frente a políticas externas de terceiros.
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-3 text-xs font-mono tabular-nums">
          <div className="p-3.5 rounded-lg bg-[#0a0c13] border border-slate-800 space-y-1">
            <span className="text-slate-400 font-sans font-semibold block">
              Veredito Honesto de Rota (Multi-Amostra)
            </span>
            <div className="text-emerald-400 font-bold">
              {avgExternalPing !== null
                ? `Rota Direta Medida (${avgExternalPing} ms / σ=${avgExternalJitter ?? 0} ms) — Manter Rota Direta caso o nó VPS adicione salto superior.`
                : 'Execute o diagnóstico acima para medir a Rota Direta com múltiplas amostras.'}
            </div>
          </div>

          <div className="p-3.5 rounded-lg bg-[#0a0c13] border border-slate-800 space-y-1">
            <span className="text-slate-400 font-sans font-semibold block">
              Auditoria de Túnel WireGuard (WireGuardTunnelManager)
            </span>
            <div className="text-purple-300">
              Bloqueia scripts PreUp/PostUp e exige verificação real de Handshake + contadores RX/TX via wg.exe show dump.
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

