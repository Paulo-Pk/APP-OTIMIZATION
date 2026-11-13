import React, { useState } from 'react';
import {
  Wifi,
  Play,
  Globe,
  Server,
  AlertCircle,
  ShieldCheck,
} from 'lucide-react';
import { useI18n } from '../../i18n';

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

    // 1. Measure Local HTTP Loopback Latency (explicitly separated from external internet latency)
    const localSamples: number[] = [];
    for (let i = 0; i < 3; i++) {
      const start = performance.now();
      try {
        const res = await fetch(`/api/network/ping?t=${Date.now()}`);
        if (res.ok) {
          localSamples.push(performance.now() - start);
        }
      } catch {
        // failed probe
      }
    }

    if (localSamples.length > 0) {
      const avgLocal = localSamples.reduce((a, b) => a + b, 0) / localSamples.length;
      setLocalPingMs(Math.round(avgLocal));
    } else {
      setLocalPingMs(null);
    }

    // 2. Measure External Gaming Targets via Real TCP Handshake RTT
    try {
      const targetRes = await fetch('/api/network/measure-targets');
      if (targetRes.ok) {
        const data = await targetRes.json();
        if (data.results && Array.isArray(data.results)) {
          setTargetResults(data.results);

          const successfulTargets = (data.results as TargetMeasurement[]).filter(
            (r) => r.isSuccess && r.pingMs !== null
          );
          if (successfulTargets.length > 0) {
            const avgPing =
              successfulTargets.reduce((acc, curr) => acc + (curr.pingMs || 0), 0) /
              successfulTargets.length;
            const avgJitter =
              successfulTargets.reduce((acc, curr) => acc + (curr.jitterMs || 0), 0) /
              successfulTargets.length;

            setAvgExternalPing(Math.round(avgPing));
            setAvgExternalJitter(parseFloat(avgJitter.toFixed(1)));
          } else {
            setAvgExternalPing(null);
            setAvgExternalJitter(null);
          }
        }
      } else {
        setTargetResults([]);
        setAvgExternalPing(null);
        setAvgExternalJitter(null);
      }
    } catch {
      setTargetResults([]);
      setAvgExternalPing(null);
      setAvgExternalJitter(null);
      setTestError(t.networkLab.measurementUnavailable);
    }

    // 3. Measure Real External Download Throughput via Cloudflare Speed CDN (Never localhost!)
    try {
      const res = await fetch(`/api/network/external-throughput?t=${Date.now()}`);
      if (res.ok) {
        const data = await res.json();
        if (data.available && typeof data.downloadMbps === 'number') {
          setDownloadMbps(data.downloadMbps);
        } else {
          setDownloadMbps(null);
        }
      } else {
        setDownloadMbps(null);
      }
    } catch {
      setDownloadMbps(null);
    }

    // 4. Measure Real DNS Resolution (UDP 53)
    try {
      const dnsRes = await fetch('/api/network/dns-benchmark');
      if (dnsRes.ok) {
        const dnsData = await dnsRes.json();
        if (dnsData.results) {
          setDnsResults(dnsData.results);
        }
      }
    } catch {
      setDnsResults([]);
    }

    setIsRunning(false);
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
          Distinção Técnica: Os tempos para servidores externos referem-se ao tempo de conexão TCP (SYN/SYN-ACK Handshake), distinto de ICMP Echo. O teste de velocidade realiza download externo real (speed.cloudflare.com), nunca contra localhost.
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
            {t.networkLab.localServerLatencyTitle} (IPC Local)
          </span>
          <div className="text-2xl font-black text-cyan-400">
            {localPingMs !== null
              ? `${localPingMs} ms`
              : hasRun
              ? t.networkLab.measurementUnavailable
              : '—'}
          </div>
          <span className="text-[10px] text-slate-400 block font-sans">Loopback HTTP Local (Não é internet)</span>
        </div>

        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-1">
          <span className="text-[10px] text-slate-500 uppercase block font-semibold font-sans">
            Tempo de Conexão TCP (Externo)
          </span>
          <div className="text-2xl font-black text-emerald-400">
            {avgExternalPing !== null
              ? `${avgExternalPing} ms`
              : hasRun
              ? t.networkLab.measurementUnavailable
              : '—'}
          </div>
          <span className="text-[10px] text-slate-400 block font-sans">TCP Handshake RTT Médio</span>
        </div>

        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-1">
          <span className="text-[10px] text-slate-500 uppercase block font-semibold font-sans">
            {t.networkLab.jitterTitle} (TCP)
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
            Download Externo (Cloudflare CDN)
          </span>
          <div className="text-2xl font-black text-amber-300">
            {downloadMbps !== null
              ? `${downloadMbps} Mbps`
              : hasRun
              ? t.networkLab.measurementUnavailable
              : '—'}
          </div>
          <span className="text-[10px] text-slate-400 block font-sans">Origem Externa Confiável (HTTPS)</span>
        </div>
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
                      {t.networkLab.packetLoss}
                    </span>
                    <span
                      className={`text-sm font-bold ${
                        target.tcpFailurePercent === 0
                          ? 'text-emerald-400'
                          : target.packetLossPercent < 30
                          ? 'text-amber-400'
                          : 'text-rose-400'
                      }`}
                    >
                      {target.packetLossPercent}%
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

