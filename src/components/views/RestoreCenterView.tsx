import React, { useState } from 'react';
import {
  History,
  RotateCcw,
  CheckCircle2,
  AlertTriangle,
  Shield,
  FileCheck,
  Plus,
} from 'lucide-react';
import { RestoreItem } from '../../types';

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
  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Banner */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <History className="w-4 h-4" /> Histórico & Reversibilidade Granular
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">RESTORE CENTER</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl">
            Rastreamento de cada chave de registro ou política do Windows alterada. Reverta alterações individuais ou em lote com segurança.
          </p>
        </div>

        <div className="flex items-center gap-3">
          <button
            onClick={onCreateSnapshot}
            className="flex items-center gap-1.5 px-3 py-2 rounded-lg bg-[#141724] hover:bg-[#1b2033] border border-slate-700 text-slate-300 hover:text-white text-xs font-semibold transition-colors cursor-pointer"
          >
            <Plus className="w-4 h-4 text-purple-400" />
            <span>Criar Snapshot Local</span>
          </button>

          <button
            onClick={onRestoreAllPoints}
            className="flex items-center gap-2 px-4 py-2 rounded-lg bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white font-bold text-xs uppercase tracking-wider shadow-lg shadow-purple-600/30 transition-all cursor-pointer border border-purple-400/40"
          >
            <RotateCcw className="w-4 h-4" />
            <span>Desfazer Todas as Modificações</span>
          </button>
        </div>
      </div>

      {/* Safety Notice per Master Prompt Rule #18 */}
      <div className="p-4 rounded-xl bg-[#11131c] border border-slate-800 flex items-start gap-3 text-xs text-slate-400 leading-relaxed">
        <Shield className="w-4 h-4 text-amber-400 shrink-0 mt-0.5" />
        <div>
          <span className="font-bold text-slate-200">Aviso Técnico de Segurança: </span>
          O backup interno de configurações do PC Control Ultimate preserva valores de registros modificados pelo software, mas <strong>não substitui um Ponto de Restauração nativo do Windows (VSS/System Restore)</strong>. Recomendamos manter a proteção de sistema ativada na partição C:.
        </div>
      </div>

      {/* Audit Log Table */}
      <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-4">
        <div className="flex items-center justify-between pb-2 border-b border-slate-800 text-xs">
          <span className="font-bold text-white uppercase tracking-wider">
            Registro de Auditoria de Modificações (SQLite Local)
          </span>
          <span className="text-slate-400 font-mono">
            {restoreItems.length} Entradas Gravadas
          </span>
        </div>

        {restoreItems.length === 0 ? (
          <div className="py-8 text-center text-xs text-slate-500">
            Nenhuma modificação foi aplicada ainda. O histórico está limpo.
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
                  </div>
                  <div className="text-slate-500 text-[11px] font-mono">
                    Data: {item.timestamp} · Modificado pelo Usuário
                  </div>
                </div>

                <div className="grid grid-cols-2 gap-3 text-right">
                  <div>
                    <span className="text-[9px] text-slate-500 uppercase block font-sans">
                      Valor Anterior
                    </span>
                    <span className="text-slate-400 text-xs truncate max-w-xs block" title={item.previousValue}>
                      {item.previousValue}
                    </span>
                  </div>
                  <div>
                    <span className="text-[9px] text-purple-400 uppercase block font-sans">
                      Valor Aplicado
                    </span>
                    <span className="text-purple-300 text-xs truncate max-w-xs block font-bold" title={item.newValue}>
                      {item.newValue}
                    </span>
                  </div>
                </div>

                <button
                  onClick={() => onRollbackItem(item.id)}
                  className="px-3 py-1.5 rounded-lg bg-[#141829] hover:bg-[#1c223a] border border-slate-700 text-slate-300 hover:text-white text-xs font-semibold transition-colors cursor-pointer shrink-0 flex items-center gap-1.5 font-sans"
                >
                  <RotateCcw className="w-3.5 h-3.5 text-slate-400" />
                  <span>Reverter</span>
                </button>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
};
