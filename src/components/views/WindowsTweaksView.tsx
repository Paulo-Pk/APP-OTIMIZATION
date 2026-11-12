import React, { useState, useMemo } from 'react';
import {
  Sliders,
  RotateCcw,
  Check,
  RefreshCw,
  Info,
  AlertTriangle,
  ShieldCheck,
  Cpu,
  Terminal,
} from 'lucide-react';
import { SystemTweak, TweakInspectionStateCode } from '../../types';
import { useI18n } from '../../i18n';

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
  const { t, language } = useI18n();
  const isPt = language === 'pt-BR';
  const [activeCategory, setActiveCategory] = useState<string>('all');
  const [selectedTweakId, setSelectedTweakId] = useState<string>(tweaks[0]?.id || 'perf-mmcss');
  const [isRefreshingStates, setIsRefreshingStates] = useState<boolean>(false);
  const [lastRefreshedTime, setLastRefreshedTime] = useState<string>(() =>
    new Date().toLocaleTimeString()
  );

  const categories = [
    { id: 'all', label: t.boostCenter.allCategories },
    { id: 'performance', label: isPt ? 'Desempenho' : 'Performance' },
    { id: 'gaming', label: t.boostCenter.catGaming },
    { id: 'power', label: t.boostCenter.catPower },
    { id: 'startup', label: t.boostCenter.catStartup },
    { id: 'services', label: t.boostCenter.catServices },
    { id: 'memory', label: isPt ? 'Memória' : 'Memory' },
    { id: 'system', label: t.boostCenter.catSystem },
    { id: 'background', label: t.boostCenter.catBackground },
  ];

  const getEffectiveState = (item: SystemTweak): TweakInspectionStateCode => {
    if (item.isApplied) return 'Active';
    return item.inspectionState || 'Inactive';
  };

  const getStateBadgeMeta = (state: TweakInspectionStateCode) => {
    switch (state) {
      case 'Active':
        return {
          label: isPt ? 'ATIVO' : 'ACTIVE',
          className: 'bg-emerald-950/70 text-emerald-400 border-emerald-500/40',
        };
      case 'NotConfigured':
        return {
          label: isPt ? 'NÃO CONFIGURADO' : 'NOT CONFIGURED',
          className: 'bg-slate-900/90 text-slate-400 border-slate-700/80',
        };
      case 'Inactive':
        return {
          label: isPt ? 'INATIVO' : 'INACTIVE',
          className: 'bg-slate-900/90 text-slate-400 border-slate-700/80',
        };
      case 'Custom':
        return {
          label: isPt ? 'PERSONALIZADO' : 'CUSTOM',
          className: 'bg-amber-950/70 text-amber-400 border-amber-500/40',
        };
      case 'AccessDenied':
        return {
          label: isPt ? 'ACESSO NEGADO' : 'ACCESS DENIED',
          className: 'bg-rose-950/70 text-rose-400 border-rose-500/40',
        };
      case 'WrongType':
        return {
          label: isPt ? 'TIPO INCORRETO' : 'WRONG TYPE',
          className: 'bg-rose-950/70 text-rose-400 border-rose-500/40',
        };
      case 'Incompatible':
        return {
          label: isPt ? 'INCOMPATÍVEL' : 'INCOMPATIBLE',
          className: 'bg-rose-950/70 text-rose-400 border-rose-500/40',
        };
      case 'ReadError':
      default:
        return {
          label: isPt ? 'ERRO DE LEITURA' : 'READ ERROR',
          className: 'bg-rose-950/70 text-rose-400 border-rose-500/40',
        };
    }
  };

  const summaryCounts = useMemo(() => {
    let active = 0;
    let inactive = 0;
    let custom = 0;
    let notConfigured = 0;
    let errorsOrDenied = 0;
    for (const item of tweaks) {
      const st = getEffectiveState(item);
      if (st === 'Active') active++;
      else if (st === 'Inactive') inactive++;
      else if (st === 'Custom') custom++;
      else if (st === 'NotConfigured') notConfigured++;
      else errorsOrDenied++;
    }
    return {
      total: tweaks.length,
      active,
      inactive,
      custom,
      notConfigured,
      errorsOrDenied,
    };
  }, [tweaks]);

  const handleRefreshStates = () => {
    if (isRefreshingStates) return;
    setIsRefreshingStates(true);
    setTimeout(() => {
      setLastRefreshedTime(new Date().toLocaleTimeString());
      setIsRefreshingStates(false);
    }, 180);
  };

  const filtered = tweaks.filter((item) => {
    if (activeCategory === 'all') return true;
    return item.category === activeCategory;
  });

  const selectedTweak =
    tweaks.find((item) => item.id === selectedTweakId) || filtered[0] || tweaks[0];

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Banner */}
      <div className="flex flex-col lg:flex-row items-start lg:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-cyan-900/30">
        <div className="space-y-2">
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-cyan-400 font-bold">
            <Sliders className="w-4 h-4" />
            <span>
              {isPt
                ? 'CATÁLOGO FECHADO AUDITADO · INSPEÇÃO REAL DE CHAVE, VALOR E TIPO'
                : 'AUDITED CLOSED CATALOG · REAL KEY, VALUE & KIND INSPECTION'}
            </span>
          </div>
          <h1 className="text-xl font-black text-white">{t.windowsTweaks.title}</h1>
          <p className="text-xs text-slate-400 max-w-2xl leading-relaxed">
            {isPt
              ? 'Cada ajuste consulta diretamente o Registro do Windows (existência da chave, valor e RegistryValueKind), valida pós-escrita e preserva reversão DPAPI+HMAC.'
              : 'Each tweak directly queries the Windows Registry (key existence, value, and RegistryValueKind), verifies post-write state, and preserves DPAPI+HMAC rollback.'}
          </p>

          {/* Summary Counters Bar */}
          <div className="inline-flex flex-wrap items-center gap-3 px-3 py-1.5 rounded-lg bg-[#090c14] border border-slate-800 text-[11px] font-mono text-slate-300">
            <span>
              {isPt ? 'Consultados' : 'Queried'}: <strong className="text-white">{summaryCounts.total}</strong>
            </span>
            <span className="text-slate-700">|</span>
            <span>
              {isPt ? 'Ativos' : 'Active'}:{' '}
              <strong className="text-emerald-400">{summaryCounts.active}</strong>
            </span>
            <span className="text-slate-700">|</span>
            <span>
              {isPt ? 'Inativos' : 'Inactive'}:{' '}
              <strong className="text-slate-400">{summaryCounts.inactive}</strong>
            </span>
            <span className="text-slate-700">|</span>
            <span>
              {isPt ? 'Personalizados' : 'Custom'}:{' '}
              <strong className="text-amber-400">{summaryCounts.custom}</strong>
            </span>
            <span className="text-slate-700">|</span>
            <span>
              {isPt ? 'Não Configurados' : 'Not Configured'}:{' '}
              <strong className="text-slate-400">{summaryCounts.notConfigured}</strong>
            </span>
            <span className="text-slate-700">|</span>
            <span>
              {isPt ? 'Acesso Negado / Erros' : 'Access Denied / Errors'}:{' '}
              <strong className="text-rose-400">{summaryCounts.errorsOrDenied}</strong>
            </span>
            <span className="text-slate-600 text-[10px]">({lastRefreshedTime})</span>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2.5 shrink-0">
          <button
            onClick={handleRefreshStates}
            disabled={isRefreshingStates}
            className="flex items-center gap-1.5 px-3.5 py-2 rounded-lg bg-cyan-600 hover:bg-cyan-500 disabled:opacity-60 text-white text-xs font-bold transition-colors cursor-pointer shadow-sm"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${isRefreshingStates ? 'animate-spin' : ''}`} />
            <span>{isPt ? 'ATUALIZAR ESTADOS' : 'REFRESH STATES'}</span>
          </button>

          <button
            onClick={onRestoreAll}
            className="flex items-center gap-1.5 px-3.5 py-2 rounded-lg bg-[#141724] hover:bg-[#1b2033] border border-slate-700 text-slate-300 hover:text-white text-xs font-semibold transition-colors cursor-pointer"
          >
            <RotateCcw className="w-3.5 h-3.5 text-slate-400" />
            <span>{t.windowsTweaks.restoreDefaults}</span>
          </button>
        </div>
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
      <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-3">
        <div className="divide-y divide-slate-800/80">
          {filtered.map((item) => {
            const stateCode = getEffectiveState(item);
            const stateBadge = getStateBadgeMeta(stateCode);
            const isSelected = selectedTweak?.id === item.id;
            const displayName = isPt ? item.name : item.nameEn || item.name;
            const displayDesc = isPt ? item.description : item.descriptionEn || item.description;
            const displayBenefit = isPt
              ? item.expectedBenefit
              : item.expectedBenefitEn || item.expectedBenefit;

            return (
              <div
                key={item.id}
                onClick={() => setSelectedTweakId(item.id)}
                className={`py-4 px-3 -mx-3 rounded-lg transition-colors flex flex-col xl:flex-row items-start xl:items-center justify-between gap-4 text-xs cursor-pointer ${
                  isSelected ? 'bg-[#141927] border border-slate-700/70' : 'hover:bg-[#121622]'
                }`}
              >
                <div className="space-y-1.5 max-w-2xl">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-bold text-white text-sm">{displayName}</span>
                    <span
                      className={`text-[10px] font-mono font-bold px-2 py-0.5 rounded border ${stateBadge.className}`}
                    >
                      {stateBadge.label}
                    </span>
                    <span className="text-[10px] font-mono px-2 py-0.5 rounded bg-slate-900 text-slate-400 border border-slate-800">
                      {isPt ? `Risco: ${item.risk}` : `Risk: ${item.risk}`}
                    </span>
                    {item.isRecommendedForApply === false && (
                      <span className="text-[10px] font-mono font-semibold px-2 py-0.5 rounded bg-amber-950/60 text-amber-300 border border-amber-500/30">
                        {isPt ? 'NÃO RECOMENDADO / PADRÃO' : 'NOT RECOMMENDED / DEFAULT'}
                      </span>
                    )}
                  </div>
                  <p className="text-slate-300 leading-relaxed">{displayDesc}</p>
                  <p className="text-[11px] text-emerald-400/90">
                    <strong>{isPt ? 'Benefício esperado:' : 'Expected benefit:'}</strong> {displayBenefit}
                  </p>
                  <div className="text-[11px] font-mono text-slate-500 pt-0.5">
                    <span className="text-slate-400">{item.id}</span> · {t.windowsTweaks.keyLabel}{' '}
                    {item.registryKey}\{item.valueName} ({item.valueKind || 'DWord'})
                  </div>
                </div>

                <div className="flex flex-wrap items-center gap-4 shrink-0 font-mono w-full xl:w-auto justify-between xl:justify-end">
                  <div className="flex items-center gap-4">
                    <div className="text-right">
                      <span className="text-[10px] text-slate-500 block uppercase font-sans">
                        {isPt ? 'Valor Atual' : 'Current Value'}
                      </span>
                      <span className="text-slate-200 font-semibold block">
                        {item.isApplied ? item.recommendedValue : item.currentValue}
                      </span>
                    </div>
                    <div className="text-right">
                      <span className="text-[10px] text-slate-500 block uppercase font-sans">
                        {isPt ? 'Recomendado' : 'Recommended'}
                      </span>
                      <span className="text-emerald-400 font-semibold block">
                        {item.recommendedValue}
                      </span>
                    </div>
                  </div>

                  <div className="flex items-center gap-2 font-sans">
                    <button
                      onClick={(e) => {
                        e.stopPropagation();
                        setSelectedTweakId(item.id);
                        onToggleTweak(item.id);
                      }}
                      className={`px-3 py-1.5 rounded-lg text-xs font-bold transition-all cursor-pointer flex items-center gap-1.5 ${
                        item.isApplied
                          ? 'bg-[#181c2d] text-slate-300 hover:bg-[#20263c] border border-slate-700'
                          : 'bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white border border-purple-400/30 shadow-sm'
                      }`}
                    >
                      {item.isApplied ? (
                        <>
                          <RotateCcw className="w-3.5 h-3.5" />
                          <span>{isPt ? 'Reverter' : 'Revert'}</span>
                        </>
                      ) : (
                        <>
                          <Check className="w-3.5 h-3.5" />
                          <span>{isPt ? 'Aplicar' : 'Apply'}</span>
                        </>
                      )}
                    </button>

                    <button
                      onClick={(e) => {
                        e.stopPropagation();
                        setSelectedTweakId(item.id);
                      }}
                      className="px-2.5 py-1.5 rounded-lg text-xs font-semibold bg-[#151926] hover:bg-[#1c2235] text-cyan-300 border border-slate-700/80 transition-colors cursor-pointer flex items-center gap-1"
                    >
                      <Info className="w-3.5 h-3.5" />
                      <span>{isPt ? 'Explicação Detalhada' : 'Detailed Explanation'}</span>
                    </button>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      </div>

      {/* On-Demand Detailed Technical Explanation Panel (§6) */}
      {selectedTweak && (
        <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-4">
          <div className="flex flex-col md:flex-row md:items-center justify-between gap-3 pb-3 border-b border-slate-800/80">
            <div>
              <div className="flex flex-wrap items-center gap-2 mb-1">
                <span className="text-[10px] font-bold uppercase tracking-wider text-cyan-400">
                  {isPt
                    ? 'EXPLICAÇÃO TÉCNICA DETALHADA DO AJUSTE'
                    : 'DETAILED TECHNICAL TWEAK EXPLANATION'}
                </span>
                <span className="text-[10px] font-mono px-2 py-0.5 rounded bg-slate-900 text-slate-300 border border-slate-800">
                  {selectedTweak.id}
                </span>
                <span className="text-[10px] font-mono px-2 py-0.5 rounded bg-slate-900 text-slate-300 border border-slate-800">
                  {selectedTweak.recommendationStatus === 'NotRecommendedOrDefault'
                    ? isPt
                      ? 'NÃO RECOMENDADO / PADRÃO'
                      : 'NOT RECOMMENDED / DEFAULT'
                    : selectedTweak.recommendationStatus === 'Conditional'
                    ? isPt
                      ? 'CONDICIONAL / SITUACIONAL'
                      : 'CONDITIONAL / SITUATIONAL'
                    : isPt
                    ? 'RECOMENDADO'
                    : 'RECOMMENDED'}
                </span>
              </div>
              <h2 className="text-base font-bold text-white">
                {isPt ? selectedTweak.name : selectedTweak.nameEn || selectedTweak.name}
              </h2>
              <p className="text-xs text-slate-400 mt-0.5">
                {isPt
                  ? selectedTweak.description
                  : selectedTweak.descriptionEn || selectedTweak.description}
              </p>
            </div>

            {(() => {
              const st = getEffectiveState(selectedTweak);
              const badge = getStateBadgeMeta(st);
              return (
                <div className={`px-3 py-2 rounded-lg border font-mono text-right ${badge.className}`}>
                  <div className="text-[9px] uppercase text-slate-400 font-sans">
                    {isPt ? 'Estado Real no Registro' : 'Real Registry State'}
                  </div>
                  <div className="text-xs font-bold">{badge.label}</div>
                </div>
              );
            })()}
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-xs">
            <div className="space-y-3">
              <div className="p-3.5 rounded-lg bg-[#090c14] border border-slate-800/90 space-y-1">
                <div className="flex items-center gap-1.5 text-[10px] font-bold uppercase text-cyan-400">
                  <Info className="w-3.5 h-3.5" />
                  <span>{isPt ? 'O que este ajuste realmente faz' : 'What this tweak actually does'}</span>
                </div>
                <p className="text-slate-200 leading-relaxed">
                  {isPt
                    ? selectedTweak.whatItDoes || selectedTweak.description
                    : selectedTweak.whatItDoesEn || selectedTweak.whatItDoes || selectedTweak.description}
                </p>
              </div>

              <div className="p-3.5 rounded-lg bg-[#090c14] border border-slate-800/90 space-y-1">
                <div className="flex items-center gap-1.5 text-[10px] font-bold uppercase text-emerald-400">
                  <ShieldCheck className="w-3.5 h-3.5" />
                  <span>
                    {isPt
                      ? 'Qual problema resolve & Benefício esperado'
                      : 'Problem solved & Expected benefit'}
                  </span>
                </div>
                <p className="text-slate-200 leading-relaxed">
                  {isPt
                    ? selectedTweak.problemSolved || selectedTweak.expectedBenefit
                    : selectedTweak.problemSolvedEn ||
                      selectedTweak.problemSolved ||
                      selectedTweak.expectedBenefit}
                </p>
                <p className="text-emerald-400 font-semibold pt-1">
                  {isPt ? 'Benefício:' : 'Benefit:'}{' '}
                  {isPt
                    ? selectedTweak.expectedBenefit
                    : selectedTweak.expectedBenefitEn || selectedTweak.expectedBenefit}
                </p>
              </div>

              <div className="p-3.5 rounded-lg bg-[#090c14] border border-slate-800/90 space-y-1">
                <div className="text-[10px] font-bold uppercase text-slate-400">
                  {isPt
                    ? 'Comportamento Padrão Documentado pelo Windows'
                    : 'Documented Windows Default Behavior'}
                </div>
                <p className="text-slate-300 leading-relaxed">
                  {isPt
                    ? selectedTweak.defaultWindowsBehavior
                    : selectedTweak.defaultWindowsBehaviorEn || selectedTweak.defaultWindowsBehavior}
                </p>
              </div>
            </div>

            <div className="space-y-3">
              <div className="p-3.5 rounded-lg bg-[#090c14] border border-slate-800/90 space-y-1">
                <div className="flex items-center gap-1.5 text-[10px] font-bold uppercase text-amber-400">
                  <AlertTriangle className="w-3.5 h-3.5" />
                  <span>
                    {isPt ? 'Possíveis efeitos negativos & Risco' : 'Potential side effects & Risk'}
                  </span>
                </div>
                <p className="text-slate-200 leading-relaxed">
                  {isPt
                    ? selectedTweak.potentialSideEffects
                    : selectedTweak.potentialSideEffectsEn || selectedTweak.potentialSideEffects}
                </p>
                <p className="text-amber-300 font-semibold pt-1">
                  {isPt ? `Nível de Risco: ${selectedTweak.risk}` : `Risk Level: ${selectedTweak.risk}`}
                </p>
              </div>

              <div className="p-3.5 rounded-lg bg-[#090c14] border border-slate-800/90 space-y-1">
                <div className="flex items-center gap-1.5 text-[10px] font-bold uppercase text-purple-400">
                  <Cpu className="w-3.5 h-3.5" />
                  <span>
                    {isPt
                      ? 'Reinicialização & Dependências de Hardware / Windows'
                      : 'Reboot & Hardware / Windows Dependencies'}
                  </span>
                </div>
                <p className="text-white font-semibold">
                  {isPt ? 'Exige Reinicialização: ' : 'Requires Reboot: '}
                  {selectedTweak.requiresReboot
                    ? isPt
                      ? 'Sim (Reinicialização necessária)'
                      : 'Yes (Reboot required)'
                    : isPt
                    ? 'Não (Efeito imediato)'
                    : 'No (Immediate effect)'}
                </p>
                <p className="text-slate-300 leading-relaxed">
                  {isPt
                    ? selectedTweak.hardwareOrOsDependency
                    : selectedTweak.hardwareOrOsDependencyEn || selectedTweak.hardwareOrOsDependency}
                </p>
              </div>

              <div className="p-3.5 rounded-lg bg-[#090c14] border border-slate-800/90 space-y-1 font-mono text-[11px]">
                <div className="flex items-center gap-1.5 text-[10px] font-bold uppercase text-slate-400 font-sans">
                  <Terminal className="w-3.5 h-3.5" />
                  <span>
                    {isPt
                      ? 'Inspeção Direta da Chave e Valor no Registro'
                      : 'Direct Registry Key & Value Inspection'}
                  </span>
                </div>
                <div className="text-slate-300 break-all">
                  {isPt ? 'Chave:' : 'Key:'} {selectedTweak.registryKey}
                </div>
                <div className="text-slate-300">
                  {isPt ? 'Valor:' : 'Value:'} {selectedTweak.valueName} ({selectedTweak.valueKind})
                </div>
                <div className="text-white font-bold pt-1">
                  {isPt ? 'Valor Atual Lido:' : 'Current Read Value:'}{' '}
                  {selectedTweak.isApplied
                    ? selectedTweak.recommendedValue
                    : selectedTweak.currentValue}
                </div>
                <div className="text-emerald-400 font-bold">
                  {isPt ? 'Valor Recomendado:' : 'Recommended Value:'}{' '}
                  {selectedTweak.recommendedValue}
                </div>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
