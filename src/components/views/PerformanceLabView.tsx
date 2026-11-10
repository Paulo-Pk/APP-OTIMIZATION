import React, { useState } from 'react';
import {
  Gauge,
  TrendingUp,
  Activity,
  Zap,
  Play,
  RotateCcw,
  CheckCircle2,
  Calendar,
  Layers,
  ArrowUpRight,
} from 'lucide-react';
import { PresentMonSession } from '../../types';
import { samplePresentMonSessions } from '../../data/defaultGames';

export const PerformanceLabView: React.FC = () => {
  const [sessions, setSessions] = useState<PresentMonSession[]>(samplePresentMonSessions);
  const [selectedGame, setSelectedGame] = useState<string>('Counter-Strike 2');
  const [isRecording, setIsRecording] = useState<boolean>(false);
  const [recordSeconds, setRecordSeconds] = useState<number>(0);

  // Filter paired sessions for Before vs After comparison
  const cs2Optimized = sessions.find((s) => s.id === 'session-cs2-optimized');
  const cs2Baseline = sessions.find((s) => s.id === 'session-cs2-baseline');

  const cpOptimized = sessions.find((s) => s.id === 'session-cp2077-optimized');
  const cpBaseline = sessions.find((s) => s.id === 'session-cp2077-baseline');

  const currentPair =
    selectedGame === 'Counter-Strike 2'
      ? { baseline: cs2Baseline, optimized: cs2Optimized }
      : { baseline: cpBaseline, optimized: cpOptimized };

  const fpsDelta = currentPair.baseline && currentPair.optimized
    ? (((currentPair.optimized.avgFps - currentPair.baseline.avgFps) / currentPair.baseline.avgFps) * 100).toFixed(1)
    : '0';

  const low1Delta = currentPair.baseline && currentPair.optimized
    ? (((currentPair.optimized.onePercentLow - currentPair.baseline.onePercentLow) / currentPair.baseline.onePercentLow) * 100).toFixed(1)
    : '0';

  const frametimeDelta = currentPair.baseline && currentPair.optimized
    ? (((currentPair.baseline.frametimeStDevMs - currentPair.optimized.frametimeStDevMs) / currentPair.baseline.frametimeStDevMs) * 100).toFixed(1)
    : '0';

  const handleStartCapture = () => {
    setIsRecording(true);
    setRecordSeconds(0);
    const interval = setInterval(() => {
      setRecordSeconds((prev) => {
        if (prev >= 15) {
          clearInterval(interval);
          setIsRecording(false);
          return 15;
        }
        return prev + 1;
      });
    }, 1000);
  };

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Banner */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <Gauge className="w-4 h-4" /> Análise de Frametimes com Intel PresentMon
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">PERFORMANCE LAB</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl">
            Comparativo científico de métricas de FPS: taxas médias, 1% Low (stuttering) e desvio padrão de frametimes antes e depois das otimizações.
          </p>
        </div>

        <div className="flex items-center gap-3">
          <button
            onClick={handleStartCapture}
            disabled={isRecording}
            className={`flex items-center gap-2 px-4 py-2 rounded-lg font-bold text-xs uppercase tracking-wider transition-all cursor-pointer ${
              isRecording
                ? 'bg-rose-600 text-white animate-pulse'
                : 'bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white shadow-lg shadow-purple-600/30 border border-purple-400/40'
            }`}
          >
            <Play className="w-4 h-4 fill-current" />
            <span>{isRecording ? `Capturando PresentMon (${recordSeconds}s)...` : 'Capturar Sessão'}</span>
          </button>
        </div>
      </div>

      {/* Game Selector for Comparison */}
      <div className="flex items-center justify-between p-3 rounded-xl bg-[#0f121b] border border-slate-800">
        <div className="flex items-center gap-2">
          <span className="text-xs text-slate-400 font-semibold mr-1">Comparar Jogo:</span>
          {['Counter-Strike 2', 'Cyberpunk 2077'].map((game) => (
            <button
              key={game}
              onClick={() => setSelectedGame(game)}
              className={`px-3 py-1.5 rounded-lg text-xs font-semibold transition-all cursor-pointer ${
                selectedGame === game
                  ? 'bg-purple-600 text-white shadow-md shadow-purple-600/30'
                  : 'bg-[#151926] text-slate-400 hover:text-slate-200'
              }`}
            >
              {game}
            </button>
          ))}
        </div>

        <div className="text-xs text-slate-400 font-mono hidden sm:block">
          Condições Idênticas: Resolução 2560x1440 · Preset Alto · Duração 10min
        </div>
      </div>

      {/* Side-by-Side Comparison: ANTES vs DEPOIS */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
        {/* Baseline Card (Antes da Otimização) */}
        <div className="p-5 rounded-xl bg-[#0e111a] border border-slate-800 space-y-4">
          <div className="flex items-center justify-between pb-2 border-b border-slate-800">
            <div>
              <span className="text-[10px] font-bold text-slate-500 uppercase tracking-widest block">
                Condição Padrão
              </span>
              <h3 className="text-sm font-bold text-slate-300">
                ANTES DA OTIMIZAÇÃO (BASELINE)
              </h3>
            </div>
            <span className="text-xs font-mono text-slate-500">{currentPair.baseline?.date}</span>
          </div>

          <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
            <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-900">
              <span className="text-[10px] text-slate-500 uppercase block">FPS Médio</span>
              <span className="text-xl font-bold font-mono text-slate-300">
                {currentPair.baseline?.avgFps}
              </span>
            </div>
            <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-900">
              <span className="text-[10px] text-slate-500 uppercase block">1% Low (Fluidez)</span>
              <span className="text-xl font-bold font-mono text-slate-300">
                {currentPair.baseline?.onePercentLow}
              </span>
            </div>
            <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-900">
              <span className="text-[10px] text-slate-500 uppercase block">Frametime Médio</span>
              <span className="text-xl font-bold font-mono text-slate-300">
                {currentPair.baseline?.frametimeAvgMs} ms
              </span>
            </div>
            <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-900">
              <span className="text-[10px] text-slate-500 uppercase block">Desvio / Jitter</span>
              <span className="text-sm font-bold font-mono text-slate-400">
                ±{currentPair.baseline?.frametimeStDevMs} ms
              </span>
            </div>
            <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-900">
              <span className="text-[10px] text-slate-500 uppercase block">Uso CPU / GPU</span>
              <span className="text-sm font-bold font-mono text-slate-400">
                {currentPair.baseline?.cpuLoadAvg}% / {currentPair.baseline?.gpuLoadAvg}%
              </span>
            </div>
            <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-900">
              <span className="text-[10px] text-slate-500 uppercase block">Temp Média</span>
              <span className="text-sm font-bold font-mono text-slate-400">
                CPU {currentPair.baseline?.cpuTempAvg}°C / GPU {currentPair.baseline?.gpuTempAvg}°C
              </span>
            </div>
          </div>
        </div>

        {/* Optimized Card (Depois da Otimização) */}
        <div className="p-5 rounded-xl bg-[#111524] border border-purple-500/50 space-y-4 shadow-lg shadow-purple-950/20">
          <div className="flex items-center justify-between pb-2 border-b border-purple-900/40">
            <div>
              <span className="text-[10px] font-bold text-purple-400 uppercase tracking-widest block">
                Com PC Control Ultimate
              </span>
              <h3 className="text-sm font-bold text-white flex items-center gap-1.5">
                DEPOIS DA OTIMIZAÇÃO (BOOST ATIVO)
                <CheckCircle2 className="w-4 h-4 text-emerald-400" />
              </h3>
            </div>
            <span className="text-xs font-mono text-purple-300">{currentPair.optimized?.date}</span>
          </div>

          <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
            <div className="p-3 rounded-lg bg-[#0a0c13] border border-purple-950/60">
              <div className="flex justify-between items-start">
                <span className="text-[10px] text-slate-400 uppercase block">FPS Médio</span>
                <span className="text-[10px] font-bold text-emerald-400">+{fpsDelta}%</span>
              </div>
              <span className="text-xl font-bold font-mono text-white">
                {currentPair.optimized?.avgFps}
              </span>
            </div>
            <div className="p-3 rounded-lg bg-[#0a0c13] border border-purple-950/60">
              <div className="flex justify-between items-start">
                <span className="text-[10px] text-slate-400 uppercase block">1% Low</span>
                <span className="text-[10px] font-bold text-emerald-400">+{low1Delta}%</span>
              </div>
              <span className="text-xl font-bold font-mono text-emerald-400">
                {currentPair.optimized?.onePercentLow}
              </span>
            </div>
            <div className="p-3 rounded-lg bg-[#0a0c13] border border-purple-950/60">
              <div className="flex justify-between items-start">
                <span className="text-[10px] text-slate-400 uppercase block">Frametime</span>
                <span className="text-[10px] font-bold text-emerald-400">Melhor</span>
              </div>
              <span className="text-xl font-bold font-mono text-purple-300">
                {currentPair.optimized?.frametimeAvgMs} ms
              </span>
            </div>
            <div className="p-3 rounded-lg bg-[#0a0c13] border border-purple-950/60">
              <div className="flex justify-between items-start">
                <span className="text-[10px] text-slate-400 uppercase block">Stutter StDev</span>
                <span className="text-[10px] font-bold text-emerald-400">-{frametimeDelta}%</span>
              </div>
              <span className="text-sm font-bold font-mono text-emerald-300">
                ±{currentPair.optimized?.frametimeStDevMs} ms
              </span>
            </div>
            <div className="p-3 rounded-lg bg-[#0a0c13] border border-purple-950/60">
              <span className="text-[10px] text-slate-400 uppercase block">Uso CPU / GPU</span>
              <span className="text-sm font-bold font-mono text-slate-200">
                {currentPair.optimized?.cpuLoadAvg}% / {currentPair.optimized?.gpuLoadAvg}%
              </span>
            </div>
            <div className="p-3 rounded-lg bg-[#0a0c13] border border-purple-950/60">
              <span className="text-[10px] text-slate-400 uppercase block">Temp Média</span>
              <span className="text-sm font-bold font-mono text-slate-200">
                CPU {currentPair.optimized?.cpuTempAvg}°C / GPU {currentPair.optimized?.gpuTempAvg}°C
              </span>
            </div>
          </div>
        </div>
      </div>

      {/* Frametime Simulation Graph */}
      <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-3">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Activity className="w-4 h-4 text-purple-400" />
            <h3 className="text-sm font-bold text-white">
              Variação de Frametimes PresentMon (Milissegundos por Quadro)
            </h3>
          </div>
          <span className="text-xs font-mono text-emerald-400">
            Linha Reta = Ausência de Stutters (Taxa de Quadros Perfeita)
          </span>
        </div>

        <div className="h-40 bg-[#0a0c13] rounded-lg border border-slate-800 p-2 relative overflow-hidden flex items-center">
          <svg className="w-full h-full" viewBox="0 0 500 100" preserveAspectRatio="none">
            {/* Target 144Hz line (6.94ms) */}
            <line x1="0" y1="50" x2="500" y2="50" stroke="#334155" strokeDasharray="3 3" />

            {/* Baseline with spikes */}
            <polyline
              fill="none"
              stroke="#64748b"
              strokeWidth="1.5"
              points="0,52 30,50 60,75 90,48 120,53 150,85 180,49 210,51 240,80 270,49 300,52 330,78 360,49 400,51 450,82 500,50"
            />

            {/* Optimized with flat low frametime */}
            <polyline
              fill="none"
              stroke="#a855f7"
              strokeWidth="2"
              points="0,32 30,31 60,33 90,32 120,31 150,33 180,32 210,31 240,32 270,31 300,32 330,31 360,32 400,31 450,32 500,31"
            />
          </svg>

          <div className="absolute top-2 right-3 flex items-center gap-4 text-xs font-mono">
            <span className="text-slate-400 flex items-center gap-1">
              <span className="w-2.5 h-1 bg-slate-500 inline-block" /> Baseline (Picos de Latência)
            </span>
            <span className="text-purple-400 font-bold flex items-center gap-1">
              <span className="w-2.5 h-1 bg-purple-500 inline-block" /> PC Control Boost (Estável)
            </span>
          </div>
        </div>
      </div>
    </div>
  );
};
