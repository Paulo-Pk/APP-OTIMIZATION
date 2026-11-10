import React, { useState } from 'react';
import {
  Wifi,
  Activity,
  Play,
  RotateCcw,
  CheckCircle2,
  Globe,
  Server,
  Zap,
  AlertCircle,
  ShieldCheck,
} from 'lucide-react';
import { useI18n } from '../../i18n';

interface TargetMeasurement {
  id: string;
  server: string;
  location: string;
  host: string;
  pingMs: number | null;
  jitterMs: number | null;
  packetLossPercent: number;
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

  // Local server / web bridge latency
  const [localPingMs, setLocalPingMs] = useState<number | null>(null);

  // External gaming target metrics (Real TCP handshakes)
  const [targetResults, setTargetResults] = useState<TargetMeasurement[]>([]);

  // Average external metrics
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

    // 1. Measure Local Server / Web Bridge Latency
    const localSamples: number[] = [];
    for (let i = 0; i < 4; i++) {
      const start = performance.now();
      try {
        const res = await fetch(`/api/network/ping?t=${Date.now()}`);
        if (res.ok) {
          const elapsed = performance.now() - start;
          localSamples.push(elapsed);
        }
      } catch {
        // failed probe
      }
      await new Promise((r) => setTimeout(r, 60));
    }

    if (localSamples.length > 0) {
      const avgLocal = localSamples.reduce((a, b) => a + b, 0) / localSamples.length;
      setLocalPingMs(Math.round(avgLocal));
    } else {
      setLocalPingMs(null);
    }

    // 2. Measure External Gaming Targets via Real TCP Handshake
    try {
      const targetRes = await fetch('/api/network/measure-targets');
      if (targetRes.ok) {
        const data = await targetRes.json();
        if (data.results && Array.isArray(data.results)) {
          setTargetResults(data.results);

          // Calculate average external ping and jitter for successful probes
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

    // 3. Measure Real Download Transfer Speed via Chunk Stream
    try {
      const dlStart = performance.now();
      const res = await fetch(`/api/network/payload?size=2048&t=${Date.now()}`);
      if (res.ok) {
        const buffer = await res.arrayBuffer();
        const dlElapsedSeconds = (performance.now() - dlStart) / 1000;
        if (dlElapsedSeconds > 0 && buffer.byteLength > 0) {
          const totalBits = buffer.byteLength * 8;
          const calculatedMbps = totalBits / (dlElapsedSeconds * 1000000);
          setDownloadMbps(Math.round(calculatedMbps));
        } else {
          setDownloadMbps(null);
        }
      } else {
        setDownloadMbps(null);
      }
    } catch {
      setDownloadMbps(null);
    }

    // 4. Measure Real DNS Resolution (No synthetic fallbacks)
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

      {/* Real TCP Notice */}
      <div className="p-3.5 rounded-xl bg-[#0e121e] border border-purple-500/20 text-xs flex items-center gap-2.5 text-purple-300">
        <ShieldCheck className="w-4 h-4 text-purple-400 shrink-0" />
        <span>{t.networkLab.realTcpNotice}</span>
      </div>

      {testError && (
        <div className="p-4 rounded-xl bg-[#261316] border border-rose-500/60 text-xs text-rose-300 flex items-center gap-2.5">
          <AlertCircle className="w-5 h-5 text-rose-400 shrink-0" />
          <span>{testError}</span>
        </div>
      )}

      {/* Primary Metrics Row (Distinguishing Local Server vs External Gaming Clusters) */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        {/* Local Server Latency */}
        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-1">
          <span className="text-[10px] text-slate-500 uppercase block font-semibold">
            {t.networkLab.localServerLatencyTitle}
          </span>
          <div className="text-2xl font-black font-mono text-cyan-400">
            {localPingMs !== null
              ? `${localPingMs} ms`
              : hasRun
              ? t.networkLab.measurementUnavailable
              : '—'}
          </div>
          <span className="text-[10px] text-slate-400 block">{t.networkLab.pingDesc}</span>
        </div>

        {/* External Gaming Average Latency */}
        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-1">
          <span className="text-[10px] text-slate-500 uppercase block font-semibold">
            {t.networkLab.pingLatency} (Média Externa)
          </span>
          <div className="text-2xl font-black font-mono text-emerald-400">
            {avgExternalPing !== null
              ? `${avgExternalPing} ms`
              : hasRun
              ? t.networkLab.measurementUnavailable
              : '—'}
          </div>
          <span className="text-[10px] text-slate-400 block">TCP Handshake para Servidores de Jogos</span>
        </div>

        {/* Jitter */}
        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-1">
          <span className="text-[10px] text-slate-500 uppercase block font-semibold">
            {t.networkLab.jitterTitle}
          </span>
          <div className="text-2xl font-black font-mono text-purple-300">
            {avgExternalJitter !== null
              ? `±${avgExternalJitter} ms`
              : hasRun
              ? t.networkLab.measurementUnavailable
              : '—'}
          </div>
          <span className="text-[10px] text-slate-400 block">{t.networkLab.jitterDesc}</span>
        </div>

        {/* Real Download Throughput */}
        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-1">
          <span className="text-[10px] text-slate-500 uppercase block font-semibold">
            {t.networkLab.throughputTitle}
          </span>
          <div className="text-2xl font-black font-mono text-amber-300">
            {downloadMbps !== null
              ? `${downloadMbps} Mbps`
              : hasRun
              ? t.networkLab.measurementUnavailable
              : '—'}
          </div>
          <span className="text-[10px] text-slate-400 block">{t.networkLab.throughputDesc}</span>
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
                <div className="flex justify-between items-center text-slate-400 font-mono pt-1">
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

                <div className="flex items-center gap-6 font-mono">
                  {/* Ping */}
                  <div>
                    <span className="text-slate-500 block text-[9px] uppercase">Ping (TCP)</span>
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

                  {/* Jitter */}
                  <div>
                    <span className="text-slate-500 block text-[9px] uppercase">Jitter</span>
                    <span className="text-slate-300 text-sm">
                      {target.jitterMs !== null ? `±${target.jitterMs} ms` : '—'}
                    </span>
                  </div>

                  {/* Packet Loss */}
                  <div>
                    <span className="text-slate-500 block text-[9px] uppercase">
                      {t.networkLab.packetLoss}
                    </span>
                    <span
                      className={`text-sm font-bold ${
                        target.packetLossPercent === 0
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
    </div>
  );
};
