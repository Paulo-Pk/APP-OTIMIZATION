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
import { useI18n } from '../../i18n';

interface BenchmarkCenterViewProps {
  telemetry: HardwareTelemetry;
}

export const BenchmarkCenterView: React.FC<BenchmarkCenterViewProps> = ({
  telemetry,
}) => {
  const { t } = useI18n();

  const [records, setRecords] = useState<BenchmarkRecord[]>([
    {
      id: 'bm-1',
      type: 'CPU',
      score: 11240,
      metric: 'MIPS / AVX',
      date: `${t.common.today}, 10:14`,
      hardware: telemetry.cpu.name,
      durationMs: 342,
      cpuTempPeak: 66,
      gpuTempPeak: 48,
      notes: 'x86 Prime Sieve Arithmetic Precision',
    },
    {
      id: 'bm-2',
      type: 'GPU',
      score: 18450,
      metric: 'Shader Compute / FPS',
      date: `${t.common.today}, 10:16`,
      hardware: telemetry.gpu.name,
      durationMs: 410,
      cpuTempPeak: 58,
      gpuTempPeak: 62,
      notes: 'WebGL2 High-Density Raymarching',
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
        metric: t.benchmarks.metricIpc,
        date: t.benchmarks.justNow,
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
        date: t.benchmarks.justNow,
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
        date: t.benchmarks.justNow,
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
            <BarChart3 className="w-4 h-4" /> {t.benchmarks.subtitle}
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">{t.benchmarks.title}</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl leading-relaxed">
            {t.benchmarks.description}
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            onClick={() => handleRunCpu()}
            disabled={isRunning}
            className="flex items-center gap-2 px-4 py-2 rounded-lg bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white font-bold text-xs uppercase tracking-wider shadow-lg shadow-purple-600/30 transition-all cursor-pointer border border-purple-400/40 disabled:opacity-50"
          >
            <Play className="w-4 h-4 fill-current" />
            <span>{t.benchmarks.runBenchmark}</span>
          </button>
        </div>
      </div>

      {/* Progress status if test running */}
      {isRunning && (
        <div className="p-4 rounded-xl bg-[#141829] border border-purple-500 space-y-2 animate-scale-in">
          <div className="flex justify-between items-center text-xs">
            <span className="font-bold text-purple-300">
              {t.benchmarks.runningTest} {activeTest}: {progressState.currentTest}
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
            <h3 className="font-bold text-sm text-white">{t.benchmarks.cpuTestTitle}</h3>
            <p className="text-xs text-slate-400 leading-relaxed">
              {t.benchmarks.cpuTestDesc}
            </p>
          </div>
          <button
            onClick={handleRunCpu}
            disabled={isRunning}
            className="w-full py-2 px-3 rounded-lg bg-[#141726] hover:bg-[#1c2238] border border-purple-500/30 text-purple-200 text-xs font-semibold cursor-pointer transition-colors disabled:opacity-50"
          >
            {t.benchmarks.testCpu}
          </button>
        </div>

        {/* GPU Benchmark Card */}
        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-3 flex flex-col justify-between">
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <Flame className="w-5 h-5 text-rose-400" />
              <span className="text-[10px] font-mono text-slate-500">WebGL2 Raymarching</span>
            </div>
            <h3 className="font-bold text-sm text-white">{t.benchmarks.gpuTestTitle}</h3>
            <p className="text-xs text-slate-400 leading-relaxed">
              {t.benchmarks.gpuTestDesc}
            </p>
          </div>
          <button
            onClick={handleRunGpu}
            disabled={isRunning}
            className="w-full py-2 px-3 rounded-lg bg-[#141726] hover:bg-[#1c2238] border border-rose-500/30 text-rose-200 text-xs font-semibold cursor-pointer transition-colors disabled:opacity-50"
          >
            {t.benchmarks.testGpu}
          </button>
        </div>

        {/* RAM Benchmark Card */}
        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-3 flex flex-col justify-between">
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <CircuitBoard className="w-5 h-5 text-cyan-400" />
              <span className="text-[10px] font-mono text-slate-500">32MB Block Transfer</span>
            </div>
            <h3 className="font-bold text-sm text-white">{t.benchmarks.ramTestTitle}</h3>
            <p className="text-xs text-slate-400 leading-relaxed">
              {t.benchmarks.ramTestDesc}
            </p>
          </div>
          <button
            onClick={handleRunRam}
            disabled={isRunning}
            className="w-full py-2 px-3 rounded-lg bg-[#141726] hover:bg-[#1c2238] border border-cyan-500/30 text-cyan-200 text-xs font-semibold cursor-pointer transition-colors disabled:opacity-50"
          >
            {t.benchmarks.testRam}
          </button>
        </div>
      </div>

      {/* Benchmark History Log Table */}
      <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-3">
        <div className="flex items-center justify-between pb-2 border-b border-slate-800">
          <div className="flex items-center gap-2">
            <Clock className="w-4 h-4 text-purple-400" />
            <h3 className="text-xs font-bold text-white uppercase tracking-wider">
              {t.benchmarks.historyTitle}
            </h3>
          </div>
          <span className="text-xs font-mono text-slate-500">
            {records.length} {t.benchmarks.totalTests}
          </span>
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
                  <span className="text-slate-500 block text-[9px] uppercase">{t.benchmarks.score}</span>
                  <span className="text-base font-bold text-purple-300">
                    {r.score.toLocaleString()} <span className="text-[10px] text-slate-400 font-normal">{r.metric}</span>
                  </span>
                </div>
                <div>
                  <span className="text-slate-500 block text-[9px] uppercase">{t.benchmarks.duration}</span>
                  <span className="text-slate-300">{r.durationMs}ms</span>
                </div>
                <div>
                  <span className="text-slate-500 block text-[9px] uppercase">{t.benchmarks.peakTemp}</span>
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
