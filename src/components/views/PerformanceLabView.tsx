import React, { useState, useEffect, useCallback, useRef } from 'react';
import {
  Gauge,
  Activity,
  Play,
  CheckCircle2,
  AlertCircle,
} from 'lucide-react';
import { HardwareTelemetry, PresentMonSession } from '../../types';
import { useI18n } from '../../i18n';
import { nativeBridge } from '../../services/nativeBridge';

interface PerformanceLabViewProps { telemetry: HardwareTelemetry; }

export const PerformanceLabView: React.FC<PerformanceLabViewProps> = ({ telemetry }) => {
  const { t } = useI18n();
  // Strictly empty until a real PresentMon capture is completed via the Windows Agent
  const [sessions, setSessions] = useState<PresentMonSession[]>([]);
  const [selectedProcess, setSelectedProcess] = useState<string>('');
  const [runningProcesses, setRunningProcesses] = useState<Array<{ processId: number; processName: string; mainWindowTitle: string }>>([]);
  const [isLoadingProcesses, setIsLoadingProcesses] = useState(false);

  const refreshRunningProcesses = useCallback(async () => {
    setIsLoadingProcesses(true);
    try {
      const processes = await nativeBridge.listRunningProcesses();
      const visibleProcesses = (processes || []).filter((p) => p.processName && p.processId > 4 && (p.mainWindowTitle || '').trim().length > 0).sort((a, b) => a.mainWindowTitle.localeCompare(b.mainWindowTitle, 'pt-BR'));
      setRunningProcesses(visibleProcesses);
      setSelectedProcess((current) => visibleProcesses.some((p) => p.processName === current) ? current : (visibleProcesses[0]?.processName || ''));
    } finally {
      setIsLoadingProcesses(false);
    }
  }, []);

  useEffect(() => {
    const unsubscribe = nativeBridge.subscribe((status) => {
      if (status.isConnected) void refreshRunningProcesses();
      else { setRunningProcesses([]); setSelectedProcess(''); }
    });
    return unsubscribe;
  }, [refreshRunningProcesses]);
  const [durationSec, setDurationSec] = useState<number>(10);
  const [isRecording, setIsRecording] = useState<boolean>(false);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const telemetrySamples = useRef<Array<{ cpuLoad: number; gpuLoad: number; cpuTemp: number; gpuTemp: number }>>([]);

  useEffect(() => {
    if (!isRecording) return;
    telemetrySamples.current.push({
      cpuLoad: Number(telemetry.cpu.load) || 0,
      gpuLoad: Number(telemetry.gpu.load) || 0,
      cpuTemp: Number(telemetry.cpu.temperature) || 0,
      gpuTemp: Number(telemetry.gpu.temperature) || 0,
    });
  }, [telemetry, isRecording]);

  const handleStartCapture = async () => {
    if (!selectedProcess) {
      setStatusMessage('Selecione um jogo ou processo em execução antes de iniciar a captura.');
      return;
    }

    telemetrySamples.current = [];
    setIsRecording(true);
    setStatusMessage(t.performanceLab.initializingHook);

    try {
      const nativeResult = await nativeBridge.runPresentMonCapture(selectedProcess, durationSec);
      if (nativeResult && nativeResult.capturedFrames > 0) {
        const samples = telemetrySamples.current;
        const average = (key: 'cpuLoad' | 'gpuLoad' | 'cpuTemp' | 'gpuTemp') => {
          const values = samples.map((sample) => sample[key]).filter((value) => Number.isFinite(value) && value > 0);
          return values.length ? Number((values.reduce((sum, value) => sum + value, 0) / values.length).toFixed(1)) : 0;
        };

        const newSession: PresentMonSession = {
          id: `session-${Date.now()}`,
          gameTitle: `${selectedProcess} (PresentMon Real)`,
          date: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
          durationSeconds: durationSec,
          avgFps: nativeResult.avgFps,
          minFps: nativeResult.minFps ?? 0,
          onePercentLow: nativeResult.onePercentLow,
          pointOnePercentLow: 0,
          frametimeAvgMs: nativeResult.frametimeAvgMs,
          frametimeStDevMs: nativeResult.frametimeStDevMs,
          cpuLoadAvg: average('cpuLoad'),
          gpuLoadAvg: average('gpuLoad'),
          cpuTempAvg: average('cpuTemp'),
          gpuTempAvg: average('gpuTemp'),
          isOptimized: false,
        };
        setSessions((prev) => [newSession, ...prev]);
        setStatusMessage(t.performanceLab.captureSuccess);
      } else {
        setStatusMessage(t.performanceLab.agentRequiredPresentMon);
      }
    } catch (error) {
      setStatusMessage(error instanceof Error ? `Não foi possível concluir a captura: ${error.message}` : 'Não foi possível concluir a captura PresentMon.');
    } finally {
      setIsRecording(false);
    }
  };

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Banner */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <Gauge className="w-4 h-4" /> {t.performanceLab.subtitle}
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">{t.performanceLab.title}</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl leading-relaxed">
            {t.performanceLab.description}
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-3">
          <select
            value={selectedProcess}
            onChange={(e) => setSelectedProcess(e.target.value)}
            disabled={isLoadingProcesses || runningProcesses.length === 0}
            className="min-w-[250px] px-3 py-2 rounded-lg bg-[#0a0c13] border border-slate-700 text-xs text-white disabled:opacity-60"
          >
            <option value="" style={{ backgroundColor: '#0a0c13', color: '#f8fafc' }}>{isLoadingProcesses ? 'Procurando jogos em execução...' : 'Selecione um jogo em execução'}</option>
            {runningProcesses.map((process) => (
              <option key={process.processId} value={process.processName} style={{ backgroundColor: '#0a0c13', color: '#f8fafc' }}>
                {process.mainWindowTitle ? `${process.mainWindowTitle} — ${process.processName}.exe` : `${process.processName}.exe`}
              </option>
            ))}
          </select>
          <button type="button" onClick={() => void refreshRunningProcesses()} disabled={isLoadingProcesses} className="px-3 py-2 rounded-lg bg-[#171b2a] border border-slate-700 text-xs text-slate-200 disabled:opacity-50">
            {isLoadingProcesses ? 'Buscando...' : 'Atualizar jogos'}
          </button>
          <select
            value={durationSec}
            onChange={(e) => setDurationSec(Number(e.target.value))}
            className="px-3 py-2 rounded-lg bg-[#0a0c13] border border-slate-700 text-xs font-mono text-white"
          >
            <option value={10} style={{ backgroundColor: "#0a0c13", color: "#f8fafc" }}>10 segundos</option>
            <option value={30} style={{ backgroundColor: "#0a0c13", color: "#f8fafc" }}>30 segundos</option>
            <option value={60} style={{ backgroundColor: "#0a0c13", color: "#f8fafc" }}>60 segundos</option>
          </select>
          <button
            onClick={handleStartCapture}
            disabled={isRecording || !selectedProcess}
            className={`flex items-center gap-2 px-4 py-2 rounded-lg font-bold text-xs uppercase tracking-wider transition-all cursor-pointer ${
              isRecording
                ? 'bg-rose-600 text-white animate-pulse'
                : 'bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white shadow-lg shadow-purple-600/30 border border-purple-400/40'
            }`}
          >
            <Play className="w-4 h-4 fill-current" />
            <span>{isRecording ? t.performanceLab.capturing : t.performanceLab.captureSession}</span>
          </button>
        </div>
      </div>

      {statusMessage && (
        <div className="p-3 bg-[#111626] border border-purple-500/40 rounded-lg text-xs text-purple-300 font-mono flex items-center justify-between">
          <span>{statusMessage}</span>
          <button
            onClick={() => setStatusMessage(null)}
            className="text-slate-400 hover:text-white cursor-pointer font-bold"
          >
            ×
          </button>
        </div>
      )}

      {sessions.length === 0 ? (
        <div className="p-8 rounded-xl bg-[#0f121b] border border-slate-800 text-center space-y-3">
          <AlertCircle className="w-8 h-8 text-purple-400 mx-auto" />
          <div className="text-sm font-bold text-white">
            Nenhuma Sessão PresentMon Capturada Nesta Sessão
          </div>
          <p className="text-xs text-slate-400 max-w-lg mx-auto leading-relaxed">
            O PC Control Ultimate não exibe métricas de FPS ou frametime pré-fabricadas. Conecte o Agente Windows Privilegiado com o Intel PresentMon instalado, informe o executável do jogo em execução e inicie uma captura real para visualizar FPS Médio, 1% Low e desvio padrão de frametime.
          </p>
        </div>
      ) : (
        <div className="space-y-4">
          {sessions.map((s) => (
            <div
              key={s.id}
              className="p-5 rounded-xl bg-[#111524] border border-purple-500/50 space-y-4"
            >
              <div className="flex items-center justify-between pb-2 border-b border-purple-900/40">
                <div className="flex items-center gap-2">
                  <Activity className="w-4 h-4 text-purple-400" />
                  <h3 className="text-sm font-bold text-white">{s.gameTitle}</h3>
                  <CheckCircle2 className="w-4 h-4 text-emerald-400" />
                </div>
                <span className="text-xs font-mono text-purple-300">
                  {s.date} · {s.durationSeconds}s
                </span>
              </div>

              <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 font-mono tabular-nums">
                <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800">
                  <span className="text-[10px] text-slate-400 uppercase block font-sans">
                    {t.performanceLab.avgFps}
                  </span>
                  <span className="text-xl font-bold text-white">{s.avgFps} FPS</span>
                </div>
                <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800">
                  <span className="text-[10px] text-slate-400 uppercase block font-sans">
                    {t.performanceLab.low1Fps}
                  </span>
                  <span className="text-xl font-bold text-emerald-400">{s.onePercentLow} FPS</span>
                </div>
                <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800">
                  <span className="text-[10px] text-slate-400 uppercase block font-sans">
                    {t.performanceLab.frametimeAvg}
                  </span>
                  <span className="text-xl font-bold text-purple-300">{s.frametimeAvgMs} ms</span>
                </div>
                <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800">
                  <span className="text-[10px] text-slate-400 uppercase block font-sans">
                    {t.performanceLab.frametimeJitter}
                  </span>
                  <span className="text-xl font-bold text-cyan-300">±{s.frametimeStDevMs} ms</span>
                </div>
                <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800">
                  <span className="text-[10px] text-slate-400 uppercase block font-sans">Uso médio da CPU</span>
                  <span className="text-xl font-bold text-purple-300">{s.cpuLoadAvg > 0 ? `${s.cpuLoadAvg}%` : 'N/D'}</span>
                </div>
                <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800">
                  <span className="text-[10px] text-slate-400 uppercase block font-sans">Uso médio da GPU</span>
                  <span className="text-xl font-bold text-cyan-300">{s.gpuLoadAvg > 0 ? `${s.gpuLoadAvg}%` : 'N/D'}</span>
                </div>
                <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800">
                  <span className="text-[10px] text-slate-400 uppercase block font-sans">Temperatura média da CPU</span>
                  <span className="text-xl font-bold text-amber-300">{s.cpuTempAvg > 0 ? `${s.cpuTempAvg}°C` : 'N/D'}</span>
                </div>
                <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800">
                  <span className="text-[10px] text-slate-400 uppercase block font-sans">Temperatura média da GPU</span>
                  <span className="text-xl font-bold text-rose-300">{s.gpuTempAvg > 0 ? `${s.gpuTempAvg}°C` : 'N/D'}</span>
                </div>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};
