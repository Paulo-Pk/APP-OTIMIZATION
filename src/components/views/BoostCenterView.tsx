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
import { useI18n } from '../../i18n';

interface BoostCenterViewProps {
  tweaks: SystemTweak[];
  onToggleTweak: (id: string) => void;
  onApplyAllLevel: (level: OptimizationLevel) => void;
  onRestoreAll: () => void;
  onOpenBoostModal: () => void;
}

const plainTweakText: Record<string, { title: string; explanation: string }> = {
  'perf-mmcss': { title: 'Ajustar prioridades de tarefas multimídia', explanation: 'Muda como o Windows distribui o tempo do processador. O resultado varia e não garante mais FPS.' },
  'perf-game-dvr': { title: 'Desligar gravação automática do Xbox', explanation: 'Evita a captura de vídeo em segundo plano. Clipes automáticos podem deixar de funcionar.' },
  'perf-hpet': { title: 'Ajustar a resposta do processador', explanation: 'Muda uma regra de agendamento de tarefas do Windows. Pode não trazer diferença perceptível.' },
  'perf-tcp-nagle': { title: 'Ajustar tráfego de rede multimídia', explanation: 'Muda uma regra de tráfego do Windows. Não reduz ping por si só.' },
  'start-fast-boot': { title: 'Desligar a inicialização rápida', explanation: 'O PC pode demorar um pouco mais para ligar, mas fará uma inicialização completa.' },
  'start-edge-preload': { title: 'Evitar pré-carregamento do Edge', explanation: 'Impede o navegador de manter componentes carregados quando você não o está usando.' },
  'bg-telemetry': { title: 'Reduzir dados de diagnóstico', explanation: 'Reduz os dados de diagnóstico enviados ao Windows quando sua edição permite.' },
  'bg-cortana-news': { title: 'Ocultar notícias da barra de tarefas', explanation: 'Oculta o painel de notícias e interesses do Windows.' },
  'power-ultimate-plan': { title: 'Reduzir economia de energia em alguns processos', explanation: 'Pode elevar consumo e temperatura. Não força a CPU a operar no clock máximo.' },
  'power-pcie-link': { title: 'Ajustar prioridade de tarefas de jogos', explanation: 'Muda uma prioridade usada pelo Windows durante tarefas de jogos; o efeito varia.' },
  'serv-sysmain': { title: 'Mudar o SysMain para manual', explanation: 'Pode alterar o carregamento de programas. Não é um ganho universal de desempenho.' },
  'serv-spooler': { title: 'Mudar o serviço de impressão para manual', explanation: 'Se você imprime, o Windows poderá precisar iniciar o serviço quando necessário.' },
  'mem-standby-clean': { title: 'Manter o cache padrão do Windows', explanation: 'Não limpa a RAM nem aumenta FPS por si só.' },
  'mem-lock-executive': { title: 'Manter partes do Windows na memória', explanation: 'Pode consumir mais RAM. Só aplique se houver memória disponível suficiente.' },
  'game-hags': { title: 'Agendamento de GPU por hardware', explanation: 'O resultado varia conforme placa de vídeo, driver e jogo.' },
  'game-fso-override': { title: 'Ajustar compatibilidade de tela cheia', explanation: 'Pode afetar alguns jogos. Reverta se aparecerem problemas de imagem.' },
  'sys-delivery-p2p': { title: 'Impedir compartilhamento de atualizações', explanation: 'Evita que seu PC compartilhe atualizações com outros computadores pela rede.' },
  'sys-transparency': { title: 'Desligar transparências do Windows', explanation: 'Muda a aparência da interface. Não há ganho garantido em PCs modernos.' },
  'perf-fso-flags': { title: 'Ajustar compatibilidade de tela cheia', explanation: 'É uma opção de compatibilidade; não garante mais FPS.' },
  'storage-ntfs-lastaccess': { title: 'Reduzir atualização de acesso a arquivos', explanation: 'Muda quando o Windows registra o último acesso de arquivos; o ganho pode ser inexistente.' },
  'net-tcp-autotuning': { title: 'Manter configuração de retransmissão de rede', explanation: 'Não é uma otimização de ping nem promete reduzir a latência.' },
  'priv-advertising-id': { title: 'Desativar identificador de anúncios', explanation: 'Reduz o uso do identificador para personalizar anúncios no Windows.' },
  'maint-startup-delay': { title: 'Reduzir atraso de abertura de programas', explanation: 'Pode alterar o atraso de alguns aplicativos ao iniciar o Windows.' },
};

