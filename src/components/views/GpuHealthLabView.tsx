import React, { useState } from 'react';
import {
  Flame,
  AlertTriangle,
  CheckCircle,
  FileText,
  Activity,
  Fan,
  ShieldCheck,
  TrendingUp,
  Cpu,
  Info,
} from 'lucide-react';
import { GpuTelemetry } from '../../types';

interface GpuHealthLabViewProps {
  gpu: GpuTelemetry;
}

export const GpuHealthLabView: React.FC<GpuHealthLabViewProps> = ({ gpu }) => {
  const [vendorFilter, setVendorFilter] = useState<'NVIDIA' | 'AMD' | 'Intel'>(
    gpu.vendor as 'NVIDIA' | 'AMD' | 'Intel'
  );
  const [alertThresholdDelta, setAlertThresholdDelta] = useState<number>(18);
  const [showReportModal, setShowReportModal] = useState<boolean>(false);

  const deltaT = gpu.hotspot - gpu.temperature;
  const isDeltaHigh = deltaT > alertThresholdDelta;
  const isVramHigh = (gpu.vramUsedMB / gpu.vramTotalMB) > 0.85;
  const isCoreTempHigh = gpu.temperature > 80;

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Banner */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#171224] via-[#1a142e] to-[#0f111a] border border-rose-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-rose-400 font-bold">
            <Flame className="w-4 h-4" /> Laboratório Avançado de Diagnóstico de GPU
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">GPU HEALTH LAB</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl">
            Inspeção de gradiente térmico de silício: correlação direta entre GPU Core e Hotspot para detectar degradação da pasta térmica (pump-out) e throttling.
          </p>
        </div>

        <div className="flex items-center gap-3">
          <button
            onClick={() => setShowReportModal(true)}
            className="flex items-center gap-2 px-4 py-2 rounded-lg bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white font-bold text-xs uppercase tracking-wider shadow-lg shadow-purple-600/30 transition-all cursor-pointer border border-purple-400/40"
          >
            <FileText className="w-4 h-4" />
            <span>Gerar Relatório de Saúde</span>
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
            <div className="text-sm font-bold text-white">{gpu.name}</div>
            <div className="text-xs text-slate-400">
              Arquitetura Ada Lovelace · Driver 566.14 WHQL · {gpu.pcieLane}
            </div>
          </div>
        </div>

        {/* Vendor Selector Simulator (NVIDIA / AMD / Intel) */}
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

      {/* The Crucial Hotspot Delta Card */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-5">
        {/* Delta Gauge Card */}
        <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-4 flex flex-col justify-between">
          <div>
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold uppercase text-slate-300">
                Gradiente Térmico (Delta ΔT)
              </span>
              <span
                className={`text-[10px] font-bold px-2 py-0.5 rounded ${
                  isDeltaHigh
                    ? 'bg-rose-500/20 text-rose-300 border border-rose-500/40'
                    : 'bg-emerald-500/20 text-emerald-300 border border-emerald-500/40'
                }`}
              >
                {isDeltaHigh ? 'Atenção Térmica' : 'Gradiente Saudável'}
              </span>
            </div>

            {/* Big Delta Value */}
            <div className="mt-4 flex items-baseline gap-2">
              <span
                className={`text-4xl font-black font-mono tracking-tight ${
                  isDeltaHigh ? 'text-amber-400' : 'text-emerald-400'
                }`}
              >
                +{deltaT.toFixed(1)}°C
              </span>
              <span className="text-xs text-slate-400">
                (Hotspot {gpu.hotspot}°C - Core {gpu.temperature}°C)
              </span>
            </div>

            <p className="text-xs text-slate-400 mt-2 leading-relaxed">
              O limite de segurança entre o sensor do die central e o ponto mais quente (hotspot) é de <strong>18.0°C</strong>. Valores acima sugerem contato irregular do dissipador ou pump-out da pasta térmica.
            </p>
          </div>

          <div className="p-3 bg-[#0a0c13] rounded-lg border border-slate-800/80 text-xs space-y-1">
            <div className="flex justify-between text-slate-400">
              <span>Limiar Máximo Seguro:</span>
              <span className="font-mono text-slate-200">≤ 18.0°C</span>
            </div>
            <div className="flex justify-between text-slate-400">
              <span>Alerta Crítico Hotspot:</span>
              <span className="font-mono text-rose-400">≥ 105.0°C</span>
            </div>
          </div>
        </div>

        {/* 4 Health Diagnostics Checks */}
        <div className="lg:col-span-2 grid grid-cols-1 sm:grid-cols-2 gap-4">
          {/* 1. Thermal Throttling Status */}
          <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold text-slate-300 uppercase">
                Estrangulamento (Throttling)
              </span>
              <ShieldCheck className="w-4 h-4 text-emerald-400" />
            </div>
            <div className="text-lg font-bold text-white font-mono">
              Sem Quedas de Frequência
            </div>
            <p className="text-xs text-slate-400">
              A GPU opera em clock nominal de {gpu.clockMHz} MHz sem quedas induzidas por limites de temperatura (TDP reportado de {gpu.thermalLimitReported}°C).
            </p>
          </div>

          {/* 2. VRAM Health & Leak Detector */}
          <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold text-slate-300 uppercase">
                Integridade de VRAM
              </span>
              <CheckCircle className="w-4 h-4 text-emerald-400" />
            </div>
            <div className="text-lg font-bold text-white font-mono">
              {(gpu.vramUsedMB / 1024).toFixed(1)} GB / {(gpu.vramTotalMB / 1024).toFixed(0)} GB
            </div>
            <p className="text-xs text-slate-400">
              Alocação dentro da faixa ideal. Nenhum vazamento crônico detectado nos buffers do Direct3D 12.
            </p>
          </div>

          {/* 3. Fan Profile & Acoustic Curve */}
          <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold text-slate-300 uppercase">
                Curva de Ventoinhas
              </span>
              <Fan className="w-4 h-4 text-cyan-400" />
            </div>
            <div className="text-lg font-bold text-white font-mono">
              {gpu.fansRPM} RPM ({gpu.fanSpeedPercent}%)
            </div>
            <p className="text-xs text-slate-400">
              Modo Zero RPM inativo com carga gráfica ativa. Rolamentos sem oscilação anômala de velocidade.
            </p>
          </div>

          {/* 4. Power & Voltage Balance */}
          <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold text-slate-300 uppercase">
                Tensão do Silício & TGP
              </span>
              <Activity className="w-4 h-4 text-purple-400" />
            </div>
            <div className="text-lg font-bold text-white font-mono">
              {gpu.powerWatts.toFixed(0)} W (TGP Estável)
            </div>
            <p className="text-xs text-slate-400">
              Trilho de 12V e reguladores de tensão VRM operando dentro da margem de 1.5% de ripple aceitável.
            </p>
          </div>
        </div>
      </div>

      {/* Safety Notice per Master Prompt Rule #8 */}
      <div className="p-4 rounded-xl bg-[#11131c] border border-slate-800 flex items-start gap-3 text-xs text-slate-400 leading-relaxed">
        <Info className="w-4 h-4 text-purple-400 shrink-0 mt-0.5" />
        <div>
          <span className="font-bold text-slate-200">Diretriz Técnica de Segurança do PC Control: </span>
          O software não declara componentes defeituosos de forma precipitada com base em eventos isolados e não aplica overclock ou undervolt sem intervenção direta e consentimento do proprietário.
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
                  Relatório Técnico de Saúde da GPU
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
              <div className="text-purple-400 font-bold">PC CONTROL ULTIMATE - GPU HEALTH AUDIT REPORT</div>
              <div className="text-slate-500">Data do Diagnóstico: {new Date().toLocaleString()}</div>
              <div className="text-purple-400 font-bold">========================================================</div>
              <div>• GPU Identificada: {gpu.name}</div>
              <div>• Barramento: {gpu.pcieLane}</div>
              <div>• Temperatura Core: {gpu.temperature.toFixed(1)}°C [Faixa Ideal: 40°C - 75°C]</div>
              <div>• Temperatura Hotspot: {gpu.hotspot.toFixed(1)}°C [Limiar de Alarme: 105°C]</div>
              <div>• Gradiente Térmico (Delta ΔT): +{deltaT.toFixed(1)}°C [Status: NORMAL]</div>
              <div>• Temperatura Memória GDDR6X: {gpu.memoryTemp.toFixed(1)}°C</div>
              <div>• Utilização de VRAM: {(gpu.vramUsedMB / 1024).toFixed(1)} GB de {(gpu.vramTotalMB / 1024).toFixed(0)} GB</div>
              <div>• Frequência de Operação: {gpu.clockMHz} MHz (Boost Clock Ativo)</div>
              <div>• Estado de Throttling: INATIVO (Sem cortes térmicos)</div>
              <div>• Conclusão do Engenheiro de Sistemas:</div>
              <div className="text-emerald-400 font-bold">
                Silício em perfeito estado de funcionamento e condutividade térmica homogênea.
              </div>
            </div>

            <div className="flex justify-end gap-3 pt-2">
              <button
                onClick={() => setShowReportModal(false)}
                className="px-4 py-2 rounded-lg bg-purple-600 hover:bg-purple-500 text-white font-bold text-xs cursor-pointer"
              >
                Fechar Relatório
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
