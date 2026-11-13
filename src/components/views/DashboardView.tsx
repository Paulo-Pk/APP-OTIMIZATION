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
} from 'lucide-react';
import { HardwareTelemetry, TabId } from '../../types';
import { useI18n } from '../../i18n';

interface DashboardViewProps {
  telemetry: HardwareTelemetry;
  onNavigate: (tab: TabId) => void;
  onOpenBoostModal: () => void;
}

export const DashboardView: React.FC<DashboardViewProps> = ({
  telemetry,
  onOpenBoostModal,
}) => {
  const { t } = useI18n();

  const isTelemetryAvailable =
    telemetry.cpu.cores > 0 ||
    telemetry.cpu.load > 0 ||
    telemetry.gpu.utilization > 0 ||
    telemetry.ram.totalGB > 0 ||
    (telemetry.sensors?.some((sensor) => sensor.isAvailable && sensor.value !== null) ?? false);

  // A zero load can be a valid idle reading. Prefer the sensor availability
  // metadata so an unavailable CPU/GPU load is not displayed as a measured 0%.
  const hasCpuLoad = telemetry.sensors
    ? telemetry.sensors.some(
        (sensor) =>
          sensor.hardwareType.toLowerCase() === 'cpu' &&
          sensor.sensorType.toLowerCase() === 'load' &&
          /total/i.test(sensor.name) &&
          sensor.isAvailable
      )
    : telemetry.cpu.load > 0;

  const hasGpuLoad = telemetry.sensors
    ? telemetry.sensors.some(
        (sensor) =>
          /^gpu/i.test(sensor.hardwareType) &&
          sensor.sensorType.toLowerCase() === 'load' &&
          /core/i.test(sensor.name) &&
          sensor.isAvailable
      )
    : telemetry.gpu.utilization > 0;

  // Live historical telemetry points recorded strictly from real incoming telemetry
  const [history, setHistory] = useState<
    { time: number; cpuLoad: number | null; gpuLoad: number | null; cpuTemp: number | null; gpuTemp: number | null }[]
  >([]);

  useEffect(() => {
    if (!isTelemetryAvailable) {
      setHistory([]);
      return;
    }

    setHistory((prev) => {
      const nextPoint = {
        time: Date.now(),
        // Missing sensors stay null; an unavailable reading must not become a
        // false 0% load or 0°C line in the historical chart.
        cpuLoad: hasCpuLoad ? Math.min(100, Math.max(0, telemetry.cpu.load)) : null,
        gpuLoad: hasGpuLoad ? Math.min(100, Math.max(0, telemetry.gpu.utilization)) : null,
        cpuTemp: (telemetry.cpu.temperatureAvailable ?? (telemetry.cpu.temperature > 0))
          ? Math.min(110, Math.max(0, telemetry.cpu.temperature))
          : null,
        gpuTemp: (telemetry.gpu.temperatureAvailable ?? (telemetry.gpu.temperature > 0))
          ? Math.min(110, Math.max(0, telemetry.gpu.temperature))
          : null,
      };
      return [...prev.slice(-29), nextPoint];
    });
  }, [
    isTelemetryAvailable,
    telemetry.cpu.load,
    telemetry.gpu.utilization,
    telemetry.cpu.temperature,
    telemetry.gpu.temperature,
    hasCpuLoad,
    hasGpuLoad,
  ]);

  const hasGpuCoreTemperature = telemetry.gpu.temperatureAvailable ?? telemetry.gpu.temperature > 0;
  const hasGpuHotspotTemperature = telemetry.gpu.hotspotAvailable ?? (telemetry.gpu.hotspot > 0);
  const hasGpuTemps = hasGpuCoreTemperature && hasGpuHotspotTemperature;
  const hotspotDelta = hasGpuTemps
    ? (telemetry.gpu.hotspot - telemetry.gpu.temperature).toFixed(1)
    : null;

  // Physical storage sensor entries may not include volume capacity/free space.
  // Prefer a mounted volume for capacity metrics and keep physical temperature
  // as a separate fallback when the volume itself has no temperature sensor.
  const primaryDisk = telemetry.disks.find((disk) => disk.isSystemDisk && disk.capacityGB > 0)
    ?? telemetry.disks.find((disk) => disk.capacityGB > 0)
    ?? telemetry.disks[0];
  const diskTemperature = telemetry.disks.find((disk) => disk.temperature > 0)?.temperature
    ?? (primaryDisk?.temperature || 0);
  const diskUsagePercent = primaryDisk && primaryDisk.capacityGB > 0
    ? Math.min(100, Math.max(0, ((primaryDisk.capacityGB - primaryDisk.freeSpaceGB) / primaryDisk.capacityGB) * 100))
    : 0;

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Welcome / Overview Bar */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121520] via-[#151928] to-[#11131c] border border-purple-900/30">
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <span className="text-xs uppercase tracking-widest text-purple-400 font-semibold">
              {t.dashboard.panelTitle}
            </span>
            <span className="text-slate-600">·</span>
            <span className="text-xs text-slate-400">{t.dashboard.windowsVersion}</span>
          </div>
          <h1 className="text-xl lg:text-2xl font-black text-white tracking-tight">
            PC CONTROL ULTIMATE
          </h1>
          <p className="text-xs text-slate-400 max-w-2xl leading-relaxed">
            {t.dashboard.heroDescription}
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
                {telemetry.overallScore > 0 ? telemetry.overallScore : '--'}
              </span>
              <span className="text-[9px] text-slate-400 font-bold uppercase">{t.common.pts}</span>
            </div>
          </div>

          <div>
            <div className="text-xs text-slate-400">{t.common.overallHealth}</div>
            {telemetry.overallScore > 0 ? (
              <div className="text-sm font-bold text-emerald-400 flex items-center gap-1">
                <CheckCircle2 className="w-4 h-4" /> {t.common.excellent}
              </div>
            ) : (
              <div className="text-sm font-bold text-amber-400 flex items-center gap-1">
                <AlertCircle className="w-4 h-4" /> {t.common.sensorNotAvailable}
              </div>
            )}
            <div className="text-[10px] text-slate-500">{t.dashboard.readyForGaming}</div>
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
              <span className="text-xs font-bold uppercase text-slate-300">
                {t.dashboard.cpuCardTitle}
              </span>
            </div>
            <span className="text-xs font-mono font-bold text-purple-300">
              {hasCpuLoad ? `${telemetry.cpu.load}%` : t.common.sensorNotAvailable}
            </span>
          </div>

          <div className="text-sm font-bold text-white truncate" title={telemetry.cpu.name}>
            {telemetry.cpu.name || t.common.sensorNotAvailable}
          </div>

          <div className="grid grid-cols-2 gap-2 pt-1 border-t border-slate-800/60 text-xs">
            <div>
              <span className="text-slate-500 block text-[10px]">{t.dashboard.temp}</span>
              <span className="font-mono font-semibold text-slate-200">
                {(telemetry.cpu.temperatureAvailable ?? (telemetry.cpu.temperature > 0))
                  ? `${telemetry.cpu.temperature.toFixed(1)}°C`
                  : t.common.sensorNotAvailable}
              </span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">{t.dashboard.frequency}</span>
              <span className="font-mono font-semibold text-slate-200">
                {telemetry.cpu.frequencyGHz > 0
                  ? `${Math.round(telemetry.cpu.frequencyGHz * 1000).toLocaleString('pt-BR')} MHz`
                  : t.common.sensorNotAvailable}
              </span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">{t.dashboard.coresThreads}</span>
              <span className="font-mono text-slate-200">
                {telemetry.cpu.cores > 0
                  ? `${telemetry.cpu.cores}C / ${telemetry.cpu.threads}T`
                  : t.common.sensorNotAvailable}
              </span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">{t.dashboard.powerPackage}</span>
              <span className="font-mono text-slate-200">
                {telemetry.cpu.powerWatts > 0
                  ? `${telemetry.cpu.powerWatts.toFixed(0)} W`
                  : t.common.sensorNotAvailable}
              </span>
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
              <span className="text-xs font-bold uppercase text-slate-300">
                {t.dashboard.gpuCardTitle}
              </span>
            </div>
            <span className="text-xs font-mono font-bold text-rose-300">
              {hasGpuLoad ? `${telemetry.gpu.utilization}%` : t.common.sensorNotAvailable}
            </span>
          </div>

          <div className="text-sm font-bold text-white truncate" title={telemetry.gpu.name}>
            {telemetry.gpu.name || t.common.sensorNotAvailable}
          </div>

          <div className="grid grid-cols-2 gap-2 pt-1 border-t border-slate-800/60 text-xs">
            <div>
              <span className="text-slate-500 block text-[10px]">{t.dashboard.coreHotspot}</span>
              <span className="font-mono font-semibold text-slate-200">
                {hasGpuCoreTemperature ? `${telemetry.gpu.temperature.toFixed(0)}°C` : '--'} /{' '}
                <span className="text-amber-400">
                  {hasGpuHotspotTemperature
                    ? `${telemetry.gpu.hotspot.toFixed(0)}°C`
                    : t.common.sensorNotAvailable}
                </span>
              </span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">{t.dashboard.hotspotDelta}</span>
              <span className="font-mono font-semibold text-emerald-400">
                {hotspotDelta ? `+${hotspotDelta}°C` : t.common.sensorNotAvailable}
              </span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">{t.dashboard.gpuClock}</span>
              <span className="font-mono text-slate-200">
                {telemetry.gpu.clockMHz > 0 ? `${telemetry.gpu.clockMHz} MHz` : t.common.sensorNotAvailable}
              </span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">{t.dashboard.vramAllocated}</span>
              <span className="font-mono text-slate-200">
                {telemetry.gpu.vramTotalMB > 0
                  ? `${(telemetry.gpu.vramUsedMB / 1024).toFixed(1)} / ${(telemetry.gpu.vramTotalMB / 1024).toFixed(0)} GB`
                  : t.common.sensorNotAvailable}
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
              <span className="text-xs font-bold uppercase text-slate-300">
                {t.dashboard.ramCardTitle}
              </span>
            </div>
            <span className="text-xs font-mono font-bold text-cyan-300">
              {telemetry.ram.totalGB > 0 ? `${telemetry.ram.percentage}%` : t.common.sensorNotAvailable}
            </span>
          </div>

          <div className="text-sm font-bold text-white truncate">
            {telemetry.ram.totalGB > 0
              ? `${telemetry.ram.totalGB} GB ${telemetry.ram.type}`
              : t.common.sensorNotAvailable}
          </div>

          <div className="grid grid-cols-2 gap-2 pt-1 border-t border-slate-800/60 text-xs">
            <div>
              <span className="text-slate-500 block text-[10px]">{t.dashboard.used}</span>
              <span className="font-mono font-semibold text-slate-200">
                {telemetry.ram.totalGB > 0
                  ? `${telemetry.ram.usedGB.toFixed(1)} GB`
                  : t.common.sensorNotAvailable}
              </span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">{t.dashboard.available}</span>
              <span className="font-mono font-semibold text-slate-200">
                {telemetry.ram.totalGB > 0
                  ? `${telemetry.ram.availableGB.toFixed(1)} GB`
                  : t.common.sensorNotAvailable}
              </span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">{t.dashboard.spdFrequency}</span>
              <span className="font-mono text-slate-200">
                {telemetry.ram.speedMT > 0 ? `${telemetry.ram.speedMT} MT/s` : t.common.sensorNotAvailable}
              </span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">{t.dashboard.channels}</span>
              <span className="font-mono text-slate-200">
                {telemetry.ram.channels || t.common.sensorNotAvailable}
              </span>
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
              <span className="text-xs font-bold uppercase text-slate-300">
                {t.dashboard.storageCardTitle}
              </span>
            </div>
            <span className="text-xs font-mono font-bold text-emerald-300">
              {primaryDisk?.smartStatus && primaryDisk.smartStatus !== 'N/D' ? primaryDisk.smartStatus : t.common.sensorNotAvailable}
            </span>
          </div>

          <div className="text-sm font-bold text-white truncate" title={primaryDisk?.model}>
            {primaryDisk?.model || t.common.sensorNotAvailable}
          </div>

          <div className="grid grid-cols-2 gap-2 pt-1 border-t border-slate-800/60 text-xs">
            <div>
              <span className="text-slate-500 block text-[10px]">{t.dashboard.freeSpace}</span>
              <span className="font-mono font-semibold text-slate-200">
                {primaryDisk && primaryDisk.capacityGB > 0 ? `${primaryDisk.freeSpaceGB.toFixed(1)} GB` : t.common.sensorNotAvailable}
              </span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">{t.dashboard.temp}</span>
              <span className="font-mono font-semibold text-slate-200">
                {diskTemperature > 0 ? `${diskTemperature}°C` : t.common.sensorNotAvailable}
              </span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">{t.dashboard.nandHealth}</span>
              <span className="font-mono text-emerald-400">
                {primaryDisk && primaryDisk.smartStatus !== 'N/D' && primaryDisk.healthPercent > 0
                  ? `${primaryDisk.healthPercent}%`
                  : t.common.sensorNotAvailable}
              </span>
            </div>
            <div>
              <span className="text-slate-500 block text-[10px]">{t.dashboard.interface}</span>
              <span className="font-mono text-slate-200">
                {primaryDisk?.interface || t.common.sensorNotAvailable}
              </span>
            </div>
          </div>

          <div className="w-full bg-slate-800 h-1.5 rounded-full overflow-hidden">
            <div
              className="bg-gradient-to-r from-emerald-500 to-teal-500 h-full transition-all duration-500"
              style={{ width: `${diskUsagePercent}%` }}
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
              <h2 className="text-sm font-bold text-white">
                {t.dashboard.realtimeTelemetry}
              </h2>
            </div>
            <div className="flex items-center gap-4 text-xs font-mono">
              <span className="flex items-center gap-1.5 text-purple-400">
                <span className="w-2.5 h-2.5 rounded-full bg-purple-500" /> {t.dashboard.cpuLoad}
              </span>
              <span className="flex items-center gap-1.5 text-rose-400">
                <span className="w-2.5 h-2.5 rounded-full bg-rose-500" /> {t.dashboard.gpuLoad}
              </span>
              <span className="flex items-center gap-1.5 text-cyan-400">
                <span className="w-2.5 h-2.5 rounded-full bg-cyan-500" /> {t.dashboard.cpuTemp}
              </span>
            </div>
          </div>

          {/* SVG Canvas Chart */}
          <div className="h-48 w-full bg-[#0a0c13] rounded-lg border border-slate-800/60 p-2 relative overflow-hidden flex items-center justify-center">
            {history.length > 1 ? (
              <svg className="w-full h-full overflow-visible" viewBox="0 0 500 150" preserveAspectRatio="none">
                <line x1="0" y1="37" x2="500" y2="37" stroke="#1f2438" strokeDasharray="3 3" />
                <line x1="0" y1="75" x2="500" y2="75" stroke="#1f2438" strokeDasharray="3 3" />
                <line x1="0" y1="112" x2="500" y2="112" stroke="#1f2438" strokeDasharray="3 3" />

                <polyline
                  fill="none"
                  stroke="#a855f7"
                  strokeWidth="2"
                  points={history
                    .map((pt, i) => pt.cpuLoad === null
                      ? null
                      : `${(i / (history.length - 1)) * 500},${150 - (pt.cpuLoad / 100) * 140}`)
                    .filter((point): point is string => point !== null)
                    .join(' ')}
                />

                <polyline
                  fill="none"
                  stroke="#f43f5e"
                  strokeWidth="2"
                  points={history
                    .map((pt, i) => pt.gpuLoad === null
                      ? null
                      : `${(i / (history.length - 1)) * 500},${150 - (pt.gpuLoad / 100) * 140}`)
                    .filter((point): point is string => point !== null)
                    .join(' ')}
                />

                <polyline
                  fill="none"
                  stroke="#06b6d4"
                  strokeWidth="1.5"
                  strokeDasharray="4 2"
                  points={history
                    .map((pt, i) => pt.cpuTemp === null
                      ? null
                      : `${(i / (history.length - 1)) * 500},${150 - (pt.cpuTemp / 100) * 140}`)
                    .filter((point): point is string => point !== null)
                    .join(' ')}
                />
              </svg>
            ) : (
              <div className="text-xs text-slate-500 font-mono text-center px-4">
                {t.common.sensorNotAvailable} — Conecte o Agente Windows Local para telemetria de sensores em tempo real.
              </div>
            )}

            <div className="absolute bottom-1 right-2 text-[10px] text-slate-500 font-mono">
              {t.dashboard.pollingNotice}
            </div>
          </div>
        </div>

        {/* Quick Optimization Callout */}
        <div className="p-5 rounded-xl bg-gradient-to-br from-[#121626] to-[#0c0e17] border border-purple-900/40 flex flex-col justify-between space-y-4">
          <div className="space-y-2">
            <div className="flex items-center gap-2">
              <Zap className="w-5 h-5 text-purple-400" />
              <h2 className="text-sm font-bold text-white uppercase tracking-wider">
                {t.dashboard.quickOptimization}
              </h2>
            </div>
            <p className="text-xs text-slate-300 leading-relaxed">
              {t.dashboard.quickOptDesc}
            </p>
          </div>

          <div className="p-3 bg-[#0a0c13] rounded-lg border border-slate-800 space-y-1.5 text-xs">
            <div className="flex items-center justify-between text-slate-400">
              <span>{t.dashboard.proposedChanges}</span>
              <span className="font-semibold text-purple-300">7 Tweaks</span>
            </div>
            <div className="flex items-center justify-between text-slate-400">
              <span>{t.dashboard.riskLevel}</span>
              <span className="font-semibold text-emerald-400">{t.dashboard.zeroRisk}</span>
            </div>
            <div className="flex items-center justify-between text-slate-400">
              <span>{t.dashboard.restorePointLabel}</span>
              <span className="text-slate-300">{t.dashboard.restorePointNotice}</span>
            </div>
          </div>

          <button
            onClick={onOpenBoostModal}
            className="w-full py-3 px-4 rounded-lg bg-gradient-to-r from-purple-600 via-indigo-600 to-violet-600 hover:from-purple-500 hover:to-violet-500 text-white font-bold text-xs uppercase tracking-wider shadow-lg shadow-purple-600/40 transition-all cursor-pointer border border-purple-400/40 active:scale-[0.98] flex items-center justify-center gap-2"
          >
            <Zap className="w-4 h-4 fill-current" />
            <span>{t.dashboard.executeBoost}</span>
          </button>
        </div>
      </div>

      {/* Motherboard & Platform Telemetry Detail */}
      <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800/80">
        <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 pb-3 border-b border-slate-800/60">
          <div className="flex items-center gap-2">
            <CircuitBoard className="w-4 h-4 text-indigo-400" />
            <h3 className="text-xs font-bold text-white uppercase tracking-wider">
              {t.dashboard.motherboardSensors}
            </h3>
          </div>
          <div className="text-xs font-mono text-slate-400">
            {telemetry.motherboard.model} · BIOS {telemetry.motherboard.biosVersion}
          </div>
        </div>

        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3 pt-3 text-xs">
          <div>
            <span className="text-slate-500 block text-[10px]">{t.dashboard.vrmTemp}</span>
            <span className="font-mono font-semibold text-slate-200">
              {telemetry.motherboard.temperatures.vrm
                ? `${telemetry.motherboard.temperatures.vrm}°C`
                : t.common.sensorNotAvailable}
            </span>
          </div>
          <div>
            <span className="text-slate-500 block text-[10px]">{t.dashboard.chipsetTemp}</span>
            <span className="font-mono font-semibold text-slate-200">
              {telemetry.motherboard.temperatures.chipset
                ? `${telemetry.motherboard.temperatures.chipset}°C`
                : t.common.sensorNotAvailable}
            </span>
          </div>
          <div>
            <span className="text-slate-500 block text-[10px]">{t.dashboard.rail12v}</span>
            <span className="font-mono font-semibold text-emerald-400">
              {telemetry.motherboard.voltages.v12 > 0
                ? `${telemetry.motherboard.voltages.v12.toFixed(2)} V`
                : t.common.sensorNotAvailable}
            </span>
          </div>
          <div>
            <span className="text-slate-500 block text-[10px]">{t.dashboard.rail5v}</span>
            <span className="font-mono font-semibold text-slate-200">
              {telemetry.motherboard.voltages.v5 > 0
                ? `${telemetry.motherboard.voltages.v5.toFixed(2)} V`
                : t.common.sensorNotAvailable}
            </span>
          </div>
          <div>
            <span className="text-slate-500 block text-[10px]">{t.dashboard.cpuFan}</span>
            <span className="font-mono font-semibold text-slate-200">
              {telemetry.motherboard.fans.cpuFanRPM > 0
                ? `${telemetry.motherboard.fans.cpuFanRPM} RPM`
                : t.common.sensorNotAvailable}
            </span>
          </div>
          <div>
            <span className="text-slate-500 block text-[10px]">{t.dashboard.pumpAio}</span>
            <span className="font-mono font-semibold text-purple-300">
              {telemetry.motherboard.fans.pumpRPM > 0
                ? `${telemetry.motherboard.fans.pumpRPM} RPM`
                : t.common.sensorNotAvailable}
            </span>
          </div>
        </div>
      </div>
    </div>
  );
};
