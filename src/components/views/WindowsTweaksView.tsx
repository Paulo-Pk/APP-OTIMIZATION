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
import { useI18n } from '../../i18n';

interface WindowsTweaksViewProps {
  tweaks: SystemTweak[];
  onToggleTweak: (id: string) => void;
  onRestoreAll: () => void;
  onRefreshTweakStates: () => void;
  isRefreshingTweakStates: boolean;
  inspectionSummary: string;
}

const simpleExplanations: Record<string, string> = {
  'perf-mmcss': 'Ajusta como o Windows divide o tempo do processador entre tarefas multimídia e outros programas. O resultado varia e não garante mais FPS.',
  'perf-game-dvr': 'Desliga a gravação automática em segundo plano do Xbox. Se você usa clipes automáticos, eles podem parar de funcionar.',
  'perf-hpet': 'Muda a forma como o Windows agenda tarefas do processador. Pode não trazer diferença perceptível; teste e reverta se notar piora.',
  'perf-tcp-nagle': 'Altera uma regra de tráfego multimídia do Windows. Não reduz ping por si só.',
  'start-fast-boot': 'Desliga a inicialização rápida. O computador pode demorar um pouco mais para ligar, mas fará uma inicialização completa.',
  'start-edge-preload': 'Impede o Edge de ficar pré-carregado quando você não está usando o navegador.',
  'bg-telemetry': 'Reduz os dados de diagnóstico enviados ao Windows, quando sua edição permite essa configuração.',
  'bg-cortana-news': 'Oculta o painel de notícias e interesses da barra de tarefas.',
  'power-ultimate-plan': 'Evita que o Windows reduza o consumo de energia de alguns processos. Pode aumentar consumo e temperatura.',
  'power-pcie-link': 'Muda a prioridade que o Windows dá a tarefas de jogos. O efeito depende do jogo e do sistema.',
  'serv-sysmain': 'Muda o SysMain para inicialização manual. Pode alterar o carregamento de programas; não é indicado como ganho universal.',
  'serv-spooler': 'Muda o serviço de impressão para manual. Se você imprime, o Windows poderá precisar iniciá-lo quando necessário.',
  'mem-standby-clean': 'Mantém a configuração comum de cache de arquivos do Windows. Não limpa memória RAM nem aumenta FPS por si só.',
  'mem-lock-executive': 'Mantém partes do núcleo do Windows na memória RAM. Pode consumir mais memória; só aplique se souber que há folga para isso.',
  'game-hags': 'Ativa o agendamento de GPU por hardware, se suportado. O resultado varia conforme placa de vídeo, driver e jogo.',
  'game-fso-override': 'Altera o modo de compatibilidade de tela cheia. Se algum jogo apresentar problemas, restaure o padrão.',
  'sys-delivery-p2p': 'Impede que seu PC compartilhe atualizações com outros computadores pela rede.',
  'sys-transparency': 'Remove efeitos de transparência do Windows. É principalmente uma mudança visual, sem ganho garantido em PCs modernos.',
  'perf-fso-flags': 'Muda outra opção de compatibilidade de tela cheia. Pode afetar alguns jogos; não promete mais desempenho.',
  'storage-ntfs-lastaccess': 'Altera quando o Windows atualiza a data do último acesso de arquivos. O ganho costuma ser pequeno ou inexistente.',
  'net-tcp-autotuning': 'Mantém um valor de referência das tentativas de retransmissão TCP. Não é uma forma de diminuir o ping.',
  'priv-advertising-id': 'Desativa o identificador usado para personalizar anúncios no Windows.',
  'maint-startup-delay': 'Remove um atraso aplicado a alguns programas durante a inicialização. Nem todos os programas respeitam essa opção.',
};

