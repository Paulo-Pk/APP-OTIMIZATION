import React from 'react';
import { Zap, Download, ShieldCheck, AlertTriangle } from 'lucide-react';
import { TabId } from '../types';

interface HeaderProps {
  currentTab: TabId;
  onNavigate: (tab: TabId) => void;
  onOpenBoostModal: () => void;
  isSimulatedMode: boolean;
  onDownloadSolution: () => void;
  overallScore: number;
}

export const Header: React.FC<HeaderProps> = ({
  onNavigate,
  onOpenBoostModal,
  isSimulatedMode,
  onDownloadSolution,
  overallScore,
}) => {
  return (
    <header className="h-14 bg-[#0d0f17] border-b border-purple-900/30 px-4 flex items-center justify-between select-none shrink-0 z-20">
      {/* Brand & Identity */}
      <div className="flex items-center gap-3">
        <div className="w-8 h-8 rounded-lg bg-gradient-to-br from-purple-600 via-indigo-600 to-violet-800 flex items-center justify-center shadow-lg shadow-purple-600/30 border border-purple-400/30">
          <Zap className="w-4 h-4 text-white" />
        </div>
        <div>
          <div className="flex items-center gap-2">
            <span className="font-black text-sm tracking-wider text-white">PC CONTROL</span>
            <span className="font-bold text-xs tracking-widest text-purple-400 uppercase">ULTIMATE</span>
            <span className="text-[10px] text-slate-400 font-mono">v2.4 .NET 10 LTS</span>
          </div>
        </div>
      </div>

      {/* Center Notice: Environment State (Rule #23 Compliance) */}
      <div className="hidden lg:flex items-center gap-2 bg-[#131622] px-3 py-1 rounded-md border border-slate-800 text-xs">
        {isSimulatedMode ? (
          <>
            <AlertTriangle className="w-3.5 h-3.5 text-amber-400 shrink-0" />
            <span className="text-slate-300">
              Modo de Demonstração / Sandbox Web Ativo
            </span>
            <span className="text-slate-500 font-mono text-[11px]">·</span>
            <button
              onClick={() => onNavigate('dotnet-solution')}
              className="text-purple-400 hover:text-purple-300 underline font-medium cursor-pointer"
            >
              Exportar para Windows x64
            </button>
          </>
        ) : (
          <>
            <ShieldCheck className="w-3.5 h-3.5 text-emerald-400 shrink-0" />
            <span className="text-emerald-300">Conectado ao Serviço Privilegiado Windows</span>
          </>
        )}
      </div>

      {/* Right Controls & Quick Actions */}
      <div className="flex items-center gap-3">
        {/* Score indicator */}
        <div
          onClick={() => onNavigate('diagnostics')}
          className="cursor-pointer flex items-center gap-2 px-2.5 py-1 rounded-md bg-[#161a27] hover:bg-[#1a2030] border border-purple-900/40 text-xs transition-colors"
          title="Ver Diagnóstico do PC"
        >
          <span className="text-slate-400">Saúde:</span>
          <span className={`font-bold font-mono ${overallScore >= 90 ? 'text-emerald-400' : overallScore >= 75 ? 'text-amber-400' : 'text-rose-400'}`}>
            {overallScore}/100
          </span>
        </div>

        {/* Quick Solution Download */}
        <button
          onClick={onDownloadSolution}
          className="flex items-center gap-1.5 px-3 py-1.5 rounded-md bg-[#181c2b] hover:bg-[#20263a] border border-purple-500/40 text-purple-300 hover:text-purple-200 text-xs font-semibold transition-colors cursor-pointer shadow-sm"
          title="Baixar Solução Completa C# .NET 10 (.zip)"
        >
          <Download className="w-3.5 h-3.5 text-purple-400" />
          <span className="hidden sm:inline">Baixar Solução C#</span>
        </button>

        {/* BOOST NOW Primary Action */}
        <button
          onClick={onOpenBoostModal}
          className="flex items-center gap-1.5 px-4 py-1.5 rounded-md bg-gradient-to-r from-purple-600 to-violet-600 hover:from-purple-500 hover:to-violet-500 text-white text-xs font-bold uppercase tracking-wider shadow-lg shadow-purple-600/30 transition-all cursor-pointer border border-purple-400/40 active:scale-95"
        >
          <Zap className="w-3.5 h-3.5 fill-current" />
          <span>BOOST NOW</span>
        </button>

        {/* Window controls styling mockup */}
        <div className="flex items-center gap-2 pl-2 border-l border-slate-800 text-slate-500">
          <div className="w-3 h-3 rounded-full bg-slate-700/60" title="Minimizar"></div>
          <div className="w-3 h-3 rounded-full bg-slate-700/60" title="Maximizar"></div>
          <div className="w-3 h-3 rounded-full bg-rose-500/80" title="Fechar"></div>
        </div>
      </div>
    </header>
  );
};
