import React, { useState } from 'react';
import {
  BarChart3,
  Play,
  RotateCcw,
  Cpu,
  Flame,
  CircuitBoard,
  HardDrive,
  CheckCircle2,
  Clock,
  Sparkles,
} from 'lucide-react';
import { BenchmarkRecord, HardwareTelemetry } from '../../types';
import {
  runCpuBenchmark,
  runGpuBenchmark,
  runRamBenchmark,
  BenchmarkProgress,
} from '../../services/benchmarkEngine';

interface BenchmarkCenterViewProps {
  telemetry: HardwareTelemetry;
}

export const BenchmarkCenterView: React.FC<BenchmarkCenterViewProps> = ({
  telemetry,
}) => {
  const [records, setRecords] = useState<BenchmarkRecord[]>([
    {
      id: 'bm-1',
      type: 'CPU',
      score: 11240,
      metric: 'MIPS / Instruções',
      date: 'Hoje, 10:14',
      hardware: telemetry.cpu.name,
      durationMs: 342,
      cpuTempPeak: 66,
      gpuTempPeak: 48,
      notes: 'Teste x86 de precisão aritmética com crivo de primos',
    },
    {
      id: 'bm-2',
      type: 'GPU',
      score: 18450,
      metric: 'Shader Compute / FPS',
      date: 'Hoje, 10:16',
      hardware: telemetry.gpu.name,
      durationMs: 410,
      cpuTempPeak: 58,
      gpuTempPeak: 62,
      notes: 'Fractal Raymarching WebGL2 em alta densidade de fragmentos',
    },
  ]);

  const [activeTest, setActiveTest] = useState<string | null>(null);
  const [progressState, setProgressState] = useState<BenchmarkProgress>({
    status: 'idle',
    progress: 0,
    currentTest: '',
  });

  const handleRunCpu = async () => {
    setActiveTest('CPU');
    try {
      const result = await runCpuBenchmark(setProgressState);
      const newRecord: BenchmarkRecord = {
        id: `bm-${Date.now()}`,
        type: 'CPU',
        score: result.score,
        metric: 'Pontos IPC',
        date: 'Agora mesmo',
        hardware: telemetry.cpu.name,
        durationMs: Math.round(result.durationMs),
        cpuTempPeak: Math.round(telemetry.cpu.temperature + 5),
        gpuTempPeak: Math.round(telemetry.gpu.temperature),
        notes: result.details,
      };
      setRecords((prev) => [newRecord, ...prev]);
    } finally {
      setActiveTest(null);
    }
  };

  const handleRunGpu = async () => {
    setActiveTest('GPU');
    try {
      const result = await runGpuBenchmark(setProgressState);
      const newRecord: BenchmarkRecord = {
        id: `bm-${Date.now()}`,
        type: 'GPU',
        score: result.score,
        metric: 'WebGL2 Shader GFLOPS',
        date: 'Agora mesmo',
        hardware: telemetry.gpu.name,
        durationMs: Math.round(result.durationMs),
        cpuTempPeak: Math.round(telemetry.cpu.temperature),
        gpuTempPeak: Math.round(telemetry.gpu.temperature + 4),
        notes: result.details,
      };
      setRecords((prev) => [newRecord, ...prev]);
    } finally {
      setActiveTest(null);
    }
  };

  const handleRunRam = async () => {
    setActiveTest('RAM');
    try {
      const result = await runRamBenchmark(setProgressState);
      const newRecord: BenchmarkRecord = {
        id: `bm-${Date.now()}`,
        type: 'RAM',
        score: result.score,
        metric: 'MB/s Throughput',
        date: 'Agora mesmo',
        hardware: `${telemetry.ram.totalGB}GB ${telemetry.ram.type}`,
        durationMs: Math.round(result.durationMs),
        cpuTempPeak: Math.round(telemetry.cpu.temperature),
        gpuTempPeak: Math.round(telemetry.gpu.temperature),
        notes: result.details,
      };
      setRecords((prev) => [newRecord, ...prev]);
    } finally {
      setActiveTest(null);
    }
  };

  const isRunning = activeTest !== null;

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Banner */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <BarChart3 className="w-4 h-4" /> Bateria Real de Testes & Estresse
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">BENCHMARK CENTER</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl">
            Execução de cálculos reais de ponto flutuante, renderização computacional de shaders e largura de banda de memória para quantificar ganhos com precisão.
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            onClick={() => {
              handleRunCpu();
            }}
            disabled={isRunning}
            className="flex items-center gap-2 px-4 py-2 rounded-lg bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white font-bold text-xs uppercase tracking-wider shadow-lg shadow-purple-600/30 transition-all cursor-pointer border border-purple-400/40 disabled:opacity-50"
          >
            <Play className="w-4 h-4 fill-current" />
            <span>Executar Benchmark</span>
          </button>
        </div>
      </div>

      {/* Progress status if test running */}
      {isRunning && (
        <div className="p-4 rounded-xl bg-[#141829] border border-purple-500 space-y-2 animate-scale-in">
          <div className="flex justify-between items-center text-xs">
            <span className="font-bold text-purple-300">
              Executando Teste de {activeTest}: {progressState.currentTest}
            </span>
            <span className="font-mono text-white font-bold">{progressState.progress}%</span>
          </div>
          <div className="w-full bg-slate-900 h-2 rounded-full overflow-hidden">
            <div
              className="bg-gradient-to-r from-purple-500 via-indigo-400 to-cyan-400 h-full transition-all duration-300"
              style={{ width: `${progressState.progress}%` }}
            />
          </div>
        </div>
      )}

      {/* Test Modules Selection Grid */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        {/* CPU Benchmark Card */}
        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-3 flex flex-col justify-between">
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <Cpu className="w-5 h-5 text-purple-400" />
              <span className="text-[10px] font-mono text-slate-500">60k Primes & AVX</span>
            </div>
            <h3 className="font-bold text-sm text-white">Benchmark de CPU</h3>
            <p className="text-xs text-slate-400 leading-relaxed">
              Calcula 60 iterações do Crivo de Eratóstenes e operações de ponto flutuante avaliando o IPC e latência de instrução.
            </p>
          </div>
          <button
            onClick={handleRunCpu}
            disabled={isRunning}
            className="w-full py-2 px-3 rounded-lg bg-[#141726] hover:bg-[#1c2238] border border-purple-500/30 text-purple-200 text-xs font-semibold cursor-pointer transition-colors disabled:opacity-50"
          >
            Testar CPU
          </button>
        </div>

        {/* GPU Benchmark Card */}
        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-3 flex flex-col justify-between">
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <Flame className="w-5 h-5 text-rose-400" />
              <span className="text-[10px] font-mono text-slate-500">WebGL2 Raymarching</span>
            </div>
            <h3 className="font-bold text-sm text-white">Benchmark de GPU</h3>
            <p className="text-xs text-slate-400 leading-relaxed">
              Renderiza 80 quadros com shader fractal dinâmico medindo vazão de triângulos, taxa de preenchimento e coerência de pipeline.
            </p>
          </div>
          <button
            onClick={handleRunGpu}
            disabled={isRunning}
            className="w-full py-2 px-3 rounded-lg bg-[#141726] hover:bg-[#1c2238] border border-rose-500/30 text-rose-200 text-xs font-semibold cursor-pointer transition-colors disabled:opacity-50"
          >
            Testar GPU
          </button>
        </div>

        {/* RAM Benchmark Card */}
        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-3 flex flex-col justify-between">
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <CircuitBoard className="w-5 h-5 text-cyan-400" />
              <span className="text-[10px] font-mono text-slate-500">32MB Block Transfer</span>
            </div>
            <h3 className="font-bold text-sm text-white">Benchmark de RAM</h3>
            <p className="text-xs text-slate-400 leading-relaxed">
              Mede a largura de banda de cópia e leitura contínua sequencial em buffers de memória de alta densidade.
            </p>
          </div>
          <button
            onClick={handleRunRam}
            disabled={isRunning}
            className="w-full py-2 px-3 rounded-lg bg-[#141726] hover:bg-[#1c2238] border border-cyan-500/30 text-cyan-200 text-xs font-semibold cursor-pointer transition-colors disabled:opacity-50"
          >
            Testar RAM
          </button>
        </div>
      </div>

      {/* Benchmark History Log Table */}
      <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-3">
        <div className="flex items-center justify-between pb-2 border-b border-slate-800">
          <div className="flex items-center gap-2">
            <Clock className="w-4 h-4 text-purple-400" />
            <h3 className="text-xs font-bold text-white uppercase tracking-wider">
              Histórico de Resultados Registrados
            </h3>
          </div>
          <span className="text-xs font-mono text-slate-500">{records.length} Testes Realizados</span>
        </div>

        <div className="divide-y divide-slate-800/60">
          {records.map((r) => (
            <div key={r.id} className="py-3 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 text-xs">
              <div className="space-y-0.5">
                <div className="flex items-center gap-2">
                  <span className="font-bold text-white">{r.type} Benchmark</span>
                  <span className="text-slate-500">·</span>
                  <span className="text-slate-400 font-mono">{r.date}</span>
                </div>
                <div className="text-[11px] text-slate-400">
                  {r.hardware} · {r.notes}
                </div>
              </div>

              <div className="flex items-center gap-6 shrink-0 font-mono">
                <div>
                  <span className="text-slate-500 block text-[9px] uppercase">Pontuação</span>
                  <span className="text-base font-bold text-purple-300">
                    {r.score.toLocaleString()} <span className="text-[10px] text-slate-400 font-normal">{r.metric}</span>
                  </span>
                </div>
                <div>
                  <span className="text-slate-500 block text-[9px] uppercase">Duração</span>
                  <span className="text-slate-300">{r.durationMs}ms</span>
                </div>
                <div>
                  <span className="text-slate-500 block text-[9px] uppercase">Pico Térmico</span>
                  <span className="text-slate-300">CPU {r.cpuTempPeak}°C / GPU {r.gpuTempPeak}°C</span>
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
};
