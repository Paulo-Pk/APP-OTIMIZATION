import React, { useState } from 'react';
import {
  Stethoscope,
  CheckCircle2,
  AlertTriangle,
  AlertCircle,
  HelpCircle,
  ShieldCheck,
  RefreshCw,
  Wrench,
  Check,
} from 'lucide-react';
import { DiagnosticIssue, HealthStatus, TabId } from '../../types';
import { useI18n } from '../../i18n';

interface DiagnosticsViewProps {
  onNavigate: (tab: TabId) => void;
  onOpenBoostModal: () => void;
}

export const DiagnosticsView: React.FC<DiagnosticsViewProps> = ({
  onNavigate,
  onOpenBoostModal,
}) => {
  const { t } = useI18n();

  const [issues, setIssues] = useState<DiagnosticIssue[]>([
    {
      id: 'diag-1',
      component: 'Gerenciamento de Energia',
      severity: 'Atenção',
      title: 'Plano de Energia Equilibrado Padrão Ativo',
      description: 'O Windows está alternando entre estados de economia C-State reduzindo a frequência de clock em transições rápidas de cena.',
      recommendation: 'Ativar o plano de energia Desempenho Máximo para manter multiplicadores altos.',
      autoFixAvailable: true,
      tweakIdRef: 'power-ultimate-plan',
    },
    {
      id: 'diag-2',
      component: 'Placa de Vídeo & GPU Hotspot',
      severity: 'Excelente',
      title: 'Gradiente Térmico Saudável (ΔT de 10.6°C)',
      description: 'A diferença entre o núcleo da GPU e o hotspot está dentro dos padrões nominais (<18°C), indicando excelente condutividade do composto térmico.',
      recommendation: 'Nenhuma ação necessária. Cooler e fluxo de ar operando perfeitamente.',
      autoFixAvailable: false,
    },
    {
      id: 'diag-3',
      component: 'Memória RAM & Cache',
      severity: 'Bom',
      title: 'Standby List Memory Ocupando 4.2GB',
      description: 'Páginas em cache de arquivos antigos não liberadas podem gerar micro-travamentos ao carregar novas texturas no jogo.',
      recommendation: 'Habilitar purga automática de Standby List no Boost Center.',
      autoFixAvailable: true,
      tweakIdRef: 'mem-standby-clean',
    },
    {
      id: 'diag-4',
      component: 'Inicialização do Windows',
      severity: 'Atenção',
      title: 'Inicialização Rápida (Fast Startup) Ativa',
      description: 'O recurso Fast Startup salva um instantâneo do kernel acumulando pequenas anomalias de drivers de áudio e GPU ao longo do tempo.',
      recommendation: 'Desativar Fast Startup para garantir reinicializações 100% limpas.',
      autoFixAvailable: true,
      tweakIdRef: 'start-fast-boot',
    },
    {
      id: 'diag-5',
      component: 'Integridade de Discos & SMART',
      severity: 'Excelente',
      title: 'Unidades NVMe Gen4 com 99% de Saúde',
      description: 'Nenhum setor defeituoso realocado ou erro CRC reportado nos controladores Samsung 990 PRO e Kingston KC3000.',
      recommendation: 'Integridade dos dados garantida.',
      autoFixAvailable: false,
    },
    {
      id: 'diag-6',
      component: 'Segurança do Sistema',
      severity: 'Excelente',
      title: 'Windows Defender & Isolamento de Núcleo Ativos',
      description: 'Todas as proteções essenciais de integridade de memória e antivírus estão em execução sem impacto de FPS.',
      recommendation: 'Mantenha a segurança ativa. O PC Control não desativa proteções vitais.',
      autoFixAvailable: false,
    },
  ]);

  const [fixedIds, setFixedIds] = useState<string[]>([]);

  const handleFixIssue = (issue: DiagnosticIssue) => {
    setFixedIds((prev) => [...prev, issue.id]);
  };

  const severityBadges: Record<HealthStatus, { label: string; color: string; icon: React.ElementType }> = {
    Excelente: { label: t.common.excellent, color: 'bg-emerald-500/20 text-emerald-300 border-emerald-500/40', icon: CheckCircle2 },
    Bom: { label: t.common.good, color: 'bg-cyan-500/20 text-cyan-300 border-cyan-500/40', icon: CheckCircle2 },
    Atenção: { label: t.common.warning, color: 'bg-amber-500/20 text-amber-300 border-amber-500/40', icon: AlertTriangle },
    Crítico: { label: t.common.critical, color: 'bg-rose-500/20 text-rose-300 border-rose-500/40', icon: AlertCircle },
  };

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Banner */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <Stethoscope className="w-4 h-4" /> {t.diagnostics.subtitle}
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">{t.diagnostics.title}</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl leading-relaxed">
            {t.diagnostics.description}
          </p>
        </div>

        <button
          onClick={onOpenBoostModal}
          className="flex items-center gap-2 px-4 py-2 rounded-lg bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white font-bold text-xs uppercase tracking-wider shadow-lg shadow-purple-600/30 transition-all cursor-pointer border border-purple-400/40"
        >
          <Wrench className="w-4 h-4" />
          <span>{t.diagnostics.fixWithOneClick}</span>
        </button>
      </div>

      {/* Issues List */}
      <div className="space-y-3">
        {issues.map((issue) => {
          const isFixed = fixedIds.includes(issue.id);
          const badge = severityBadges[isFixed ? 'Excelente' : issue.severity];
          const Icon = badge.icon;

          return (
            <div
              key={issue.id}
              className={`p-4 rounded-xl border transition-all ${
                isFixed
                  ? 'bg-[#0f1422] border-emerald-500/30'
                  : issue.severity === 'Atenção'
                  ? 'bg-[#12141f] border-amber-900/30'
                  : 'bg-[#0f121b] border-slate-800'
              } flex flex-col md:flex-row items-start md:items-center justify-between gap-4`}
            >
              <div className="space-y-1 max-w-3xl">
                <div className="flex items-center gap-2">
                  <span className={`text-[10px] font-bold px-2 py-0.5 rounded border flex items-center gap-1 ${badge.color}`}>
                    <Icon className="w-3 h-3" />
                    <span>{isFixed ? t.diagnostics.fixApplied : badge.label}</span>
                  </span>
                  <span className="text-slate-600">·</span>
                  <span className="text-xs font-bold text-purple-400/90 uppercase">
                    {issue.component}
                  </span>
                </div>

                <h3 className="font-bold text-sm text-white">{issue.title}</h3>
                <p className="text-xs text-slate-400 leading-relaxed">
                  {issue.description}
                </p>

                <div className="text-xs text-purple-300 font-mono pt-1">
                  {t.diagnostics.recommendation} {issue.recommendation}
                </div>
              </div>

              {issue.autoFixAvailable && !isFixed && (
                <button
                  onClick={() => handleFixIssue(issue)}
                  className="px-3.5 py-1.5 rounded-lg bg-[#181c2d] hover:bg-[#20273d] border border-purple-500/40 text-purple-200 text-xs font-semibold cursor-pointer transition-colors shrink-0 flex items-center gap-1.5"
                >
                  <Wrench className="w-3.5 h-3.5 text-purple-400" />
                  <span>{t.diagnostics.applyFix}</span>
                </button>
              )}

              {isFixed && (
                <div className="flex items-center gap-1.5 text-xs font-bold text-emerald-400 shrink-0 font-mono">
                  <Check className="w-4 h-4" /> {t.diagnostics.fixApplied}
                </div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
};
