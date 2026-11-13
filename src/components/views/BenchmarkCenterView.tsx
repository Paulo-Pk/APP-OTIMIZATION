import React, { useState } from 'react';
import {
  BarChart3,
  Play,
  Cpu,
  Flame,
  CircuitBoard,
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

  // Start empty — records are only populated after a user actually runs a test
  const [records, setRecords] = useState<BenchmarkRecord[]>([]);

  const [activeTest, setActiveTest] = useState<string | null>(null);
  const [testError, setTestError] = useState<string | null>(null);
  const [progressState, setProgressState] = useState<BenchmarkProgress>({
    status: 'idle',
    progress: 0,
    currentTest: '',
  });

  const handleRunCpu = async () => {
    setActiveTest('CPU');
    setTestError(null);
    try {
      const result = await runCpuBenchmark(setProgressState);
      const newRecord: BenchmarkRecord = {
        id: `bm-${Date.now()}`,
        type: 'CPU',
        score: result.score,
        metric: t.benchmarks.metricIpc,
        date: t.benchmarks.justNow,
        hardware: telemetry.cpu.name || t.common.sensorNotAvailable,
        durationMs: Math.round(result.durationMs),
        cpuTempPeak: telemetry.cpu.temperature > 0 ? Math.round(telemetry.cpu.temperature) : 0,
        gpuTempPeak: telemetry.gpu.temperature > 0 ? Math.round(telemetry.gpu.temperature) : 0,
        notes: `${result.details} (Teste Sintético Navegador V8)`,
      };
      setRecords((prev) => [newRecord, ...prev]);
    } catch (error) {
      setTestError(error instanceof Error ? error.message : 'Não foi possível concluir o teste do processador.');
    } finally {
      setActiveTest(null);
    }
  };

  const handleRunGpu = async () => {
    setActiveTest('GPU');
    setTestError(null);
    try {
      const result = await runGpuBenchmark(setProgressState);
      const newRecord: BenchmarkRecord = {
        id: `bm-${Date.now()}`,
        type: 'GPU',
        score: result.score,
        metric: 'FPS de renderização',
        date: t.benchmarks.justNow,
        hardware: telemetry.gpu.name || t.common.sensorNotAvailable,
        durationMs: Math.round(result.durationMs),
        cpuTempPeak: telemetry.cpu.temperature > 0 ? Math.round(telemetry.cpu.temperature) : 0,
        gpuTempPeak: telemetry.gpu.temperature > 0 ? Math.round(telemetry.gpu.temperature) : 0,
        notes: result.details,
      };
      setRecords((prev) => [newRecord, ...prev]);
    } catch (error) {
      setTestError(error instanceof Error ? error.message : 'Não foi possível concluir o teste da placa de vídeo. Verifique se o navegador permite WebGL2.');
    } finally {
      setActiveTest(null);
    }
  };

  const handleRunRam = async () => {
    setActiveTest('RAM');
    setTestError(null);
    try {
      const result = await runRamBenchmark(setProgressState);
      const newRecord: BenchmarkRecord = {
        id: `bm-${Date.now()}`,
        type: 'RAM',
        score: result.score,
        metric: 'MB/s Throughput',
        date: t.benchmarks.justNow,
        hardware:
          telemetry.ram.totalGB > 0
            ? `${telemetry.ram.totalGB}GB ${telemetry.ram.type}`
            : t.common.sensorNotAvailable,
        durationMs: Math.round(result.durationMs),
        cpuTempPeak: telemetry.cpu.temperature > 0 ? Math.round(telemetry.cpu.temperature) : 0,
        gpuTempPeak: telemetry.gpu.temperature > 0 ? Math.round(telemetry.gpu.temperature) : 0,
        notes: `${result.details} (Teste Sintético ArrayBuffer)`,
      };
      setRecords((prev) => [newRecord, ...prev]);
    } catch (error) {
      setTestError(error instanceof Error ? error.message : 'Não foi possível concluir o teste de memória.');
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

      {/* Runtime Environment Notice */}
      <div className="p-3.5 rounded-xl bg-[#0f1424] border border-cyan-500/30 text-xs flex items-start gap-3">
        <Sparkles className="w-4 h-4 text-cyan-400 shrink-0 mt-0.5" />
        <div className="space-y-0.5">
          <div className="font-bold text-cyan-300">
            Como os testes funcionam
          </div>
          <p className="text-slate-400 leading-relaxed text-[11px]">
            O teste de GPU renderiza uma cena gráfica usando a aceleração disponível no navegador e mede os quadros por segundo. O resultado é uma comparação sintética, não substitui um benchmark profissional de jogos ou 3D.
          </p>
        </div>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
        <div className="rounded-xl border border-purple-500/30 bg-gradient-to-br from-[#171329] to-[#0f121b] p-4">
          <div className="text-[10px] font-bold uppercase tracking-wider text-purple-300">Processador detectado</div>
          <div className="mt-2 text-sm font-bold text-white truncate">{telemetry.cpu.name || 'Não identificado'}</div>
          <div className="mt-1 text-[11px] text-slate-400">Temperatura atual: {telemetry.cpu.temperature > 0 ? telemetry.cpu.temperature.toFixed(1) + '°C' : 'N/D'}</div>
        </div>
        <div className="rounded-xl border border-rose-500/30 bg-gradient-to-br from-[#21131a] to-[#0f121b] p-4">
          <div className="text-[10px] font-bold uppercase tracking-wider text-rose-300">Placa de vídeo detectada</div>
          <div className="mt-2 text-sm font-bold text-white truncate">{telemetry.gpu.name || 'Não identificada'}</div>
          <div className="mt-1 text-[11px] text-slate-400">Temperatura: {telemetry.gpu.temperature > 0 ? telemetry.gpu.temperature.toFixed(1) + '°C' : 'N/D'} · Uso: {telemetry.gpu.utilization > 0 ? telemetry.gpu.utilization.toFixed(0) + '%' : 'N/D'}</div>
        </div>
        <div className="rounded-xl border border-cyan-500/30 bg-gradient-to-br from-[#10202a] to-[#0f121b] p-4">
          <div className="text-[10px] font-bold uppercase tracking-wider text-cyan-300">Memória RAM</div>
          <div className="mt-2 text-sm font-bold text-white">{telemetry.ram.totalGB > 0 ? telemetry.ram.totalGB + ' GB' : 'Não identificada'}</div>
          <div className="mt-1 text-[11px] text-slate-400">Em uso: {telemetry.ram.usedGB > 0 ? telemetry.ram.usedGB.toFixed(1) + ' GB' : 'N/D'}</div>
        </div>
      </div>

      {testError && <div className="rounded-lg border border-rose-500/40 bg-rose-500/10 px-4 py-3 text-xs text-rose-200">{testError}</div>}

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

      {benchmarkError && (
        <div role="alert" className="p-3.5 rounded-xl bg-rose-950/30 border border-rose-500/40 text-sm text-rose-200">
          <div className="font-bold mb-1">O teste da GPU não foi concluído</div>
          <p className="text-xs text-rose-100/90">{benchmarkError}</p>
        </div>
      )}

      {/* Test Modules Selection Grid */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        {/* CPU Benchmark Card */}
        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-3 flex flex-col justify-between">
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <Cpu className="w-5 h-5 text-purple-400" />
              <span className="text-[10px] font-mono text-slate-500">V8 Arithmetic Sieve</span>
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
              <span className="text-[10px] font-mono text-slate-400">Teste gráfico acelerado</span>
            </div>
            <h3 className="font-bold text-sm text-white">{t.benchmarks.gpuTestTitle}</h3>
            <p className="text-xs text-slate-400 leading-relaxed">
              Executa uma carga gráfica na placa de vídeo e mede quantos quadros ela consegue renderizar por segundo. Requer aceleração de hardware no navegador.
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

      {/* Visual summary of the tests actually executed */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800"><div className="text-[10px] uppercase text-slate-500">Testes nesta sessão</div><div className="text-2xl font-black text-white mt-1">{records.length}</div><div className="text-[11px] text-slate-400">Somente execuções concluídas</div></div>
        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800"><div className="text-[10px] uppercase text-slate-500">Último resultado</div><div className="text-2xl font-black text-purple-300 mt-1">{records.length ? records[0].score.toLocaleString() : '—'}</div><div className="text-[11px] text-slate-400">{records.length ? records[0].type + ' · ' + records[0].metric : 'Execute um teste para começar'}</div></div>
        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800"><div className="text-[10px] uppercase text-slate-500">Tempo do último teste</div><div className="text-2xl font-black text-cyan-300 mt-1">{records.length ? records[0].durationMs + ' ms' : '—'}</div><div className="text-[11px] text-slate-400">Duração medida pelo cronômetro</div></div>
      </div>
      <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-3">
        <div><h3 className="text-sm font-bold text-white">Comparativo visual dos resultados</h3><p className="text-[11px] text-slate-400">Cada tipo de teste é normalizado separadamente. Pontuações de CPU, GPU e RAM não são comparáveis entre si.</p></div>
        {records.length === 0 ? <div className="h-24 flex items-center justify-center rounded-lg bg-[#0a0c13] border border-slate-800 text-xs text-slate-500">O gráfico será preenchido após os primeiros testes</div> : (
          <div className="space-y-3">
            {(['CPU', 'GPU', 'RAM'] as const).map((kind) => {
              const group = records.filter((record) => record.type === kind).slice(0, 5).reverse();
              const maxScore = Math.max(1, ...group.map((record) => record.score));
              if (group.length === 0) return null;
              return <div key={kind} className="space-y-2"><div className="text-[11px] font-bold text-slate-300">{kind}</div>{group.map((record) => <div key={record.id} className="grid grid-cols-[minmax(70px,0.7fr)_3fr_auto] gap-3 items-center text-[11px]"><span className="text-slate-500 truncate">{record.date}</span><div className="h-2.5 rounded-full bg-slate-800 overflow-hidden"><div className="h-full rounded-full bg-purple-500" style={{ width: (record.score / maxScore * 100) + '%' }} /></div><span className="font-mono text-slate-200">{record.score.toLocaleString()}</span></div>)}</div>;
            })}
          </div>
        )}
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

        {records.length === 0 ? (
          <div className="py-6 text-center text-xs text-slate-500 font-mono">
            Nenhum teste executado nesta sessão. Clique em um dos testes acima para iniciar.
          </div>
        ) : (
          <div className="divide-y divide-slate-800/60">
            {records.map((r) => (
              <div
                key={r.id}
                className="py-3 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 text-xs"
              >
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
                    <span className="text-slate-500 block text-[9px] uppercase">
                      {t.benchmarks.score}
                    </span>
                    <span className="text-base font-bold text-purple-300">
                      {r.score.toLocaleString()}{' '}
                      <span className="text-[10px] text-slate-400 font-normal">{r.metric}</span>
                    </span>
                  </div>
                  <div>
                    <span className="text-slate-500 block text-[9px] uppercase">
                      {t.benchmarks.duration}
                    </span>
                    <span className="text-slate-300">{r.durationMs}ms</span>
                  </div>
                  <div>
                    <span className="text-slate-500 block text-[9px] uppercase">
                      {t.benchmarks.peakTemp}
                    </span>
                    <span className="text-slate-300">
                      {r.cpuTempPeak > 0
                        ? `CPU ${r.cpuTempPeak}°C / GPU ${r.gpuTempPeak}°C`
                        : t.common.sensorNotAvailable}
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
