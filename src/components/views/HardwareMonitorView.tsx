import React from 'react';
import {
  Cpu,
  Flame,
  CircuitBoard,
  HardDrive,
} from 'lucide-react';
import { HardwareTelemetry } from '../../types';
import { useI18n } from '../../i18n';

interface HardwareMonitorViewProps {
  telemetry: HardwareTelemetry;
}

export const HardwareMonitorView: React.FC<HardwareMonitorViewProps> = ({
  telemetry,
}) => {
  const { t } = useI18n();

  const hasCpuTemperature = telemetry.cpu.temperatureAvailable ?? telemetry.cpu.temperature > 0;
  const hasGpuCoreTemperature = telemetry.gpu.temperatureAvailable ?? telemetry.gpu.temperature > 0;
  const hasGpuHotspotTemperature = telemetry.gpu.hotspotAvailable ?? telemetry.gpu.hotspot > 0;
  const hasGpuTemps = hasGpuCoreTemperature && hasGpuHotspotTemperature;
  const hasCpuLoadReading = (telemetry.sensors || []).some((sensor) => sensor.hardwareType.toLowerCase() === 'cpu' && sensor.sensorType === 'Load' && /total/i.test(sensor.name) && sensor.isAvailable && sensor.value !== null && Number.isFinite(sensor.value));
  // The native bridge already scopes VRAM readings to the primary GPU adapter.
  // Do not fall back to the unfiltered sensor list here: it contains every GPU
  // and could combine dedicated memory readings from different adapters.
  const vramUsedMB = telemetry.gpu.vramUsedMB > 0 ? telemetry.gpu.vramUsedMB : null;
  const vramTotalMB = telemetry.gpu.vramTotalMB > 0 ? telemetry.gpu.vramTotalMB : null;
  const sensorTypeLabels: Record<string, string> = { Temperature: 'Temperatura', Fan: 'Ventoinha', Clock: 'Frequência', Load: 'Uso', Voltage: 'Tensão', Power: 'Consumo' };
  const hardwareTypeLabels: Record<string, string> = { Cpu: 'Processador', Gpu: 'Placa de vídeo', Memory: 'Memória', Motherboard: 'Placa-mãe', Storage: 'Armazenamento' };
  const sensorTypeLabel = (type: string) => sensorTypeLabels[type] || type;
  const hardwareTypeLabel = (type: string) => type.toLowerCase().startsWith('gpu') ? 'Placa de vídeo' : type.toLowerCase().startsWith('cpu') ? 'Processador' : hardwareTypeLabels[type] || type;

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Title Header */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <Cpu className="w-4 h-4" /> {t.hardwareMonitor.subtitle}
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">Monitoramento do PC</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl leading-relaxed">
            Temperaturas, uso e espaço dos componentes. Os números são lidos do agente Windows; quando um sensor não existe, mostramos “Não disponível” em vez de inventar valores.
          </p>
        </div>

        <div className="flex items-center gap-2 px-3 py-1.5 rounded-lg bg-[#0e111a] border border-slate-800 text-xs font-mono text-slate-300">
          <span
            className={`w-2 h-2 rounded-full ${
              (hasCpuLoadReading || telemetry.cpu.temperature > 0 || telemetry.cpu.frequencyGHz > 0) ? 'bg-emerald-400 animate-pulse' : 'bg-amber-400'
            }`}
          />
          <span>{t.hardwareMonitor.pollingLabel}</span>
        </div>
      </div>

      {/* CPU In-Depth Section with Per-Core Load */}
      <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-4">
        <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-2 pb-3 border-b border-slate-800/80">
          <div className="flex items-center gap-2">
            <Cpu className="w-5 h-5 text-purple-400" />
            <div>
              <h2 className="text-sm font-bold text-white uppercase tracking-wider">
                {telemetry.cpu.name || t.common.sensorNotAvailable}
              </h2>
              <span className="text-xs font-mono text-slate-500">
                Socket {telemetry.cpu.socket} ·{' '}
                {telemetry.cpu.cores > 0
                  ? `${telemetry.cpu.cores} Cores / ${telemetry.cpu.threads} Threads`
                  : t.common.sensorNotAvailable}
              </span>
            </div>
          </div>

          <div className="flex items-center gap-4 text-xs font-mono">
            <div>
              <span className="text-slate-500">{t.hardwareMonitor.vcoreVoltage} </span>
              <span className="text-slate-200 font-semibold">
                {telemetry.cpu.voltageV > 0
                  ? `${telemetry.cpu.voltageV.toFixed(3)} V`
                  : t.common.sensorNotAvailable}
              </span>
            </div>
            <div>
              <span className="text-slate-500">{t.hardwareMonitor.pptPower} </span>
              <span className="text-slate-200 font-semibold">
                {telemetry.cpu.powerWatts > 0
                  ? `${telemetry.cpu.powerWatts.toFixed(1)} W`
                  : t.common.sensorNotAvailable}
              </span>
            </div>
          </div>
        </div>

        {/* CPU summary: real values supplied by the Windows hardware agent */}
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
          <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800/80">
            <span className="text-[10px] text-slate-500 uppercase block">Temperatura da CPU</span>
            <span className="text-lg font-mono font-bold text-white">{hasCpuTemperature ? `${telemetry.cpu.temperature.toFixed(1)}°C` : t.common.sensorNotAvailable}</span>
          </div>
          <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800/80">
            <span className="text-[10px] text-slate-500 uppercase block">Frequência da CPU</span>
            <span className="text-lg font-mono font-bold text-purple-300">{telemetry.cpu.frequencyGHz > 0 ? `${Math.round(telemetry.cpu.frequencyGHz * 1000).toLocaleString('pt-BR')} MHz` : t.common.sensorNotAvailable}</span>
          </div>
          <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800/80">
            <span className="text-[10px] text-slate-500 uppercase block">Uso da CPU</span>
            <span className="text-lg font-mono font-bold text-cyan-300">{hasCpuLoadReading ? `${telemetry.cpu.load.toFixed(0)}%` : t.common.sensorNotAvailable}</span>
          </div>
        </div>

        {/* Per-Core Load Grid */}
        <div>
          <span className="text-xs font-semibold text-slate-400 uppercase tracking-wider block mb-2">
            {t.hardwareMonitor.perCoreLoad}
          </span>
          {telemetry.cpu.coreList.length > 0 ? (
            <div className="grid grid-cols-2 sm:grid-cols-4 lg:grid-cols-8 gap-2.5">
              {telemetry.cpu.coreList.map((core) => (
                <div
                  key={core.coreId}
                  className="p-2.5 rounded-lg bg-[#0a0c13] border border-slate-800/80 space-y-1.5"
                >
                  <div className="flex items-center justify-between text-[11px] font-mono">
                    <span className="text-slate-400 font-semibold">
                      {t.hardwareMonitor.coreLabel} #{core.coreId}
                    </span>
                    <span className="text-purple-400 font-bold">{core.load}%</span>
                  </div>
                  <div className="w-full bg-slate-800 h-1.5 rounded-full overflow-hidden">
                    <div
                      className="bg-purple-500 h-full transition-all duration-300"
                      style={{ width: `${core.load}%` }}
                    />
                  </div>
                  <div className="flex items-center justify-between text-[10px] font-mono text-slate-500">
                    <span>
                      {core.frequencyGHz !== null && core.frequencyGHz > 0 ? `${Math.round(core.frequencyGHz * 1000).toLocaleString('pt-BR')} MHz` : '--'}
                    </span>
                    <span>{core.temp !== null && core.temp > 0 ? `${core.temp.toFixed(0)}°C` : '--'}</span>
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <div className="p-4 rounded-lg bg-[#0a0c13] border border-slate-800/80 text-xs text-slate-500 font-mono">
              {t.common.sensorNotAvailable}
            </div>
          )}
        </div>
      </div>

      {/* GPU In-Depth Section */}
      <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-4">
        <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-2 pb-3 border-b border-slate-800/80">
          <div className="flex items-center gap-2">
            <Flame className="w-5 h-5 text-rose-400" />
            <div>
              <h2 className="text-sm font-bold text-white uppercase tracking-wider">
                {telemetry.gpu.name || t.common.sensorNotAvailable}
              </h2>
              <span className="text-xs font-mono text-slate-500">
                {t.hardwareMonitor.vendor} {telemetry.gpu.vendor} · {t.hardwareMonitor.interface}{' '}
                {telemetry.gpu.pcieLane}
              </span>
            </div>
          </div>

          <div className="flex items-center gap-3 text-xs font-mono">
            <span className="px-2.5 py-1 rounded bg-slate-800 text-slate-300">
              {t.hardwareMonitor.gpuFans}{' '}
              {telemetry.gpu.fansRPM > 0
                ? `${telemetry.gpu.fansRPM} RPM (${telemetry.gpu.fanSpeedPercent}%)`
                : t.common.sensorNotAvailable}
            </span>
          </div>
        </div>

        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3">
          <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800/80">
            <span className="text-[10px] text-slate-500 uppercase block">
              Temperatura da GPU
            </span>
            <span className="text-base font-mono font-bold text-white">
              {hasGpuCoreTemperature
                ? `${telemetry.gpu.temperature.toFixed(1)}°C`
                : t.common.sensorNotAvailable}
            </span>
            <span className="text-[10px] text-emerald-400 block mt-0.5">
              {hasGpuCoreTemperature ? t.common.good : '--'}
            </span>
          </div>

          <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800/80">
            <span className="text-[10px] text-slate-500 uppercase block">
              Hotspot
            </span>
            <span className="text-base font-mono font-bold text-amber-400">
              {hasGpuHotspotTemperature
                ? `${telemetry.gpu.hotspot.toFixed(1)}°C`
                : t.common.sensorNotAvailable}
            </span>
            <span className="text-[10px] text-slate-400 block mt-0.5">
              Delta:{' '}
              {hasGpuTemps
                ? `+${(telemetry.gpu.hotspot - telemetry.gpu.temperature).toFixed(1)}°C`
                : t.common.sensorNotAvailable}
            </span>
          </div>

          <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800/80">
            <span className="text-[10px] text-slate-500 uppercase block">
              {t.hardwareMonitor.gpuVramTemp}
            </span>
            <span className="text-base font-mono font-bold text-slate-200">
              {telemetry.gpu.memoryTemp > 0
                ? `${telemetry.gpu.memoryTemp.toFixed(1)}°C`
                : t.common.sensorNotAvailable}
            </span>
            <span className="text-[10px] text-slate-400 block mt-0.5">VRAM</span>
          </div>

          <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800/80">
            <span className="text-[10px] text-slate-500 uppercase block">
              {t.hardwareMonitor.gpuClock}
            </span>
            <span className="text-base font-mono font-bold text-purple-300">
              {telemetry.gpu.clockMHz > 0
                ? `${telemetry.gpu.clockMHz} MHz`
                : t.common.sensorNotAvailable}
            </span>
            <span className="text-[10px] text-slate-400 block mt-0.5">
              {telemetry.gpu.clockMHz > 0 ? t.hardwareMonitor.boostActive : '--'}
            </span>
          </div>

          <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800/80">
            <span className="text-[10px] text-slate-500 uppercase block">
              {t.hardwareMonitor.gpuPower}
            </span>
            <span className="text-base font-mono font-bold text-slate-200">
              {telemetry.gpu.powerWatts > 0
                ? `${telemetry.gpu.powerWatts.toFixed(0)} W`
                : t.common.sensorNotAvailable}
            </span>
            <span className="text-[10px] text-slate-400 block mt-0.5">
              {telemetry.gpu.powerWatts > 0 ? t.hardwareMonitor.tgpNormal : '--'}
            </span>
          </div>

          <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800/80">
            <span className="text-[10px] text-slate-500 uppercase block">
              {t.hardwareMonitor.vramAllocation}
            </span>
            <span className="text-base font-mono font-bold text-cyan-300">
              {vramTotalMB !== null && vramTotalMB > 0 && vramUsedMB !== null
                ? `${(vramUsedMB / 1024).toFixed(1)} GB`
                : t.common.sensorNotAvailable}
            </span>
            <span className="text-[10px] text-slate-400 block mt-0.5">
              {vramTotalMB !== null && vramTotalMB > 0
                ? `${t.hardwareMonitor.of} ${(vramTotalMB / 1024).toFixed(0)} GB`
                : '--'}
            </span>
          </div>
        </div>
      </div>

      {/* Memory & Storage Health Detail */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-3">
          <div className="flex items-center justify-between pb-2 border-b border-slate-800/80">
            <div className="flex items-center gap-2">
              <CircuitBoard className="w-4 h-4 text-cyan-400" />
              <h3 className="text-xs font-bold text-white uppercase tracking-wider">
                {t.hardwareMonitor.ramConfigTitle}
              </h3>
            </div>
            <span className="text-xs font-mono text-cyan-300">{telemetry.ram.channels}</span>
          </div>

          <div className="space-y-2 text-xs">
            <div className="flex justify-between text-slate-300">
              <span>{t.hardwareMonitor.ramCapacity}</span>
              <span className="font-mono font-bold text-white">
                {telemetry.ram.totalGB > 0
                  ? `${telemetry.ram.totalGB} GB`
                  : t.common.sensorNotAvailable}
              </span>
            </div>
            <div className="flex justify-between text-slate-300">
              <span>{t.hardwareMonitor.ramSpeed}</span>
              <span className="font-mono text-slate-200">
                {telemetry.ram.speedMT > 0
                  ? `${telemetry.ram.speedMT} MT/s (${telemetry.ram.type})`
                  : t.common.sensorNotAvailable}
              </span>
            </div>
            <div className="flex justify-between text-slate-300">
              <span>{t.hardwareMonitor.ramAvailable}</span>
              <span className="font-mono text-slate-200">
                {telemetry.ram.totalGB > 0
                  ? `${telemetry.ram.availableGB.toFixed(1)} GB`
                  : t.common.sensorNotAvailable}
              </span>
            </div>
          </div>
        </div>

        <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-3">
          <div className="flex items-center justify-between"><h3 className="text-xs font-bold text-white uppercase tracking-wider">Leituras dos componentes</h3><span className="text-[11px] text-slate-500">{(telemetry.sensors || []).filter((sensor) => sensor.isAvailable && sensor.value !== null).length} sensores disponíveis</span></div>
          {(telemetry.sensors || []).filter((sensor) => sensor.isAvailable && sensor.value !== null).length > 0 ? (
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-2">
              {(telemetry.sensors || []).filter((sensor) => sensor.isAvailable && sensor.value !== null).map((sensor) => (
                <div key={sensor.identifier} className="rounded-lg bg-[#0a0c13] border border-slate-800 px-3 py-2">
                  <div className="text-[10px] text-slate-500">{hardwareTypeLabel(sensor.hardwareType)} · {sensorTypeLabel(sensor.sensorType)}</div>
                  <div className="text-xs text-slate-300 truncate">{sensor.name}</div>
                  <div className="text-sm font-mono font-bold text-white">{Number(sensor.value).toFixed(1)} {sensor.unit}</div>
                </div>
              ))}
            </div>
          ) : <p className="text-xs text-slate-500">O agente não disponibilizou leituras individuais nesta sessão.</p>}
        </div>

        <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-3">
          <div className="flex items-center justify-between pb-2 border-b border-slate-800/80">
            <div className="flex items-center gap-2">
              <HardDrive className="w-4 h-4 text-emerald-400" />
              <h3 className="text-xs font-bold text-white uppercase tracking-wider">
                {t.hardwareMonitor.diskDiagnosticsTitle}
              </h3>
            </div>
            <span className="text-xs font-mono text-emerald-400">
              {telemetry.disks.length} {t.hardwareMonitor.drivesDetected}
            </span>
          </div>

          <div className="space-y-3 text-xs">
            {telemetry.disks.length > 0 ? (
              telemetry.disks.map((disk) => (
                <div
                  key={disk.id}
                  className="p-2.5 rounded-lg bg-[#0a0c13] border border-slate-800/60 space-y-1"
                >
                  <div className="flex justify-between items-center">
                    <span className="font-bold text-white truncate max-w-xs">{disk.model}</span>
                    <span className="text-emerald-400 font-mono text-[11px] font-bold">
                      {disk.smartStatus === 'N/D' ? 'Saúde não informada' : `${t.hardwareMonitor.health} ${disk.healthPercent}%`}
                    </span>
                  </div>
                  <div className="flex justify-between text-slate-400 text-[11px] font-mono">
                    <span>
                      {disk.capacityGB > 0 ? `${disk.freeSpaceGB.toFixed(1)} GB ${t.hardwareMonitor.freeOf} ${disk.capacityGB.toFixed(1)} GB` : t.common.sensorNotAvailable}
                    </span>
                    <span>
                      Temperatura: {disk.temperature > 0 ? `${disk.temperature}°C` : t.common.sensorNotAvailable} · Estado do disco: {disk.smartStatus === 'N/D' ? 'Não informado' : disk.smartStatus}
                    </span>
                  </div>
                </div>
              ))
            ) : (
              <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800/60 text-slate-500 font-mono">
                {t.common.sensorNotAvailable}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
