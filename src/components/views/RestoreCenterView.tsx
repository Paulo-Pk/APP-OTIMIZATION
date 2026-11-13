import React, { useState } from 'react';
import {
  History,
  RotateCcw,
  Shield,
  Plus,
  AlertTriangle,
  CheckCircle2,
  X,
} from 'lucide-react';
import { RestoreItem } from '../../types';
import { useI18n } from '../../i18n';

interface RestoreCenterViewProps {
  restoreItems: RestoreItem[];
  onRollbackItem: (id: string) => void;
  onRestoreAllPoints: () => void;
  onCreateSnapshot: () => void;
}

export const RestoreCenterView: React.FC<RestoreCenterViewProps> = ({
  restoreItems,
  onRollbackItem,
  onRestoreAllPoints,
  onCreateSnapshot,
}) => {
  const { t } = useI18n();
  const [confirmTarget, setConfirmTarget] = useState<{
    mode: 'single' | 'all';
    item?: RestoreItem;
  } | null>(null);

  const handleConfirmExecution = () => {
    if (!confirmTarget) return;
    if (confirmTarget.mode === 'all') {
      onRestoreAllPoints();
    } else if (confirmTarget.item) {
      onRollbackItem(confirmTarget.item.id);
    }
    setConfirmTarget(null);
  };

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Banner */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <History className="w-4 h-4" /> {t.restoreCenter.subtitle}
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">{t.restoreCenter.title}</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl leading-relaxed">
            {t.restoreCenter.description}
          </p>
        </div>

        <div className="flex items-center gap-3">
          <button
            onClick={onCreateSnapshot}
            className="flex items-center gap-1.5 px-3 py-2 rounded-lg bg-[#141724] hover:bg-[#1b2033] border border-slate-700 text-slate-300 hover:text-white text-xs font-semibold transition-colors cursor-pointer"
          >
            <Plus className="w-4 h-4 text-purple-400" />
            <span>{t.restoreCenter.createSnapshot}</span>
          </button>

          <button
            onClick={() => setConfirmTarget({ mode: 'all' })}
            disabled={restoreItems.length === 0}
            className="flex items-center gap-2 px-4 py-2 rounded-lg bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white font-bold text-xs uppercase tracking-wider shadow-lg shadow-purple-600/30 transition-all cursor-pointer border border-purple-400/40 disabled:opacity-50"
          >
            <RotateCcw className="w-4 h-4" />
            <span>{t.restoreCenter.undoAll}</span>
          </button>
        </div>
      </div>

      {/* Explicit Technical Notice: Supported Registry Settings Backup (Not Windows System Restore) */}
      <div className="p-4 rounded-xl bg-[#11131c] border border-slate-800 flex items-start gap-3 text-xs text-slate-400 leading-relaxed">
        <Shield className="w-4 h-4 text-amber-400 shrink-0 mt-0.5" />
        <div>
          <span className="font-bold text-slate-200">Escopo do Backup de Registro: </span>
          Este módulo gerencia o backup assinado (SHA-256 canônico) exclusivamente dos ajustes de Registro suportados pelo aplicativo, preservando tipos originais (DWORD, QWORD, STRING, EXPAND_SZ, MULTI_SZ, BINARY) e excluindo valores originalmente inexistentes. Não substitui um Ponto de Restauração de volume completo do Windows.
        </div>
      </div>

      {/* Scope & Confirmation Modal before Restoration */}
      {confirmTarget && (
        <div className="fixed inset-0 z-50 bg-black/80 flex items-center justify-center p-4">
          <div className="w-full max-w-lg rounded-xl bg-[#0f121b] border border-purple-500/50 p-5 space-y-4 shadow-2xl">
            <div className="flex items-center justify-between border-b border-slate-800 pb-3">
              <div className="flex items-center gap-2 text-amber-400 font-bold text-sm">
                <AlertTriangle className="w-5 h-5" />
                <span>Confirmação e Escopo da Restauração</span>
              </div>
              <button
                onClick={() => setConfirmTarget(null)}
                className="text-slate-400 hover:text-white cursor-pointer"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            <div className="space-y-2 text-xs text-slate-300 leading-relaxed font-mono bg-[#0a0c13] p-3.5 rounded-lg border border-slate-800">
              {confirmTarget.mode === 'all' ? (
                <>
                  <div>• Operação: Reversão individual verificada de todos os ajustes ativos</div>
                  <div>• Total de registros no escopo: {restoreItems.length}</div>
                  <div>• Validação: Whitelist de subchaves + Releitura pós-escrita</div>
                  <div>• Regra de segurança: Ajustes que falharem serão preservados na lista</div>
                </>
              ) : (
                <>
                  <div>• Item: {confirmTarget.item?.actionName}</div>
                  <div>• Componente: {confirmTarget.item?.component}</div>
                  <div>• Estado Anterior: {confirmTarget.item?.previousValue}</div>
                  {confirmTarget.item?.valueKind && (
                    <div>• Tipo Original: {confirmTarget.item.valueKind}</div>
                  )}
                  {confirmTarget.item?.wasInitiallyAbsent && (
                    <div className="text-amber-300">
                      • Ação: Excluir valor (não existia antes da otimização)
                    </div>
                  )}
                  {confirmTarget.item?.checksumSha256 && (
                    <div className="text-emerald-400">
                      • Verificação SHA-256: {confirmTarget.item.checksumSha256.substring(0, 24)}...
                    </div>
                  )}
                </>
              )}
            </div>

            <div className="flex justify-end gap-3 pt-2">
              <button
                onClick={() => setConfirmTarget(null)}
                className="px-4 py-2 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 text-xs font-semibold cursor-pointer"
              >
                {t.common.cancel}
              </button>
              <button
                onClick={handleConfirmExecution}
                className="px-4 py-2 rounded-lg bg-purple-600 hover:bg-purple-500 text-white text-xs font-bold cursor-pointer flex items-center gap-1.5"
              >
                <CheckCircle2 className="w-4 h-4" />
                <span>Confirmar e Restaurar</span>
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Audit Log Table */}
      <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-4">
        <div className="flex items-center justify-between pb-2 border-b border-slate-800 text-xs">
          <span className="font-bold text-white uppercase tracking-wider">
            {t.restoreCenter.auditLogTitle}
          </span>
          <span className="text-slate-400 font-mono tabular-nums">
            {restoreItems.length} {t.restoreCenter.entriesRecorded}
          </span>
        </div>

        {restoreItems.length === 0 ? (
          <div className="py-8 text-center text-xs text-slate-500">
            {t.restoreCenter.noModifications}
          </div>
        ) : (
          <div className="divide-y divide-slate-800/80">
            {restoreItems.map((item) => (
              <div
                key={item.id}
                className="py-3.5 flex flex-col md:flex-row items-start md:items-center justify-between gap-4 text-xs font-mono"
              >
                <div className="space-y-1 max-w-xl font-sans">
                  <div className="flex items-center gap-2">
                    <span className="font-bold text-white text-sm">{item.actionName}</span>
                    <span className="text-slate-500">·</span>
                    <span className="text-purple-400 text-xs font-mono">{item.component}</span>
                    {item.valueKind && (
                      <>
                        <span className="text-slate-500">·</span>
                        <span className="text-cyan-400 text-[11px] font-mono">{item.valueKind}</span>
                      </>
                    )}
                  </div>
                  <div className="text-slate-500 text-[11px] font-mono">
                    {t.common.date}: {item.timestamp} · {t.restoreCenter.userModified}
                  </div>
                </div>

                <div className="grid grid-cols-2 gap-3 text-right">
                  <div>
                    <span className="text-[9px] text-slate-500 uppercase block font-sans">
                      {t.restoreCenter.previousValue}
                    </span>
                    <span className="text-slate-400 text-xs truncate max-w-xs block" title={item.previousValue}>
                      {item.previousValue}
                    </span>
                  </div>
                  <div>
                    <span className="text-[9px] text-purple-400 uppercase block font-sans">
                      {t.restoreCenter.appliedValue}
                    </span>
                    <span className="text-purple-300 text-xs truncate max-w-xs block font-bold" title={item.newValue}>
                      {item.newValue}
                    </span>
                  </div>
                </div>

                <button
                  onClick={() => setConfirmTarget({ mode: 'single', item })}
                  disabled={item.reversible === false}
                  title={item.reversible === false ? 'Este backup está incompleto e não pode ser restaurado pela interface.' : 'Restaurar item'}
                  className="px-3 py-1.5 rounded-lg bg-[#141829] hover:bg-[#1c223a] border border-slate-700 text-slate-300 hover:text-white text-xs font-semibold transition-colors cursor-pointer shrink-0 flex items-center gap-1.5 font-sans disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-[#141829]"
                >
                  <RotateCcw className="w-3.5 h-3.5 text-slate-400" />
                  <span>{t.restoreCenter.rollbackButton}</span>
                </button>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
};