export const WindowsTweaksView: React.FC<WindowsTweaksViewProps> = ({
  tweaks,
  onToggleTweak,
  onRestoreAll,
  onRefreshTweakStates,
  isRefreshingTweakStates,
  inspectionSummary,
}) => {
  const { t } = useI18n();
  const [activeCategory, setActiveCategory] = useState<string>('all');

  const categories = [
    { id: 'all', label: t.boostCenter.allCategories },
    { id: 'power', label: t.boostCenter.catPower },
    { id: 'startup', label: t.boostCenter.catStartup },
    { id: 'services', label: t.boostCenter.catServices },
    { id: 'system', label: t.boostCenter.catSystem },
    { id: 'gaming', label: t.boostCenter.catGaming },
    { id: 'background', label: t.boostCenter.catBackground },
  ];

  const filtered = tweaks.filter((item) => {
    if (activeCategory === 'all') return true;
    return item.category === activeCategory;
  });

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Banner */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <Sliders className="w-4 h-4" /> {t.windowsTweaks.subtitle}
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">Ajustes do Windows</h1>
          <p className="text-xs text-slate-300 mt-1">Escolha apenas o que você entende. Cada opção explica o que muda, o possível benefício e os riscos antes de aplicar.</p>
        </div>

        <div className="flex items-center gap-2">
        <button
          onClick={onRefreshTweakStates}
          disabled={isRefreshingTweakStates}
          className="flex items-center gap-1.5 px-3 py-2 rounded-lg bg-[#141724] hover:bg-[#1b2033] border border-cyan-800/60 text-cyan-200 text-xs font-semibold disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <Sliders className={`w-3.5 h-3.5 ${isRefreshingTweakStates ? 'animate-spin' : ''}`} />
          <span>{isRefreshingTweakStates ? 'Consultando...' : 'Atualizar estados'}</span>
        </button>
        <button
          onClick={onRestoreAll}
          className="flex items-center gap-1.5 px-3 py-2 rounded-lg bg-[#141724] hover:bg-[#1b2033] border border-slate-700 text-slate-300 hover:text-white text-xs font-semibold transition-colors cursor-pointer"
        >
          <RotateCcw className="w-3.5 h-3.5 text-slate-400" />
          <span>{t.windowsTweaks.restoreDefaults}</span>
        </button>
        </div>
      </div>
      <div className="px-4 py-3 rounded-lg bg-[#0f121b] border border-slate-800 text-[11px] text-slate-300">
        {inspectionSummary}
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
          {filtered.map((item) => (
            <div
              key={item.id}
              className="py-4 flex flex-col md:flex-row items-start md:items-center justify-between gap-4 text-xs"
            >
              <div className="space-y-1 max-w-2xl">
                <div className="flex items-center gap-2">
                  <span className="font-bold text-white text-sm">{item.name}</span>
                  {item.isApplied && (
                    <span className="text-[10px] font-mono font-bold px-2 py-0.5 rounded bg-purple-500/20 text-purple-300 border border-purple-500/30">
                      {t.windowsTweaks.modified}
                    </span>
                  )}
                </div>
                <p className="text-slate-200 leading-relaxed">{simpleExplanations[item.id] || item.description}</p>
                <p className="text-[11px] text-emerald-300/90 leading-relaxed">
                  Impacto esperado: {item.expectedBenefit}
                </p>
                <div className="text-[11px] text-slate-300 pt-1 flex flex-wrap gap-x-3 gap-y-1">
                  <span>Possível impacto: <strong>{item.risk === 'Baixo' ? 'baixo' : item.risk === 'Médio' ? 'moderado' : 'alto'}</strong></span>
                  <span>Estado atual: <strong>{item.registryState || 'Ainda não verificado'}</strong></span>
                </div>
                {item.registryDiagnostic && <p className="text-[11px] text-amber-300/90 leading-relaxed">{item.registryDiagnostic}</p>}
                <details className="pt-1 text-[10px] text-slate-500">
                  <summary className="cursor-pointer hover:text-purple-300">Ver detalhes técnicos</summary>
                  <div className="mt-2 space-y-1 font-mono break-all">
                    <div>Valor encontrado: {item.currentValue}</div>
                    <div>Tipo encontrado: {item.actualRegistryKind || 'N/D'} · Tipo esperado: {item.expectedRegistryKind || item.valueKind || 'N/D'}</div>
                    <div>{t.windowsTweaks.keyLabel} {item.registryKey || item.commandScript || t.windowsTweaks.systemRegistryLabel}</div>
                  </div>
                </details>
              </div>

              <div className="flex items-center gap-4 shrink-0 font-mono">
                <div className="text-right">
                  <span className="text-[10px] text-slate-500 block uppercase font-sans">
                    {t.windowsTweaks.systemValue}
                  </span>
                  <span className="text-slate-300 font-semibold truncate max-w-xs block">
                    {item.isApplied ? item.recommendedValue : item.currentValue}
                  </span>
                </div>

                <button
                  onClick={() => onToggleTweak(item.id)}
                  disabled={item.isApplied ? item.canRestore !== true : item.canApply !== true}
                  title={item.isApplied
                    ? (item.canRestore ? 'Restaurar estado original autenticado' : 'Sem histórico confiável pendente para restaurar')
                    : (item.canApply ? 'Aplicar após leitura confirmada do padrão/inativo' : 'Bloqueado até que a leitura confirme o padrão/inativo conhecido')}
                  className={`px-3 py-1.5 rounded-lg text-xs font-bold transition-all cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed flex items-center gap-1.5 ${
                    item.isApplied
                      ? 'bg-[#181c2d] text-slate-300 hover:bg-[#20263c] border border-slate-700'
                      : 'bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white border border-purple-400/30 shadow-sm'
                  }`}
                >
                  {item.isApplied ? (
                    <>
                      <RotateCcw className="w-3.5 h-3.5" />
                      <span>{t.common.undo}</span>
                    </>
                  ) : (
                    <>
                      <Check className="w-3.5 h-3.5" />
                      <span>{t.windowsTweaks.enable}</span>
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