export const BoostCenterView: React.FC<BoostCenterViewProps> = ({
  tweaks,
  onToggleTweak,
  onApplyAllLevel,
  onRestoreAll,
  onOpenBoostModal,
}) => {
  const { t } = useI18n();
  const [selectedLevel, setSelectedLevel] = useState<OptimizationLevel>('balanced');
  const [selectedCategory, setSelectedCategory] = useState<string>('all');
  const [searchQuery, setSearchQuery] = useState('');

  const categoryLabels: Record<TweakCategory, string> = {
    performance: t.boostCenter.catPerformance,
    startup: t.boostCenter.catStartup,
    background: t.boostCenter.catBackground,
    power: t.boostCenter.catPower,
    services: t.boostCenter.catServices,
    memory: t.boostCenter.catMemory,
    gaming: t.boostCenter.catGaming,
    system: t.boostCenter.catSystem,
  };

  const filteredTweaks = tweaks.filter((item) => {
    const matchesLevel =
      selectedLevel === 'advanced'
        ? true
        : selectedLevel === 'balanced'
        ? item.level === 'conservative' || item.level === 'balanced'
        : item.level === 'conservative';

    const matchesCategory =
      selectedCategory === 'all' || item.category === selectedCategory;

    const matchesSearch =
      searchQuery === '' ||
      item.name.toLowerCase().includes(searchQuery.toLowerCase()) ||
      item.description.toLowerCase().includes(searchQuery.toLowerCase());

    return matchesLevel && matchesCategory && matchesSearch;
  });

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Header Banner */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <Zap className="w-4 h-4" /> {t.boostCenter.subtitle}
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">{t.boostCenter.title}</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl leading-relaxed">
            {t.boostCenter.description}
          </p>
        </div>

        <div className="flex items-center gap-3">
          <button
            onClick={onRestoreAll}
            className="flex items-center gap-1.5 px-3 py-2 rounded-lg bg-[#141724] hover:bg-[#1b2033] border border-slate-700 text-slate-300 hover:text-white text-xs font-semibold transition-colors cursor-pointer"
          >
            <RotateCcw className="w-3.5 h-3.5 text-slate-400" />
            <span>{t.boostCenter.restoreAll}</span>
          </button>

          <button
            onClick={onOpenBoostModal}
            className="flex items-center gap-2 px-4 py-2 rounded-lg bg-gradient-to-r from-purple-600 to-violet-600 hover:from-purple-500 hover:to-violet-500 text-white font-bold text-xs uppercase tracking-wider shadow-lg shadow-purple-600/30 transition-all cursor-pointer border border-purple-400/40"
          >
            <Zap className="w-4 h-4 fill-current" />
            <span>{t.boostCenter.applyProfile} {selectedLevel.toUpperCase()}</span>
          </button>
        </div>
      </div>

      {/* Level Selector & Controls Bar */}
      <div className="flex flex-col lg:flex-row items-start lg:items-center justify-between gap-4 p-4 rounded-xl bg-[#0f121b] border border-slate-800">
        <div className="flex items-center gap-2">
          <span className="text-xs text-slate-400 font-semibold mr-1">{t.boostCenter.profileLabel}</span>
          {(['conservative', 'balanced', 'advanced'] as OptimizationLevel[]).map(
            (lvl) => {
              const isActive = selectedLevel === lvl;
              const labels = {
                conservative: t.boostCenter.profileConservative,
                balanced: t.boostCenter.profileBalanced,
                advanced: t.boostCenter.profileAdvanced,
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
            placeholder={t.common.searchPlaceholder}
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="w-full sm:w-48 bg-[#0a0c13] border border-slate-800 rounded-lg px-3 py-1.5 text-xs text-slate-200 placeholder-slate-500 focus:outline-none focus:border-purple-500"
          />

          <select
            value={selectedCategory}
            onChange={(e) => setSelectedCategory(e.target.value)}
            className="bg-[#0a0c13] border border-slate-800 rounded-lg px-3 py-1.5 text-xs text-slate-300 focus:outline-none focus:border-purple-500 cursor-pointer"
          >
            <option value="all">{t.boostCenter.allCategories}</option>
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
                      {t.common.risk}: {isRiskHigh ? t.common.riskHigh : isRiskMed ? t.common.riskMedium : t.common.riskLow}
                    </span>

                    {tweak.isApplied && (
                      <span className="text-[10px] font-bold px-2 py-0.5 rounded bg-purple-500/20 text-purple-300 border border-purple-500/40 flex items-center gap-1">
                        <Check className="w-3 h-3" /> {t.common.applied}
                      </span>
                    )}
                  </div>
                </div>

                <h3 className="text-sm font-bold text-white leading-snug">
                  {plainTweakText[tweak.id]?.title || tweak.name}
                </h3>

                <p className="text-xs text-slate-400 leading-relaxed">
                  {plainTweakText[tweak.id]?.explanation || tweak.description}
                </p>

                <div className="p-2.5 rounded-lg bg-[#0a0c13] border border-slate-800/80 text-xs">
                  <span className="text-emerald-400 font-semibold block text-[10px] uppercase tracking-wider mb-0.5">
                    {t.boostCenter.expectedBenefit}
                  </span>
                  <span className="text-slate-300 text-[11px]">
                    {plainTweakText[tweak.id]?.explanation || tweak.expectedBenefit}
                  </span>
                </div>
                <div className="text-[10px] text-slate-300">Situação no Windows: <strong>{tweak.registryState || 'Ainda não verificado'}</strong></div>
                {tweak.registryDiagnostic && <p className="text-[10px] text-amber-300/90">{tweak.registryDiagnostic}</p>}
                <details className="text-[10px] text-slate-500"><summary className="cursor-pointer hover:text-purple-300">Ver detalhes técnicos</summary>
                <div className="grid grid-cols-2 gap-2 text-[11px] font-mono pt-2">
                  <div className="p-2 rounded bg-[#090b11] border border-slate-900">
                    <span className="text-slate-500 block text-[9px] uppercase font-sans">
                      {t.boostCenter.currentConfig}
                    </span>
                    <span className="text-slate-400 truncate block" title={tweak.currentValue}>
                      {tweak.currentValue}
                    </span>
                  </div>
                  <div className="p-2 rounded bg-[#090b11] border border-purple-950/40">
                    <span className="text-purple-400 block text-[9px] uppercase font-sans">
                      {t.boostCenter.recommendedConfig}
                    </span>
                    <span className="text-purple-300 truncate block" title={tweak.recommendedValue}>
                      {tweak.recommendedValue}
                    </span>
                  </div>
                </div>
                </details>
              </div>

              <div className="pt-2 border-t border-slate-800/60 flex items-center justify-between gap-2">
                <span className="text-[10px] text-slate-500 font-mono">
                  {tweak.reversible ? t.common.reversible : 'Manual'}
                </span>

                <div className="flex items-center gap-2">
                  {tweak.isApplied ? (
                    <button
                      onClick={() => onToggleTweak(tweak.id)}
                      disabled={tweak.canRestore !== true}
                      title={tweak.canRestore ? 'Restaurar estado original autenticado' : 'Sem histórico confiável pendente para restaurar'}
                      className="px-3 py-1.5 rounded-lg bg-[#161a29] disabled:opacity-40 disabled:cursor-not-allowed hover:bg-[#1f243a] border border-slate-700 text-slate-300 text-xs font-semibold transition-colors cursor-pointer flex items-center gap-1.5"
                    >
                      <RotateCcw className="w-3.5 h-3.5 text-slate-400" />
                      <span>{t.common.restore}</span>
                    </button>
                  ) : (
                    <button
                      onClick={() => onToggleTweak(tweak.id)}
                      disabled={tweak.canApply !== true}
                      title={tweak.canApply ? 'Aplicar após leitura confirmada do padrão/inativo' : 'Bloqueado até leitura confirmada do Registro'}
                      className="px-3.5 py-1.5 rounded-lg bg-gradient-to-r disabled:opacity-40 disabled:cursor-not-allowed from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white text-xs font-bold transition-all cursor-pointer border border-purple-400/40 shadow-sm flex items-center gap-1.5"
                    >
                      <Zap className="w-3.5 h-3.5 fill-current" />
                      <span>{t.common.apply}</span>
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
