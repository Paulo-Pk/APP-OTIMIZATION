import React, { useState } from 'react';
import {
  Zap,
  RotateCcw,
  Shield,
  CheckCircle,
  AlertTriangle,
  Flame,
  Filter,
  Check,
} from 'lucide-react';
import {
  SystemTweak,
  OptimizationLevel,
  TweakCategory,
  RiskLevel,
  RestoreItem,
} from '../../types';

interface BoostCenterViewProps {
  tweaks: SystemTweak[];
  onToggleTweak: (id: string) => void;
  onApplyAllLevel: (level: OptimizationLevel) => void;
  onRestoreAll: () => void;
  onOpenBoostModal: () => void;
}

export const BoostCenterView: React.FC<BoostCenterViewProps> = ({
  tweaks,
  onToggleTweak,
  onApplyAllLevel,
  onRestoreAll,
  onOpenBoostModal,
}) => {
  const [selectedLevel, setSelectedLevel] = useState<OptimizationLevel>('balanced');
  const [selectedCategory, setSelectedCategory] = useState<string>('all');
  const [searchQuery, setSearchQuery] = useState('');

  const categoryLabels: Record<TweakCategory, string> = {
    performance: 'Desempenho do Windows',
    startup: 'Inicialização',
    background: 'Processos em Segundo Plano',
    power: 'Gerenciamento de Energia',
    services: 'Serviços do Windows',
    memory: 'Otimização de Memória',
    gaming: 'Jogos & Escalonamento',
    system: 'Configurações de Sistema',
  };

  const filteredTweaks = tweaks.filter((t) => {
    const matchesLevel =
      selectedLevel === 'advanced'
        ? true
        : selectedLevel === 'balanced'
        ? t.level === 'conservative' || t.level === 'balanced'
        : t.level === 'conservative';

    const matchesCategory =
      selectedCategory === 'all' || t.category === selectedCategory;

    const matchesSearch =
      searchQuery === '' ||
      t.name.toLowerCase().includes(searchQuery.toLowerCase()) ||
      t.description.toLowerCase().includes(searchQuery.toLowerCase());

    return matchesLevel && matchesCategory && matchesSearch;
  });

  const appliedCount = tweaks.filter((t) => t.isApplied).length;

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Header Banner */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <Zap className="w-4 h-4" /> Central de Otimização do Sistema
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">BOOST CENTER</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl">
            Ajustes certificados para redução de latência DPC, eliminação de micro-stuttering e priorização de threads em DirectX 11, 12 e Vulkan.
          </p>
        </div>

        <div className="flex items-center gap-3">
          <button
            onClick={onRestoreAll}
            className="flex items-center gap-1.5 px-3 py-2 rounded-lg bg-[#141724] hover:bg-[#1b2033] border border-slate-700 text-slate-300 hover:text-white text-xs font-semibold transition-colors cursor-pointer"
          >
            <RotateCcw className="w-3.5 h-3.5 text-slate-400" />
            <span>Restaurar Tudo</span>
          </button>

          <button
            onClick={onOpenBoostModal}
            className="flex items-center gap-2 px-4 py-2 rounded-lg bg-gradient-to-r from-purple-600 to-violet-600 hover:from-purple-500 hover:to-violet-500 text-white font-bold text-xs uppercase tracking-wider shadow-lg shadow-purple-600/30 transition-all cursor-pointer border border-purple-400/40"
          >
            <Zap className="w-4 h-4 fill-current" />
            <span>Aplicar Nível {selectedLevel.toUpperCase()}</span>
          </button>
        </div>
      </div>

      {/* Level Selector & Controls Bar */}
      <div className="flex flex-col lg:flex-row items-start lg:items-center justify-between gap-4 p-4 rounded-xl bg-[#0f121b] border border-slate-800">
        {/* 3 Optimization Levels */}
        <div className="flex items-center gap-2">
          <span className="text-xs text-slate-400 font-semibold mr-1">Perfil:</span>
          {(['conservative', 'balanced', 'advanced'] as OptimizationLevel[]).map(
            (lvl) => {
              const isActive = selectedLevel === lvl;
              const labels = {
                conservative: 'Conservador (Seguro)',
                balanced: 'Equilibrado (Recomendado)',
                advanced: 'Avançado (Gamer Extremo)',
              };
              return (
                <button
                  key={lvl}
                  onClick={() => setSelectedLevel(lvl)}
                  className={`px-3 py-1.5 rounded-lg text-xs font-semibold transition-all cursor-pointer ${
                    isActive
                      ? 'bg-purple-600 text-white shadow-md shadow-purple-600/30 border border-purple-400/40'
                      : 'bg-[#151926] text-slate-400 hover:text-slate-200 hover:bg-[#1a2030] border border-slate-800'
                  }`}
                >
                  {labels[lvl]}
                </button>
              );
            }
          )}
        </div>

        {/* Search & Category Filter */}
        <div className="flex items-center gap-3 w-full lg:w-auto">
          <input
            type="text"
            placeholder="Buscar otimização..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="w-full sm:w-48 bg-[#0a0c13] border border-slate-800 rounded-lg px-3 py-1.5 text-xs text-slate-200 placeholder-slate-500 focus:outline-none focus:border-purple-500"
          />

          <select
            value={selectedCategory}
            onChange={(e) => setSelectedCategory(e.target.value)}
            className="bg-[#0a0c13] border border-slate-800 rounded-lg px-3 py-1.5 text-xs text-slate-300 focus:outline-none focus:border-purple-500 cursor-pointer"
          >
            <option value="all">Todas as 8 Categorias</option>
            {Object.entries(categoryLabels).map(([key, label]) => (
              <option key={key} value={key}>
                {label}
              </option>
            ))}
          </select>
        </div>
      </div>

      {/* Tweaks Cards Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {filteredTweaks.map((tweak) => {
          const isRiskHigh = tweak.risk === 'Alto';
          const isRiskMed = tweak.risk === 'Médio';

          return (
            <div
              key={tweak.id}
              className={`p-4 rounded-xl transition-all border ${
                tweak.isApplied
                  ? 'bg-[#111522] border-purple-600/40 shadow-sm shadow-purple-900/10'
                  : 'bg-[#0e111a] border-slate-800/80 hover:border-slate-700'
              } flex flex-col justify-between space-y-3`}
            >
              <div className="space-y-2">
                {/* Header row: Category & Risk & Applied badge */}
                <div className="flex items-center justify-between gap-2">
                  <span className="text-[11px] font-bold text-purple-400/90 uppercase tracking-wide">
                    {categoryLabels[tweak.category]}
                  </span>
                  <div className="flex items-center gap-2">
                    <span
                      className={`text-[10px] font-bold px-2 py-0.5 rounded ${
                        isRiskHigh
                          ? 'bg-rose-500/20 text-rose-300 border border-rose-500/30'
                          : isRiskMed
                          ? 'bg-amber-500/20 text-amber-300 border border-amber-500/30'
                          : 'bg-emerald-500/20 text-emerald-300 border border-emerald-500/30'
                      }`}
                    >
                      Risco: {tweak.risk}
                    </span>

                    {tweak.isApplied && (
                      <span className="text-[10px] font-bold px-2 py-0.5 rounded bg-purple-500/20 text-purple-300 border border-purple-500/40 flex items-center gap-1">
                        <Check className="w-3 h-3" /> Ativado
                      </span>
                    )}
                  </div>
                </div>

                {/* Tweak Name */}
                <h3 className="text-sm font-bold text-white leading-snug">
                  {tweak.name}
                </h3>

                {/* Description */}
                <p className="text-xs text-slate-400 leading-relaxed">
                  {tweak.description}
                </p>

                {/* Expected Benefit */}
                <div className="p-2.5 rounded-lg bg-[#0a0c13] border border-slate-800/80 text-xs">
                  <span className="text-emerald-400 font-semibold block text-[10px] uppercase tracking-wider mb-0.5">
                    Benefício Esperado
                  </span>
                  <span className="text-slate-300 font-mono text-[11px]">
                    {tweak.expectedBenefit}
                  </span>
                </div>

                {/* Config Values comparison */}
                <div className="grid grid-cols-2 gap-2 text-[11px] font-mono pt-1">
                  <div className="p-2 rounded bg-[#090b11] border border-slate-900">
                    <span className="text-slate-500 block text-[9px] uppercase font-sans">
                      Config Atual
                    </span>
                    <span className="text-slate-400 truncate block" title={tweak.currentValue}>
                      {tweak.currentValue}
                    </span>
                  </div>
                  <div className="p-2 rounded bg-[#090b11] border border-purple-950/40">
                    <span className="text-purple-400 block text-[9px] uppercase font-sans">
                      Recomendado
                    </span>
                    <span className="text-purple-300 truncate block" title={tweak.recommendedValue}>
                      {tweak.recommendedValue}
                    </span>
                  </div>
                </div>
              </div>

              {/* Action Buttons: Aplicar / Restaurar */}
              <div className="pt-2 border-t border-slate-800/60 flex items-center justify-between gap-2">
                <span className="text-[10px] text-slate-500">
                  {tweak.reversible ? '100% Reversível' : 'Reversão Manual'}
                </span>

                <div className="flex items-center gap-2">
                  {tweak.isApplied ? (
                    <button
                      onClick={() => onToggleTweak(tweak.id)}
                      className="px-3 py-1.5 rounded-lg bg-[#161a29] hover:bg-[#1f243a] border border-slate-700 text-slate-300 text-xs font-semibold transition-colors cursor-pointer flex items-center gap-1.5"
                    >
                      <RotateCcw className="w-3.5 h-3.5 text-slate-400" />
                      <span>Restaurar</span>
                    </button>
                  ) : (
                    <button
                      onClick={() => onToggleTweak(tweak.id)}
                      className="px-3.5 py-1.5 rounded-lg bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white text-xs font-bold transition-all cursor-pointer border border-purple-400/40 shadow-sm flex items-center gap-1.5"
                    >
                      <Zap className="w-3.5 h-3.5 fill-current" />
                      <span>Aplicar</span>
                    </button>
                  )}
                </div>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
};
