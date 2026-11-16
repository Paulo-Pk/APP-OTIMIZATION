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
  X,
} from 'lucide-react';
import { DiagnosticIssue, HealthStatus, TabId } from '../../types';
import { useI18n } from '../../i18n';
import { nativeBridge } from '../../services/nativeBridge';

interface DiagnosticsViewProps {
  onNavigate: (tab: TabId) => void;
  onOpenBoostModal: () => void;
  onApplyDiagnosticFix?: (
    tweakId: string,
    issueTitle: string,
    component: string
  ) => Promise<{ success: boolean; error?: string }>;
}

export const DiagnosticsView: React.FC<DiagnosticsViewProps> = ({
  onNavigate,
  onOpenBoostModal,
  onApplyDiagnosticFix,
}) => {
  const { t } = useI18n();

  const issues: DiagnosticIssue[] = [
    {
      id: 'diag-1',
      component: t.diagnostics.issue1Component,
      severity: 'Atenção',
      title: t.diagnostics.issue1Title,
      description: t.diagnostics.issue1Desc,
      recommendation: t.diagnostics.issue1Rec,
      autoFixAvailable: true,
      tweakIdRef: 'power-ultimate-plan',
    },
    {
      id: 'diag-2',
      component: t.diagnostics.issue2Component,
      severity: 'Excelente',
      title: t.diagnostics.issue2Title,
      description: t.diagnostics.issue2Desc,
      recommendation: t.diagnostics.issue2Rec,
      autoFixAvailable: false,
    },
    {
      id: 'diag-3',
      component: t.diagnostics.issue3Component,
      severity: 'Bom',
      title: t.diagnostics.issue3Title,
      description: t.diagnostics.issue3Desc,
      recommendation: t.diagnostics.issue3Rec,
      autoFixAvailable: true,
      tweakIdRef: 'mem-standby-clean',
    },
    {
      id: 'diag-4',
      component: t.diagnostics.issue4Component,
      severity: 'Atenção',
      title: t.diagnostics.issue4Title,
      description: t.diagnostics.issue4Desc,
      recommendation: t.diagnostics.issue4Rec,
      autoFixAvailable: true,
      tweakIdRef: 'start-fast-boot',
    },
    {
      id: 'diag-5',
      component: t.diagnostics.issue5Component,
      severity: 'Excelente',
      title: t.diagnostics.issue5Title,
      description: t.diagnostics.issue5Desc,
      recommendation: t.diagnostics.issue5Rec,
      autoFixAvailable: false,
    },
    {
      id: 'diag-6',
      component: t.diagnostics.issue6Component,
      severity: 'Excelente',
      title: t.diagnostics.issue6Title,
      description: t.diagnostics.issue6Desc,
      recommendation: t.diagnostics.issue6Rec,
      autoFixAvailable: false,
    },
  ];

  const [fixedIds, setFixedIds] = useState<string[]>([]);
  const [issueToConfirm, setIssueToConfirm] = useState<DiagnosticIssue | null>(null);
  const [isFixing, setIsFixing] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isAuditingSystemDoctor, setIsAuditingSystemDoctor] = useState<boolean>(false);
  const [systemDoctorSummary, setSystemDoctorSummary] = useState<string | null>(null);
  const [systemDoctorFindings, setSystemDoctorFindings] = useState<Array<{
    category: string;
    title: string;
    severity: string;
    evidence: string;
    probableCause: string;
    recommendedAction: string;
    requiresAdminForRepair: boolean;
    repairCommandId?: string | null;
  }>>([]);
  const [evidenceRecommendations, setEvidenceRecommendations] = useState<Array<{
    recommendationId: string;
    title: string;
    problemIdentified: string;
    evidenceCollected: string;
    expectedImpact: string;
    riskLevel: string;
    recommendedAction: string;
    targetModule: string;
  }>>([]);

  const handleRunSystemDoctorAudit = async () => {
    setErrorMessage(null);
    if (!nativeBridge.getStatus().isConnected) {
      setErrorMessage(t.diagnostics.agentRequiredDiag);
      return;
    }
    setIsAuditingSystemDoctor(true);
    const res = await nativeBridge.runSystemDoctorAudit();
    setIsAuditingSystemDoctor(false);
    if (!res.success) {
      setErrorMessage(res.error || 'Falha ao executar auditoria System Doctor no agente Windows.');
      return;
    }
    setSystemDoctorSummary(res.executiveSummary || null);
    setSystemDoctorFindings(res.findings);
    setEvidenceRecommendations(res.recommendations);
  };

  const handleRequestFix = (issue: DiagnosticIssue) => {
    setErrorMessage(null);
    setIssueToConfirm(issue);
  };

  const handleExecuteFix = async () => {
    if (!issueToConfirm || !issueToConfirm.tweakIdRef) return;
    setIsFixing(true);
    setErrorMessage(null);

    const isConnected = nativeBridge.getStatus().isConnected;
    if (!isConnected) {
      setErrorMessage(t.diagnostics.agentRequiredDiag);
      setIsFixing(false);
      setIssueToConfirm(null);
      return;
    }

    if (onApplyDiagnosticFix) {
      const res = await onApplyDiagnosticFix(
        issueToConfirm.tweakIdRef,
        issueToConfirm.title,
        issueToConfirm.component
      );
      if (res.success) {
        setFixedIds((prev) => [...prev, issueToConfirm.id]);
        setIssueToConfirm(null);
      } else {
        setErrorMessage(res.error || t.diagnostics.fixFailed);
      }
    } else {
      setErrorMessage('Gerenciador de correções não inicializado.');
    }

    setIsFixing(false);
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

        <div className="flex flex-wrap items-center gap-2">
          <button
            onClick={handleRunSystemDoctorAudit}
            disabled={isAuditingSystemDoctor}
            className="flex items-center gap-2 px-4 py-2 rounded-lg bg-[#181c2d] hover:bg-[#20273d] text-purple-200 font-bold text-xs uppercase tracking-wider transition-all cursor-pointer border border-purple-500/40 disabled:opacity-50"
          >
            <RefreshCw className={`w-4 h-4 text-purple-400 ${isAuditingSystemDoctor ? 'animate-spin' : ''}`} />
            <span>{isAuditingSystemDoctor ? 'Verificando...' : 'Auditoria System Doctor'}</span>
          </button>

          <button
            onClick={onOpenBoostModal}
            className="flex items-center gap-2 px-4 py-2 rounded-lg bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white font-bold text-xs uppercase tracking-wider shadow-lg shadow-purple-600/30 transition-all cursor-pointer border border-purple-400/40"
          >
            <Wrench className="w-4 h-4" />
            <span>{t.diagnostics.fixWithOneClick}</span>
          </button>
        </div>
      </div>

      {/* Error Message Notice */}
      {errorMessage && (
        <div className="p-4 rounded-xl bg-[#261316] border border-rose-500/60 text-xs text-rose-300 flex items-center justify-between animate-scale-in">
          <div className="flex items-center gap-2.5">
            <AlertCircle className="w-5 h-5 text-rose-400 shrink-0" />
            <span>{errorMessage}</span>
          </div>
          <button
            onClick={() => setErrorMessage(null)}
            className="text-slate-400 hover:text-white font-bold text-xs cursor-pointer ml-3"
          >
            {t.common.close}
          </button>
        </div>
      )}

      {/* Real System Doctor & Evidence-Based Recommendations Results */}
      {systemDoctorSummary && (
        <div className="p-4 rounded-xl bg-[#0f121b] border border-purple-500/40 space-y-3">
          <div className="text-xs font-bold text-purple-300 uppercase tracking-wider">
            Resultado da Auditoria Real (System Doctor & Recommendation Engine)
          </div>
          <p className="text-xs text-slate-300">{systemDoctorSummary}</p>
          {systemDoctorFindings.length > 0 && (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-3 pt-1">
              {systemDoctorFindings.map((finding, idx) => (
                <div key={idx} className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800 space-y-1">
                  <div className="flex items-center justify-between text-[11px]">
                    <span className="font-bold text-purple-400 uppercase">{finding.category}</span>
                    <span className="font-mono text-slate-400">{finding.severity}</span>
                  </div>
                  <div className="text-xs font-bold text-white">{finding.title}</div>
                  <div className="text-[11px] text-slate-400">{finding.evidence}</div>
                  <div className="text-[11px] text-emerald-300 font-mono">{finding.recommendedAction}</div>
                </div>
              ))}
            </div>
          )}
          {evidenceRecommendations.length > 0 && (
            <div className="space-y-2 pt-2 border-t border-slate-800/80">
              <div className="text-[11px] font-bold text-cyan-300 uppercase">
                Recomendações Baseadas em Evidências Reais
              </div>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                {evidenceRecommendations.map((rec) => (
                  <div key={rec.recommendationId} className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800 space-y-1">
                    <div className="flex items-center justify-between text-[11px]">
                      <span className="font-bold text-cyan-400">{rec.targetModule}</span>
                      <span className="font-mono text-emerald-400">{rec.riskLevel}</span>
                    </div>
                    <div className="text-xs font-bold text-white">{rec.title}</div>
                    <div className="text-[11px] text-slate-400">{rec.evidenceCollected}</div>
                    <div className="text-[11px] text-purple-300 font-mono">{rec.recommendedAction}</div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      )}

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
                  onClick={() => handleRequestFix(issue)}
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

      {/* Confirmation Modal for Diagnostic Repair */}
      {issueToConfirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/80 backdrop-blur-sm animate-fade-in">
          <div className="w-full max-w-md bg-[#0f121b] border border-purple-500/40 rounded-xl p-6 shadow-2xl space-y-4 animate-scale-in">
            <div className="flex items-start justify-between gap-3">
              <div className="flex items-center gap-2 text-purple-400">
                <Wrench className="w-5 h-5 text-purple-400" />
                <h3 className="font-bold text-base text-white">{t.diagnostics.confirmFixTitle}</h3>
              </div>
              <button
                onClick={() => setIssueToConfirm(null)}
                className="text-slate-400 hover:text-white cursor-pointer"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            <div className="space-y-1">
              <span className="text-[11px] font-bold uppercase text-purple-400">
                {issueToConfirm.component}
              </span>
              <p className="text-sm font-bold text-white">{issueToConfirm.title}</p>
              <p className="text-xs text-slate-300 leading-relaxed pt-1">
                {issueToConfirm.recommendation}
              </p>
            </div>

            <div className="p-3 rounded-lg bg-[#141829] border border-slate-800 text-xs flex items-start gap-2 text-slate-300">
              <ShieldCheck className="w-4 h-4 text-emerald-400 shrink-0 mt-0.5" />
              <span>{t.diagnostics.confirmFixWarning}</span>
            </div>

            <div className="flex justify-end items-center gap-3 pt-2">
              <button
                onClick={() => setIssueToConfirm(null)}
                className="px-4 py-2 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 text-xs font-semibold cursor-pointer transition-colors"
              >
                {t.diagnostics.confirmFixCancel}
              </button>
              <button
                onClick={handleExecuteFix}
                disabled={isFixing}
                className="px-4 py-2 rounded-lg bg-purple-600 hover:bg-purple-500 text-white text-xs font-bold cursor-pointer transition-colors shadow-lg shadow-purple-600/30 border border-purple-400/40 disabled:opacity-50"
              >
                {isFixing ? t.common.loading : t.diagnostics.confirmFixConfirm}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
