import React, { useState } from 'react';
import {
  Flame,
  CheckCircle,
  FileText,
  Activity,
  Fan,
  ShieldCheck,
  Info,
} from 'lucide-react';
import { GpuTelemetry } from '../../types';
import { useI18n } from '../../i18n';

interface GpuHealthLabViewProps {
  gpu: GpuTelemetry;
  sensors?: Array<{ identifier: string; name: string; hardwareType: string; sensorType: string; value: number | null; unit: string; isAvailable: boolean; status: string }>;
}

export const GpuHealthLabView: React.FC<GpuHealthLabViewProps> = ({ gpu, sensors = [] }) => {
  const { t } = useI18n();
  const [vendorFilter, setVendorFilter] = useState<'NVIDIA' | 'AMD' | 'Intel'>(
    (gpu.vendor === 'NVIDIA' || gpu.vendor === 'AMD' || gpu.vendor === 'Intel'
      ? gpu.vendor
      : 'NVIDIA') as 'NVIDIA' | 'AMD' | 'Intel'
  );
  const [showReportModal, setShowReportModal] = useState<boolean>(false);

  const hasGpuCoreTemperature = gpu.temperatureAvailable ?? gpu.temperature > 0;
  const hasGpuHotspotTemperature = gpu.hotspotAvailable ?? gpu.hotspot > 0;
  const hasGpuMemoryTemperature = gpu.memoryTempAvailable ?? gpu.memoryTemp > 0;
  const hasGpuSensors = hasGpuCoreTemperature && hasGpuHotspotTemperature;
  const deltaT = hasGpuSensors ? gpu.hotspot - gpu.temperature : 0;
  const isDeltaElevated = hasGpuSensors && deltaT > 22;

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Banner */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#171224] via-[#1a142e] to-[#0f111a] border border-rose-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-rose-400 font-bold">
            <Flame className="w-4 h-4" /> {t.gpuHealth.subtitle}
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">{t.gpuHealth.title}</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl leading-relaxed">
            {t.gpuHealth.description}
          </p>
        </div>

        <div className="flex items-center gap-3">
          <button
            onClick={() => setShowReportModal(true)}
            className="flex items-center gap-2 px-4 py-2 rounded-lg bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white font-bold text-xs uppercase tracking-wider shadow-lg shadow-purple-600/30 transition-all cursor-pointer border border-purple-400/40"
          >
            <FileText className="w-4 h-4" />
            <span>{t.gpuHealth.generateReport}</span>
          </button>
        </div>
      </div>

      {/* GPU Vendor Identification Header */}
      <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-lg bg-rose-500/10 border border-rose-500/30 flex items-center justify-center">
            <Flame className="w-5 h-5 text-rose-400" />
          </div>
          <div>
            <div className="text-sm font-bold text-white">
              {gpu.name || t.common.sensorNotAvailable}
            </div>
            <div className="text-xs text-slate-400">
              {t.gpuHealth.activeSiliconArch} · {gpu.pcieLane || t.common.sensorNotAvailable}
            </div>
          </div>
        </div>

        {/* Vendor Selector */}
        <div className="flex items-center gap-1.5 p-1 bg-[#0a0c13] rounded-lg border border-slate-800 text-xs">
          {(['NVIDIA', 'AMD', 'Intel'] as const).map((vnd) => (
            <button
              key={vnd}
              onClick={() => setVendorFilter(vnd)}
              className={`px-3 py-1 rounded font-semibold cursor-pointer transition-colors ${
                vendorFilter === vnd
                  ? 'bg-rose-600 text-white shadow-sm'
                  : 'text-slate-400 hover:text-white'
              }`}
            >
              {vnd === 'NVIDIA' ? 'NVIDIA GeForce' : vnd === 'AMD' ? 'AMD Radeon' : 'Intel Arc'}
            </button>
          ))}
        </div>
      </div>

      {/* Full sensor inventory for all GPU adapters */}
      <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-3">
        <div className="flex items-center justify-between gap-3">
          <h3 className="text-xs font-bold text-white uppercase tracking-wider">Sensores e componentes da GPU</h3>
          <span className="text-[11px] text-slate-400">{sensors.filter((s) => /^gpu/i.test(s.hardwareType)).length} leituras detectadas</span>
        </div>
        {sensors.filter((s) => /^gpu/i.test(s.hardwareType)).length > 0 ? (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-2">
            {sensors.filter((s) => /^gpu/i.test(s.hardwareType)).map((sensor) => (
              <div key={sensor.identifier} className="rounded-lg border border-slate-800 bg-[#0a0c13] px-3 py-2">
                <div className="text-[11px] text-slate-400">{sensor.name}</div>
                <div className="text-sm font-mono font-semibold text-slate-100">{sensor.isAvailable && sensor.value !== null ? `${Number(sensor.value).toFixed(1)} ${sensor.unit}` : 'Não disponível'}</div>
                <div className="text-[10px] text-slate-500">{sensor.sensorType} · {sensor.hardwareType}</div>
              </div>
            ))}
          </div>
        ) : <p className="text-xs text-slate-500">O agente não retornou leituras individuais da GPU nesta sessão.</p>}
      </div>
      {/* Hotspot Delta Card */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-5">
        <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-4 flex flex-col justify-between">
          <div>
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold uppercase text-slate-300">
                {t.gpuHealth.gradientTitle}
              </span>
              <span
                className={`text-[10px] font-bold px-2 py-0.5 rounded ${
                  !hasGpuSensors
                    ? 'bg-slate-800 text-slate-400 border border-slate-700'
                    : isDeltaElevated
                    ? 'bg-rose-500/20 text-rose-300 border border-rose-500/40'
                    : 'bg-emerald-500/20 text-emerald-300 border border-emerald-500/40'
                }`}
              >
                {!hasGpuSensors
                  ? t.common.sensorNotAvailable
                  : isDeltaElevated
                  ? t.gpuHealth.warningGradient
                  : t.gpuHealth.healthyGradient}
              </span>
            </div>

            <div className="mt-4 flex items-baseline gap-2">
              <span
                className={`text-3xl font-black font-mono tracking-tight ${
                  !hasGpuSensors
                    ? 'text-slate-400'
                    : isDeltaElevated
                    ? 'text-amber-400'
                    : 'text-emerald-400'
                }`}
              >
                {hasGpuSensors ? `+${deltaT.toFixed(1)}°C` : t.common.sensorNotAvailable}
              </span>
              {hasGpuSensors && (
                <span className="text-xs text-slate-400 font-mono">
                  (Hotspot {gpu.hotspot}°C - Temperatura da GPU {gpu.temperature}°C)
                </span>
              )}
            </div>

            <p className="text-xs text-slate-400 mt-2 leading-relaxed">
              {t.gpuHealth.gradientDesc}
            </p>
          </div>

          <div className="p-3 bg-[#0a0c13] rounded-lg border border-slate-800/80 text-xs space-y-1">
            <div className="flex justify-between text-slate-400">
              <span>{t.gpuHealth.maxSafeThreshold}</span>
              <span className="font-mono text-slate-200">≤ 18.0°C - 22.0°C</span>
            </div>
            <div className="flex justify-between text-slate-400">
              <span>{t.gpuHealth.criticalAlert}</span>
              <span className="font-mono text-rose-400">≥ 105.0°C</span>
            </div>
          </div>
        </div>

        {/* 4 Health Diagnostics Checks */}
        <div className="lg:col-span-2 grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold text-slate-300 uppercase">
                {t.gpuHealth.throttlingTitle}
              </span>
              <ShieldCheck className="w-4 h-4 text-emerald-400" />
            </div>
            <div className="text-lg font-bold text-white font-mono">
              {gpu.isThrottling ? 'Limitação detectada pela telemetria' : 'Nenhum alerta de limitação recebido'}
            </div>
            <p className="text-xs text-slate-400 leading-relaxed">
              {'Este estado só é informado quando o agente recebe um sinal de limitação da placa. A ausência de alerta não substitui um teste de estabilidade.'}
            </p>
          </div>

          <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold text-slate-300 uppercase">
                {t.gpuHealth.vramTitle}
              </span>
              <CheckCircle className="w-4 h-4 text-emerald-400" />
            </div>
            <div className="text-lg font-bold text-white font-mono">
              {gpu.vramTotalMB > 0
                ? `${(gpu.vramUsedMB / 1024).toFixed(1)} GB / ${(gpu.vramTotalMB / 1024).toFixed(0)} GB`
                : t.common.sensorNotAvailable}
            </div>
            <p className="text-xs text-slate-400 leading-relaxed">
              {t.gpuHealth.vramDesc}
            </p>
          </div>

          <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold text-slate-300 uppercase">
                {t.gpuHealth.fanCurveTitle}
              </span>
              <Fan className="w-4 h-4 text-cyan-400" />
            </div>
            <div className="text-lg font-bold text-white font-mono">
              {gpu.fansRPM > 0
                ? `${gpu.fansRPM} RPM (${gpu.fanSpeedPercent}%)`
                : t.common.sensorNotAvailable}
            </div>
            <p className="text-xs text-slate-400 leading-relaxed">
              {t.gpuHealth.fanCurveDesc}
            </p>
          </div>

          <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold text-slate-300 uppercase">
                {t.gpuHealth.siliconVoltageTitle}
              </span>
              <Activity className="w-4 h-4 text-purple-400" />
            </div>
            <div className="text-lg font-bold text-white font-mono">
              {gpu.powerWatts > 0
                ? `${gpu.powerWatts.toFixed(0)} W (${t.gpuHealth.tgpStable})`
                : t.common.sensorNotAvailable}
            </div>
            <p className="text-xs text-slate-400 leading-relaxed">
              {t.gpuHealth.siliconVoltageDesc}
            </p>
          </div>
        </div>
      </div>

      {/* Safety Notice */}
      <div className="p-4 rounded-xl bg-[#11131c] border border-slate-800 flex items-start gap-3 text-xs text-slate-400 leading-relaxed">
        <Info className="w-4 h-4 text-purple-400 shrink-0 mt-0.5" />
        <div>
          <span className="font-bold text-slate-200">{t.gpuHealth.engineeringDirective} </span>
          {t.gpuHealth.safetyNotice}
        </div>
      </div>

      {/* GPU Health Report Modal */}
      {showReportModal && (
        <div className="fixed inset-0 z-50 bg-black/80 backdrop-blur-xs flex items-center justify-center p-4">
          <div className="w-full max-w-2xl bg-[#0f121b] border border-purple-500/50 rounded-xl p-6 space-y-4 shadow-2xl animate-scale-in">
            <div className="flex items-center justify-between pb-3 border-b border-slate-800">
              <div className="flex items-center gap-2">
                <FileText className="w-5 h-5 text-purple-400" />
                <h3 className="text-sm font-bold text-white uppercase tracking-wider">
                  {t.gpuHealth.reportTitle}
                </h3>
              </div>
              <button
                onClick={() => setShowReportModal(false)}
                className="text-slate-400 hover:text-white font-bold text-lg cursor-pointer"
              >
                ×
              </button>
            </div>

            <div className="space-y-3 text-xs font-mono bg-[#0a0c13] p-4 rounded-lg border border-slate-800 text-slate-300 leading-relaxed">
              <div className="text-purple-400 font-bold">========================================================</div>
              <div className="text-purple-400 font-bold">PC CONTROL ULTIMATE - GPU AUDIT REPORT</div>
              <div className="text-slate-500">Timestamp: {new Date().toISOString()}</div>
              <div className="text-purple-400 font-bold">========================================================</div>
              <div>• GPU: {gpu.name} ({gpu.vendor})</div>
              <div>
                • Temperatura da GPU:{' '}
                {hasGpuCoreTemperature ? `${gpu.temperature.toFixed(1)}°C` : t.common.sensorNotAvailable}
              </div>
              <div>
                • Hotspot:{' '}
                {hasGpuHotspotTemperature ? `${gpu.hotspot.toFixed(1)}°C` : t.common.sensorNotAvailable}
              </div>
              <div>
                • Thermal Delta (ΔT):{' '}
                {hasGpuSensors ? `+${deltaT.toFixed(1)}°C` : t.common.sensorNotAvailable}
              </div>
              <div>
                • Memory Temp:{' '}
                {hasGpuMemoryTemperature ? `${gpu.memoryTemp.toFixed(1)}°C` : t.common.sensorNotAvailable}
              </div>
              <div>
                • VRAM Usage:{' '}
                {gpu.vramTotalMB > 0
                  ? `${(gpu.vramUsedMB / 1024).toFixed(1)} GB / ${(gpu.vramTotalMB / 1024).toFixed(0)} GB`
                  : t.common.sensorNotAvailable}
              </div>
              <div>
                • Frequency:{' '}
                {gpu.clockMHz > 0 ? `${gpu.clockMHz} MHz` : t.common.sensorNotAvailable}
              </div>
              <div>
                • Status:{' '}
                {hasGpuSensors ? t.gpuHealth.nominalSpecification : t.common.sensorNotAvailable}
              </div>
            </div>

            <div className="flex justify-end gap-3 pt-2">
              <button
                onClick={() => setShowReportModal(false)}
                className="px-4 py-2 rounded-lg bg-purple-600 hover:bg-purple-500 text-white font-bold text-xs cursor-pointer"
              >
                {t.gpuHealth.closeReport}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
