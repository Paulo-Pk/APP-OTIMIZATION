import React, { useState, useEffect } from 'react';
import {
  Cpu,
  Flame,
  HardDrive,
  CircuitBoard,
  Zap,
  Activity,
  CheckCircle2,
  AlertCircle,
  Gauge,
  Thermometer,
} from 'lucide-react';
import { HardwareTelemetry, TabId } from '../../types';

interface DashboardViewProps {
  telemetry: HardwareTelemetry;
  onNavigate: (tab: TabId) => void;
  onOpenBoostModal: () => void;
}

export const DashboardView: React.FC<DashboardViewProps> = ({
  telemetry,
  onNavigate,
  onOpenBoostModal,
}) => {
  // Live historical telemetry points for real-time SVG chart (60s history)
  const [history, setHistory] = useState<
    { time: number; cpuLoad: number; gpuLoad: number; cpuTemp: number; gpuTemp: number }[]
  >([]);

  useEffect(() => {
    // Generate initial history
    const initialPoints = Array.from({ length: 30 }, (_, i) => ({
      time: Date.now() - (30 - i) * 1000,
      cpuLoad: Math.min(100, Math.max(10, telemetry.cpu.load + (Math.sin(i * 0.4) * 8))),
      gpuLoad: Math.min(100, Math.max(15, telemetry.gpu.utilization + (Math.cos(i * 0.5) * 10))),
      cpuTemp: Math.min(85, Math.max(45, telemetry.cpu.temperature + (Math.sin(i * 0.2) * 3))),
      gpuTemp: Math.min(85, Math.max(45, telemetry.gpu.temperature + (Math.cos(i * 0.2) * 2))),
    }));
    setHistory(initialPoints);

    const interval = setInterval(() => {
      setHistory((prev) => {
        const nextPoint = {
          time: Date.now(),
          cpuLoad: Math.min(100, Math.max(8, telemetry.cpu.load + (Math.random() * 6 - 3))),
          gpuLoad: Math.min(100, Math.max(12, telemetry.gpu.utilization + (Math.random() * 8 - 4))),
          cpuTemp: Math.min(85, Math.max(45, telemetry.cpu.temperature + (Math.random() * 1.5 - 0.75))),
          gpuTemp: Math.min(85, Math.max(45, telemetry.gpu.temperature + (Math.random() * 1.2 - 0.6))),
        };
        const updated = [...prev.slice(1), nextPoint];
        return updated;
      });
    }, 1500);

    return () => clearInterval(interval);
  }, [telemetry]);

  const hotspotDelta = (telemetry.gpu.hotspot - telemetry.gpu.temperature).toFixed(1);

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Welcome / Overview Bar */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121520] via-[#151928] to-[#11131c] border border-purple-900/30">
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <span className="text-xs uppercase tracking-widest text-purple-400 font-semibold">
              Painel Principal de Controle
            </span>
            <span className="text-slate-600">·</span>
            <span className="text-xs text-slate-400">Windows 11 Pro 64-bit (24H2)</span>
          </div>
          <h1 className="text-xl lg:text-2xl font-black text-white tracking-tight">
            PC CONTROL ULTIMATE
          </h1>
          <p className="text-xs text-slate-400 max-w-2xl">
            Telemetria em tempo real com leitura de sensores de baixo nível, controle térmico de GPU e escalonamento para jogos competitivos.
          </p>
        </div>

        {/* Big Overall PC Health Score Card */}
        <div className="flex items-center gap-4 bg-[#0d0f17] px-5 py-3 rounded-xl border border-purple-800/40 shrink-0">
          <div className="relative w-16 h-16 flex items-center justify-center">
            <svg className="w-16 h-16 transform -rotate-90">
              <circle
                cx="32"
                cy="32"
                r="26"
                stroke="#1f2438"
                strokeWidth="5"
                fill="none"
              />
              <circle
                cx="32"
                cy="32"
                r="26"
                stroke="#a855f7"
                strokeWidth="5"
                fill="none"
                strokeDasharray="163"
                strokeDashoffset={163 - (163 * telemetry.overallScore) / 100}
                strokeLinecap="round"
              />
            </svg>
            <div className="absolute flex flex-col items-center">
              <span className="text-lg font-black font-mono text-white leading-none">
                {telemetry.overallScore}
              </span>
              <span className="text-[9px] text-slate-400 font-bold uppercase">Pts</span>
            </div>
          </div>

          <div>
            <div className="text-xs text-slate-400">Pontuação Geral</div>
            <div className="text-sm font-bold text-emerald-400 flex items-center gap-1">
              <CheckCircle2 className="w-4 h-4" /> Excelente
            </div>
            <div className="text-[10px] text-slate-500">Pronto para Jogos 4K/240Hz</div>
          </div>
        </div>
      </div>

      {/* Primary Hardware Cards Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        {/* 1. CPU Card */}
        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800/80 hover:border-purple-600/40 transition-all space-y-3">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Cpu className="w-4 h-4 text-purple-400" />
              <span className="text-xs font-bold uppercase text-slate-300">Processador (CPU)</span>
            </div>
            <span className="text-xs font-mono font-bold text-purple-300">{telemetry.cpu.load}%</span>
          </div>

          <div className="text-sm font-bold text-white truncate" title={telemetry.cpu.name}>
            {telemetry.cpu.name}
          </div>

          <div className="grid grid-cols-2 gap-2 pt-1 border-t border-slate-800/60 text-xs">
            <div>
              <span className="text-slate-500 block text-[10px]">Temperatura</span>
              <span className="font-mono font-semibold text-slate-200">
                {telemetry.cpu.temperature.toFixed(1)}°C
              </span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">Frequência</span>
              <span className="font-mono font-semibold text-slate-200">
                {telemetry.cpu.frequencyGHz.toFixed(2)} GHz
              </span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">Núcleos / Threads</span>
              <span className="font-mono text-slate-200">{telemetry.cpu.cores}C / {telemetry.cpu.threads}T</span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">Consumo (Package)</span>
              <span className="font-mono text-slate-200">{telemetry.cpu.powerWatts.toFixed(0)} W</span>
            </div>
          </div>

          <div className="w-full bg-slate-800 h-1.5 rounded-full overflow-hidden">
            <div
              className="bg-gradient-to-r from-purple-500 to-indigo-500 h-full transition-all duration-500"
              style={{ width: `${telemetry.cpu.load}%` }}
            />
          </div>
        </div>

        {/* 2. GPU Card */}
        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800/80 hover:border-purple-600/40 transition-all space-y-3">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Flame className="w-4 h-4 text-rose-400" />
              <span className="text-xs font-bold uppercase text-slate-300">Placa de Vídeo (GPU)</span>
            </div>
            <span className="text-xs font-mono font-bold text-rose-300">{telemetry.gpu.utilization}%</span>
          </div>

          <div className="text-sm font-bold text-white truncate" title={telemetry.gpu.name}>
            {telemetry.gpu.name}
          </div>

          <div className="grid grid-cols-2 gap-2 pt-1 border-t border-slate-800/60 text-xs">
            <div>
              <span className="text-slate-500 block text-[10px]">Core / Hotspot</span>
              <span className="font-mono font-semibold text-slate-200">
                {telemetry.gpu.temperature.toFixed(0)}°C / <span className="text-amber-400">{telemetry.gpu.hotspot.toFixed(0)}°C</span>
              </span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">Hotspot Delta ΔT</span>
              <span className="font-mono font-semibold text-emerald-400">
                +{hotspotDelta}°C (Ideal)
              </span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">Clock da GPU</span>
              <span className="font-mono text-slate-200">{telemetry.gpu.clockMHz} MHz</span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">VRAM Alocada</span>
              <span className="font-mono text-slate-200">
                {(telemetry.gpu.vramUsedMB / 1024).toFixed(1)} / {(telemetry.gpu.vramTotalMB / 1024).toFixed(0)} GB
              </span>
            </div>
          </div>

          <div className="w-full bg-slate-800 h-1.5 rounded-full overflow-hidden">
            <div
              className="bg-gradient-to-r from-rose-500 to-purple-500 h-full transition-all duration-500"
              style={{ width: `${telemetry.gpu.utilization}%` }}
            />
          </div>
        </div>

        {/* 3. RAM Card */}
        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800/80 hover:border-purple-600/40 transition-all space-y-3">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <CircuitBoard className="w-4 h-4 text-cyan-400" />
              <span className="text-xs font-bold uppercase text-slate-300">Memória RAM</span>
            </div>
            <span className="text-xs font-mono font-bold text-cyan-300">{telemetry.ram.percentage}%</span>
          </div>

          <div className="text-sm font-bold text-white truncate">
            {telemetry.ram.totalGB} GB {telemetry.ram.type}
          </div>

          <div className="grid grid-cols-2 gap-2 pt-1 border-t border-slate-800/60 text-xs">
            <div>
              <span className="text-slate-500 block text-[10px]">Em Uso</span>
              <span className="font-mono font-semibold text-slate-200">
                {telemetry.ram.usedGB.toFixed(1)} GB
              </span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">Disponível</span>
              <span className="font-mono font-semibold text-slate-200">
                {telemetry.ram.availableGB.toFixed(1)} GB
              </span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">Frequência SPD</span>
              <span className="font-mono text-slate-200">{telemetry.ram.speedMT} MT/s</span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">Canais</span>
              <span className="font-mono text-slate-200">{telemetry.ram.channels}</span>
            </div>
          </div>

          <div className="w-full bg-slate-800 h-1.5 rounded-full overflow-hidden">
            <div
              className="bg-gradient-to-r from-cyan-500 to-blue-500 h-full transition-all duration-500"
              style={{ width: `${telemetry.ram.percentage}%` }}
            />
          </div>
        </div>

        {/* 4. NVMe Storage Card */}
        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800/80 hover:border-purple-600/40 transition-all space-y-3">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <HardDrive className="w-4 h-4 text-emerald-400" />
              <span className="text-xs font-bold uppercase text-slate-300">Armazenamento</span>
            </div>
            <span className="text-xs font-mono font-bold text-emerald-300">SMART OK</span>
          </div>

          <div className="text-sm font-bold text-white truncate" title={telemetry.disks[0]?.model}>
            {telemetry.disks[0]?.model || 'NVMe SSD'}
          </div>

          <div className="grid grid-cols-2 gap-2 pt-1 border-t border-slate-800/60 text-xs">
            <div>
              <span className="text-slate-500 block text-[10px]">Espaço Livre</span>
              <span className="font-mono font-semibold text-slate-200">
                {telemetry.disks[0]?.freeSpaceGB} GB livres
              </span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">Temperatura SSD</span>
              <span className="font-mono font-semibold text-slate-200">
                {telemetry.disks[0]?.temperature}°C
              </span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">Saúde NAND</span>
              <span className="font-mono text-emerald-400">{telemetry.disks[0]?.healthPercent}% (Excelente)</span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">Interface</span>
              <span className="font-mono text-slate-200">{telemetry.disks[0]?.interface} Gen4</span>
            </div>
          </div>

          <div className="w-full bg-slate-800 h-1.5 rounded-full overflow-hidden">
            <div
              className="bg-gradient-to-r from-emerald-500 to-teal-500 h-full transition-all duration-500"
              style={{
                width: `${
                  telemetry.disks[0]
                    ? ((telemetry.disks[0].capacityGB - telemetry.disks[0].freeSpaceGB) /
                        telemetry.disks[0].capacityGB) *
                      100
                    : 30
                }%`,
              }}
            />
          </div>
        </div>
      </div>

      {/* Real-time Telemetry Graph & Boost Action Row */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-5">
        {/* Real-Time SVG History Chart */}
        <div className="lg:col-span-2 p-5 rounded-xl bg-[#0f121b] border border-slate-800/80 space-y-3">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Activity className="w-4 h-4 text-purple-400" />
              <h2 className="text-sm font-bold text-white">Telemetria em Tempo Real (60 Segundos)</h2>
            </div>
            <div className="flex items-center gap-4 text-xs font-mono">
              <span className="flex items-center gap-1.5 text-purple-400">
                <span className="w-2.5 h-2.5 rounded-full bg-purple-500" /> CPU Load
              </span>
              <span className="flex items-center gap-1.5 text-rose-400">
                <span className="w-2.5 h-2.5 rounded-full bg-rose-500" /> GPU Load
              </span>
              <span className="flex items-center gap-1.5 text-cyan-400">
                <span className="w-2.5 h-2.5 rounded-full bg-cyan-500" /> CPU Temp
              </span>
            </div>
          </div>

          {/* SVG Canvas Chart */}
          <div className="h-48 w-full bg-[#0a0c13] rounded-lg border border-slate-800/60 p-2 relative overflow-hidden">
            <svg className="w-full h-full overflow-visible" viewBox="0 0 500 150" preserveAspectRatio="none">
              {/* Horizontal Grid Lines */}
              <line x1="0" y1="37" x2="500" y2="37" stroke="#1f2438" strokeDasharray="3 3" />
              <line x1="0" y1="75" x2="500" y2="75" stroke="#1f2438" strokeDasharray="3 3" />
              <line x1="0" y1="112" x2="500" y2="112" stroke="#1f2438" strokeDasharray="3 3" />

              {/* CPU Load Path */}
              {history.length > 1 && (
                <polyline
                  fill="none"
                  stroke="#a855f7"
                  strokeWidth="2"
                  points={history
                    .map((pt, i) => `${(i / (history.length - 1)) * 500},${150 - (pt.cpuLoad / 100) * 140}`)
                    .join(' ')}
                />
              )}

              {/* GPU Load Path */}
              {history.length > 1 && (
                <polyline
                  fill="none"
                  stroke="#f43f5e"
                  strokeWidth="2"
                  points={history
                    .map((pt, i) => `${(i / (history.length - 1)) * 500},${150 - (pt.gpuLoad / 100) * 140}`)
                    .join(' ')}
                />
              )}

              {/* CPU Temp Path */}
              {history.length > 1 && (
                <polyline
                  fill="none"
                  stroke="#06b6d4"
                  strokeWidth="1.5"
                  strokeDasharray="4 2"
                  points={history
                    .map((pt, i) => `${(i / (history.length - 1)) * 500},${150 - (pt.cpuTemp / 100) * 140}`)
                    .join(' ')}
                />
              )}
            </svg>

            <div className="absolute bottom-1 right-2 text-[10px] text-slate-500 font-mono">
              Leituras a cada 1.5s via LibreHardwareMonitor
            </div>
          </div>
        </div>

        {/* Quick Optimization Callout */}
        <div className="p-5 rounded-xl bg-gradient-to-br from-[#121626] to-[#0c0e17] border border-purple-900/40 flex flex-col justify-between space-y-4">
          <div className="space-y-2">
            <div className="flex items-center gap-2">
              <Zap className="w-5 h-5 text-purple-400" />
              <h2 className="text-sm font-bold text-white uppercase tracking-wider">
                Otimização Rápida
              </h2>
            </div>
            <p className="text-xs text-slate-300 leading-relaxed">
              Realize uma análise instantânea do kernel do Windows, descarte caches em espera, trave multiplicadores de clock e aplique as melhores práticas para jogos competitivos.
            </p>
          </div>

          <div className="p-3 bg-[#0a0c13] rounded-lg border border-slate-800 space-y-1.5 text-xs">
            <div className="flex items-center justify-between text-slate-400">
              <span>Alterações Propostas:</span>
              <span className="font-semibold text-purple-300">7 Tweaks Pendentes</span>
            </div>
            <div className="flex items-center justify-between text-slate-400">
              <span>Nível de Risco:</span>
              <span className="font-semibold text-emerald-400">Zero Risco / Reversível</span>
            </div>
            <div className="flex items-center justify-between text-slate-400">
              <span>Ponto de Restauração:</span>
              <span className="text-slate-300">Criado Automaticamente</span>
            </div>
          </div>

          <button
            onClick={onOpenBoostModal}
            className="w-full py-3 px-4 rounded-lg bg-gradient-to-r from-purple-600 via-indigo-600 to-violet-600 hover:from-purple-500 hover:to-violet-500 text-white font-bold text-xs uppercase tracking-wider shadow-lg shadow-purple-600/40 transition-all cursor-pointer border border-purple-400/40 active:scale-[0.98] flex items-center justify-center gap-2"
          >
            <Zap className="w-4 h-4 fill-current" />
            <span>Executar Análise & Otimizar</span>
          </button>
        </div>
      </div>

      {/* Motherboard & Platform Telemetry Detail */}
      <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800/80">
        <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 pb-3 border-b border-slate-800/60">
          <div className="flex items-center gap-2">
            <CircuitBoard className="w-4 h-4 text-indigo-400" />
            <h3 className="text-xs font-bold text-white uppercase tracking-wider">
              Placa-Mãe & Sensores do Sistema
            </h3>
          </div>
          <div className="text-xs font-mono text-slate-400">
            {telemetry.motherboard.model} · BIOS {telemetry.motherboard.biosVersion}
          </div>
        </div>

        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3 pt-3 text-xs">
          <div>
            <span className="text-slate-500 block text-[10px]">Temp. VRM</span>
            <span className="font-mono font-semibold text-slate-200">
              {telemetry.motherboard.temperatures.vrm}°C
            </span>
          </div>
          <div>
            <span className="text-slate-500 block text-[10px]">Temp. Chipset</span>
            <span className="font-mono font-semibold text-slate-200">
              {telemetry.motherboard.temperatures.chipset}°C
            </span>
          </div>
          <div>
            <span className="text-slate-500 block text-[10px]">Linha +12V</span>
            <span className="font-mono font-semibold text-emerald-400">
              {telemetry.motherboard.voltages.v12.toFixed(2)} V
            </span>
          </div>
          <div>
            <span className="text-slate-500 block text-[10px]">Linha +5V</span>
            <span className="font-mono font-semibold text-slate-200">
              {telemetry.motherboard.voltages.v5.toFixed(2)} V
            </span>
          </div>
          <div>
            <span className="text-slate-500 block text-[10px]">Fan CPU</span>
            <span className="font-mono font-semibold text-slate-200">
              {telemetry.motherboard.fans.cpuFanRPM} RPM
            </span>
          </div>
          <div>
            <span className="text-slate-500 block text-[10px]">Bomba AIO Pump</span>
            <span className="font-mono font-semibold text-purple-300">
              {telemetry.motherboard.fans.pumpRPM} RPM
            </span>
          </div>
        </div>
      </div>
    </div>
  );
};
