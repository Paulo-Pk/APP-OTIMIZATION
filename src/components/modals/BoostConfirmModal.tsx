import React from 'react';
import {
  Zap,
  ShieldAlert,
  CheckCircle2,
  X,
  History,
  AlertTriangle,
} from 'lucide-react';
import { SystemTweak } from '../../types';

interface BoostConfirmModalProps {
  isOpen: boolean;
  onClose: () => void;
  onConfirm: () => void;
  pendingTweaks: SystemTweak[];
}

export const BoostConfirmModal: React.FC<BoostConfirmModalProps> = ({
  isOpen,
  onClose,
  onConfirm,
  pendingTweaks,
}) => {
  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 bg-black/80 backdrop-blur-xs flex items-center justify-center p-4">
      <div className="w-full max-w-2xl bg-[#0f121b] border border-purple-500/50 rounded-xl p-6 space-y-5 shadow-2xl animate-scale-in">
        {/* Header */}
        <div className="flex items-center justify-between pb-3 border-b border-slate-800">
          <div className="flex items-center gap-2.5">
            <div className="w-8 h-8 rounded-lg bg-purple-600/20 border border-purple-500/40 flex items-center justify-center">
              <Zap className="w-4 h-4 text-purple-400" />
            </div>
            <div>
              <h2 className="text-base font-black text-white uppercase tracking-wider">
                Análise Prévia & Autorização de Otimizações
              </h2>
              <span className="text-xs text-slate-400">
                Auditoria do Sistema PC CONTROL ULTIMATE
              </span>
            </div>
          </div>

          <button
            onClick={onClose}
            className="text-slate-400 hover:text-white font-bold text-lg cursor-pointer"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Audit Summary Notice */}
        <div className="p-3.5 rounded-lg bg-[#141828] border border-purple-900/40 text-xs text-slate-300 space-y-1.5 leading-relaxed">
          <div className="font-bold text-purple-300 flex items-center gap-1.5">
            <History className="w-4 h-4" /> Ponto de Restauração Local Agendado
          </div>
          <p className="text-slate-400">
            Foram identificados <strong>{pendingTweaks.length} ajustes recomendados</strong> para seu hardware. Todos os valores prévios serão registrados no banco SQLite local para permitir reversão individual ou completa.
          </p>
        </div>

        {/* Proposed Tweaks List */}
        <div className="max-h-60 overflow-y-auto space-y-2 pr-1">
          {pendingTweaks.map((tweak) => (
            <div
              key={tweak.id}
              className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800/80 flex items-center justify-between gap-3 text-xs"
            >
              <div className="space-y-0.5">
                <div className="flex items-center gap-2">
                  <span className="font-bold text-white">{tweak.name}</span>
                  <span className="text-[10px] px-1.5 py-0.2 rounded bg-purple-500/10 text-purple-300 border border-purple-500/30">
                    {tweak.category}
                  </span>
                </div>
                <div className="text-[11px] text-slate-400">
                  {tweak.expectedBenefit}
                </div>
              </div>

              <span className="text-[10px] font-bold px-2 py-0.5 rounded bg-emerald-500/10 text-emerald-400 border border-emerald-500/30 shrink-0">
                Risco: {tweak.risk}
              </span>
            </div>
          ))}
        </div>

        {/* Actions */}
        <div className="flex items-center justify-between pt-3 border-t border-slate-800">
          <span className="text-xs text-slate-500">
            Requer autorização explícita do usuário
          </span>

          <div className="flex items-center gap-3">
            <button
              onClick={onClose}
              className="px-4 py-2 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 text-xs font-semibold cursor-pointer transition-colors"
            >
              Cancelar
            </button>

            <button
              onClick={onConfirm}
              className="px-5 py-2 rounded-lg bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white font-bold text-xs uppercase tracking-wider shadow-lg shadow-purple-600/30 transition-all cursor-pointer border border-purple-400/40 flex items-center gap-2"
            >
              <CheckCircle2 className="w-4 h-4" />
              <span>Autorizar e Aplicar Agora</span>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};
