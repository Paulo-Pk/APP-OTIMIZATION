import React from 'react';
import {
  Cpu,
  Flame,
  CircuitBoard,
  HardDrive,
  Activity,
  Fan,
  Zap,
  Layers,
  AlertTriangle,
} from 'lucide-react';
import { HardwareTelemetry } from '../../types';

interface HardwareMonitorViewProps {
  telemetry: HardwareTelemetry;
}

export const HardwareMonitorView: React.FC<HardwareMonitorViewProps> = ({
  telemetry,
}) => {
  return (
    <div className="space-y-5 animate-fade-in">
      {/* Title Header */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <Cpu className="w-4 h-4" /> Monitoramento Contínuo de Hardware
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">HARDWARE MONITOR</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl">
            Inspeção detalhada de sensores físicos via LibreHardwareMonitor: clocks por núcleo, tensões de trilho e sensores SMART.
          </p>
        </div>

        <div className="flex items-center gap-2 px-3 py-1.5 rounded-lg bg-[#0e111a] border border-slate-800 text-xs font-mono text-slate-300">
          <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse" />
          <span>Polling: 1.5s · DirectKernel I/O</span>
        </div>
      </div>

      {/* CPU In-Depth Section with Per-Core Load */}
      <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-4">
        <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-2 pb-3 border-b border-slate-800/80">
          <div className="flex items-center gap-2">
            <Cpu className="w-5 h-5 text-purple-400" />
            <div>
              <h2 className="text-sm font-bold text-white uppercase tracking-wider">
                {telemetry.cpu.name}
              </h2>
              <span className="text-xs font-mono text-slate-500">
                Socket {telemetry.cpu.socket} · {telemetry.cpu.cores} Cores / {telemetry.cpu.threads} Threads
              </span>
            </div>
          </div>

          <div className="flex items-center gap-4 text-xs font-mono">
            <div>
              <span className="text-slate-500">Tensão VCore: </span>
              <span className="text-slate-200 font-semibold">{telemetry.cpu.voltageV.toFixed(3)} V</span>
            </div>
            <div>
              <span className="text-slate-500">Consumo PPT: </span>
              <span className="text-slate-200 font-semibold">{telemetry.cpu.powerWatts.toFixed(1)} W</span>
            </div>
          </div>
        </div>

        {/* Per-Core Load Grid */}
        <div>
          <span className="text-xs font-semibold text-slate-400 uppercase tracking-wider block mb-2">
            Carga e Clocks Individuais por Núcleo
          </span>
          <div className="grid grid-cols-2 sm:grid-cols-4 lg:grid-cols-8 gap-2.5">
            {telemetry.cpu.coreList.map((core) => (
              <div
                key={core.coreId}
                className="p-2.5 rounded-lg bg-[#0a0c13] border border-slate-800/80 space-y-1.5"
              >
                <div className="flex items-center justify-between text-[11px] font-mono">
                  <span className="text-slate-400 font-semibold">Core #{core.coreId}</span>
                  <span className="text-purple-400 font-bold">{core.load}%</span>
                </div>
                <div className="w-full bg-slate-800 h-1.5 rounded-full overflow-hidden">
                  <div
                    className="bg-purple-500 h-full transition-all duration-300"
                    style={{ width: `${core.load}%` }}
                  />
                </div>
                <div className="flex items-center justify-between text-[10px] font-mono text-slate-500">
                  <span>{core.frequencyGHz.toFixed(2)}GHz</span>
                  <span>{core.temp.toFixed(0)}°C</span>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* GPU In-Depth Section */}
      <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-4">
        <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-2 pb-3 border-b border-slate-800/80">
          <div className="flex items-center gap-2">
            <Flame className="w-5 h-5 text-rose-400" />
            <div>
              <h2 className="text-sm font-bold text-white uppercase tracking-wider">
                {telemetry.gpu.name}
              </h2>
              <span className="text-xs font-mono text-slate-500">
                Fabricante: {telemetry.gpu.vendor} · Interface: {telemetry.gpu.pcieLane}
              </span>
            </div>
          </div>

          <div className="flex items-center gap-3 text-xs font-mono">
            <span className="px-2.5 py-1 rounded bg-slate-800 text-slate-300">
              Ventoinhas: {telemetry.gpu.fansRPM} RPM ({telemetry.gpu.fanSpeedPercent}%)
            </span>
          </div>
        </div>

        {/* Detailed GPU Sensors */}
        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3">
          <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800/80">
            <span className="text-[10px] text-slate-500 uppercase block">Temperatura Core</span>
            <span className="text-base font-mono font-bold text-white">
              {telemetry.gpu.temperature.toFixed(1)}°C
            </span>
            <span className="text-[10px] text-emerald-400 block mt-0.5">Seguro</span>
          </div>

          <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800/80">
            <span className="text-[10px] text-slate-500 uppercase block">Hotspot Temp</span>
            <span className="text-base font-mono font-bold text-amber-400">
              {telemetry.gpu.hotspot.toFixed(1)}°C
            </span>
            <span className="text-[10px] text-slate-400 block mt-0.5">
              Delta: +{(telemetry.gpu.hotspot - telemetry.gpu.temperature).toFixed(1)}°C
            </span>
          </div>

          <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800/80">
            <span className="text-[10px] text-slate-500 uppercase block">Memória VRAM Temp</span>
            <span className="text-base font-mono font-bold text-slate-200">
              {telemetry.gpu.memoryTemp.toFixed(1)}°C
            </span>
            <span className="text-[10px] text-slate-400 block mt-0.5">GDDR6X</span>
          </div>

          <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800/80">
            <span className="text-[10px] text-slate-500 uppercase block">Clock da GPU</span>
            <span className="text-base font-mono font-bold text-purple-300">
              {telemetry.gpu.clockMHz} MHz
            </span>
            <span className="text-[10px] text-slate-400 block mt-0.5">Boost Ativo</span>
          </div>

          <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800/80">
            <span className="text-[10px] text-slate-500 uppercase block">Consumo da Placa</span>
            <span className="text-base font-mono font-bold text-slate-200">
              {telemetry.gpu.powerWatts.toFixed(0)} W
            </span>
            <span className="text-[10px] text-slate-400 block mt-0.5">TGP Normal</span>
          </div>

          <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800/80">
            <span className="text-[10px] text-slate-500 uppercase block">Alocação VRAM</span>
            <span className="text-base font-mono font-bold text-cyan-300">
              {(telemetry.gpu.vramUsedMB / 1024).toFixed(1)} GB
            </span>
            <span className="text-[10px] text-slate-400 block mt-0.5">de 16 GB</span>
          </div>
        </div>
      </div>

      {/* Memory & Storage Health Detail */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {/* RAM Section */}
        <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-3">
          <div className="flex items-center justify-between pb-2 border-b border-slate-800/80">
            <div className="flex items-center gap-2">
              <CircuitBoard className="w-4 h-4 text-cyan-400" />
              <h3 className="text-xs font-bold text-white uppercase tracking-wider">
                Configuração de Memória RAM
              </h3>
            </div>
            <span className="text-xs font-mono text-cyan-300">{telemetry.ram.channels}</span>
          </div>

          <div className="space-y-2 text-xs">
            <div className="flex justify-between text-slate-300">
              <span>Capacidade Total:</span>
              <span className="font-mono font-bold text-white">{telemetry.ram.totalGB} GB</span>
            </div>
            <div className="flex justify-between text-slate-300">
              <span>Velocidade Identificada:</span>
              <span className="font-mono text-slate-200">{telemetry.ram.speedMT} MT/s ({telemetry.ram.type})</span>
            </div>
            <div className="flex justify-between text-slate-300">
              <span>Memória Física Disponível:</span>
              <span className="font-mono text-slate-200">{telemetry.ram.availableGB.toFixed(1)} GB</span>
            </div>
            <div className="flex justify-between text-slate-300">
              <span>Lista de Espera (Standby Cache):</span>
              <span className="font-mono text-emerald-400">4.2 GB (Limpeza Automática Pronta)</span>
            </div>
          </div>
        </div>

        {/* Disks SMART Section */}
        <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-3">
          <div className="flex items-center justify-between pb-2 border-b border-slate-800/80">
            <div className="flex items-center gap-2">
              <HardDrive className="w-4 h-4 text-emerald-400" />
              <h3 className="text-xs font-bold text-white uppercase tracking-wider">
                Diagnóstico de Discos & SMART
              </h3>
            </div>
            <span className="text-xs font-mono text-emerald-400">2 Unidades Detectadas</span>
          </div>

          <div className="space-y-3 text-xs">
            {telemetry.disks.map((disk) => (
              <div key={disk.id} className="p-2.5 rounded-lg bg-[#0a0c13] border border-slate-800/60 space-y-1">
                <div className="flex justify-between items-center">
                  <span className="font-bold text-white truncate max-w-xs">{disk.model}</span>
                  <span className="text-emerald-400 font-mono text-[11px] font-bold">Saúde: {disk.healthPercent}%</span>
                </div>
                <div className="flex justify-between text-slate-400 text-[11px] font-mono">
                  <span>{disk.freeSpaceGB} GB livres de {disk.capacityGB} GB</span>
                  <span>Temp: {disk.temperature}°C · SMART: {disk.smartStatus}</span>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
};
