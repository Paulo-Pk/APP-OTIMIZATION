import React, { useState } from 'react';
import {
  Sliders,
  ShieldCheck,
  RotateCcw,
  Check,
  Zap,
  Info,
} from 'lucide-react';
import { SystemTweak } from '../../types';

interface WindowsTweaksViewProps {
  tweaks: SystemTweak[];
  onToggleTweak: (id: string) => void;
  onRestoreAll: () => void;
}

export const WindowsTweaksView: React.FC<WindowsTweaksViewProps> = ({
  tweaks,
  onToggleTweak,
  onRestoreAll,
}) => {
  const [activeCategory, setActiveCategory] = useState<string>('all');

  const categories = [
    { id: 'all', label: 'Todos os Tweaks' },
    { id: 'power', label: 'Energia' },
    { id: 'startup', label: 'Inicialização' },
    { id: 'services', label: 'Serviços' },
    { id: 'system', label: 'Interface & Sistema' },
    { id: 'gaming', label: 'Jogos' },
    { id: 'background', label: 'Privacidade & Fundo' },
  ];

  const filtered = tweaks.filter((t) => {
    if (activeCategory === 'all') return true;
    return t.category === activeCategory;
  });

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Banner */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <Sliders className="w-4 h-4" /> Personalização Avançada de Políticas do Windows
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">WINDOWS TWEAKS</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl">
            Ajustes diretos de chaves de registro HKLM/HKCU com auditoria individual, valores recomendados e suporte a reversão instantânea.
          </p>
        </div>

        <button
          onClick={onRestoreAll}
          className="flex items-center gap-1.5 px-3 py-2 rounded-lg bg-[#141724] hover:bg-[#1b2033] border border-slate-700 text-slate-300 hover:text-white text-xs font-semibold transition-colors cursor-pointer"
        >
          <RotateCcw className="w-3.5 h-3.5 text-slate-400" />
          <span>Restaurar Valores Padrão</span>
        </button>
      </div>

      {/* Categories Filter Tabs */}
      <div className="flex flex-wrap items-center gap-2 p-3 rounded-xl bg-[#0f121b] border border-slate-800">
        {categories.map((c) => {
          const isActive = activeCategory === c.id;
          return (
            <button
              key={c.id}
              onClick={() => setActiveCategory(c.id)}
              className={`px-3 py-1.5 rounded-lg text-xs font-semibold transition-all cursor-pointer ${
                isActive
                  ? 'bg-purple-600 text-white shadow-md shadow-purple-600/30 border border-purple-400/40'
                  : 'bg-[#151926] text-slate-400 hover:text-slate-200'
              }`}
            >
              {c.label}
            </button>
          );
        })}
      </div>

      {/* Tweaks Table View */}
      <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-4">
        <div className="divide-y divide-slate-800/80">
          {filtered.map((t) => (
            <div
              key={t.id}
              className="py-4 flex flex-col md:flex-row items-start md:items-center justify-between gap-4 text-xs"
            >
              <div className="space-y-1 max-w-2xl">
                <div className="flex items-center gap-2">
                  <span className="font-bold text-white text-sm">{t.name}</span>
                  {t.isApplied && (
                    <span className="text-[10px] font-mono font-bold px-2 py-0.5 rounded bg-purple-500/20 text-purple-300 border border-purple-500/30">
                      Modificado
                    </span>
                  )}
                </div>
                <p className="text-slate-400 leading-relaxed">{t.description}</p>
                <div className="text-[11px] font-mono text-slate-500 pt-0.5">
                  Chave: {t.registryKey || t.commandScript || 'Registro do Sistema'}
                </div>
              </div>

              <div className="flex items-center gap-4 shrink-0 font-mono">
                <div className="text-right">
                  <span className="text-[10px] text-slate-500 block uppercase font-sans">
                    Valor no Sistema
                  </span>
                  <span className="text-slate-300 font-semibold truncate max-w-xs block">
                    {t.isApplied ? t.recommendedValue : t.currentValue}
                  </span>
                </div>

                <button
                  onClick={() => onToggleTweak(t.id)}
                  className={`px-3 py-1.5 rounded-lg text-xs font-bold transition-all cursor-pointer flex items-center gap-1.5 ${
                    t.isApplied
                      ? 'bg-[#181c2d] text-slate-300 hover:bg-[#20263c] border border-slate-700'
                      : 'bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white border border-purple-400/30 shadow-sm'
                  }`}
                >
                  {t.isApplied ? (
                    <>
                      <RotateCcw className="w-3.5 h-3.5" />
                      <span>Desfazer</span>
                    </>
                  ) : (
                    <>
                      <Check className="w-3.5 h-3.5" />
                      <span>Ativar</span>
                    </>
                  )}
                </button>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
};
