import React from 'react';
import {
  Layers,
  ExternalLink,
  CheckCircle2,
  AlertTriangle,
  ShieldCheck,
  RefreshCw,
} from 'lucide-react';
import { systemDrivers } from '../../data/softwareHub';

export const DriverCenterView: React.FC = () => {
  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Banner */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <Layers className="w-4 h-4" /> Gestão de Controladores e Firmware
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">DRIVER CENTER</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl">
            Identificação de versões de drivers instalados de GPU, Chipset e áudio com redirecionamento exclusivo para portais oficiais dos fabricantes.
          </p>
        </div>

        <div className="flex items-center gap-2 text-xs text-emerald-400 bg-[#0f1420] px-3 py-1.5 rounded-lg border border-emerald-500/30 font-semibold">
          <ShieldCheck className="w-4 h-4" />
          <span>Fontes Oficiais Verificadas</span>
        </div>
      </div>

      {/* Drivers List Table */}
      <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-4">
        <div className="flex items-center justify-between pb-2 border-b border-slate-800 text-xs">
          <span className="font-bold text-white uppercase tracking-wider">
            Dispositivos e Drivers do Sistema
          </span>
          <span className="text-slate-400 font-mono">
            {systemDrivers.length} Controladores Monitorados
          </span>
        </div>

        <div className="divide-y divide-slate-800/80">
          {systemDrivers.map((driver, idx) => {
            const isUpToDate = driver.status === 'Atualizado';

            return (
              <div
                key={idx}
                className="py-4 flex flex-col md:flex-row items-start md:items-center justify-between gap-4 text-xs"
              >
                <div className="space-y-1">
                  <div className="flex items-center gap-2">
                    <span className="font-bold text-white text-sm">{driver.device}</span>
                    <span
                      className={`text-[10px] font-mono font-bold px-2 py-0.5 rounded ${
                        isUpToDate
                          ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/30'
                          : 'bg-amber-500/10 text-amber-400 border border-amber-500/30'
                      }`}
                    >
                      {driver.status}
                    </span>
                  </div>
                  <div className="text-slate-400 text-[11px]">
                    Fabricante: {driver.vendor} · Versão Instalada: <span className="text-slate-200 font-mono font-semibold">{driver.installedVersion}</span> ({driver.releaseDate})
                  </div>
                </div>

                <div className="flex items-center gap-3 shrink-0">
                  <a
                    href={driver.officialUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="flex items-center gap-1.5 px-3.5 py-1.5 rounded-lg bg-[#141829] hover:bg-[#1b223d] border border-purple-500/40 text-purple-200 hover:text-white text-xs font-semibold transition-colors cursor-pointer"
                  >
                    <ExternalLink className="w-3.5 h-3.5 text-purple-400" />
                    <span>Portal Oficial do Fabricante</span>
                  </a>
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
};
