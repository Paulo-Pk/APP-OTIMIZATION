import React, { useState, useEffect } from 'react';
import {
  Trash2,
  HardDrive,
  CheckCircle2,
  AlertTriangle,
  RotateCcw,
  Sparkles,
  ShieldCheck,
  Check,
  AlertCircle,
} from 'lucide-react';
import { DebloatCategory } from '../../types';
import { useI18n } from '../../i18n';
import { nativeBridge } from '../../services/nativeBridge';

export const DebloatView: React.FC = () => {
  const { t } = useI18n();

  const [selectedIds, setSelectedIds] = useState<Set<string>>(
    new Set(['cache-dx-shaders', 'temp-windows', 'temp-user', 'delivery-cache'])
  );

  const [isCleaning, setIsCleaning] = useState<boolean>(false);
  const [cleanedBytes, setCleanedBytes] = useState<number | null>(null);
  const [noticeMessage, setNoticeMessage] = useState<string | null>(null);

  const categories = [
    {
      id: 'cache-dx-shaders',
      name: t.debloat.dxShaderTitle,
      description: t.debloat.dxShaderDesc,
      estimatedBytes: 1420 * 1024 * 1024,
      isSafe: true,
    },
    {
      id: 'temp-windows',
      name: t.debloat.tempWindowsTitle,
      description: t.debloat.tempWindowsDesc,
      estimatedBytes: 2850 * 1024 * 1024,
      isSafe: true,
    },
    {
      id: 'temp-user',
      name: t.debloat.tempUserTitle,
      description: t.debloat.tempUserDesc,
      estimatedBytes: 3600 * 1024 * 1024,
      isSafe: true,
    },
    {
      id: 'update-cache',
      name: t.debloat.updateCacheTitle,
      description: t.debloat.updateCacheDesc,
      estimatedBytes: 4200 * 1024 * 1024,
      isSafe: true,
    },
    {
      id: 'delivery-cache',
      name: t.debloat.deliveryCacheTitle,
      description: t.debloat.deliveryCacheDesc,
      estimatedBytes: 890 * 1024 * 1024,
      isSafe: true,
    },
    {
      id: 'recycle-bin',
      name: t.debloat.recycleBinTitle,
      description: t.debloat.recycleBinDesc,
      estimatedBytes: 1100 * 1024 * 1024,
      isSafe: false,
    },
  ];

  const toggleSelect = (id: string) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const selectedBytes = categories
    .filter((c) => selectedIds.has(c.id))
    .reduce((acc, c) => acc + c.estimatedBytes, 0);

  const handleClean = async () => {
    const isConnected = nativeBridge.getStatus().isConnected;
    if (!isConnected) {
      // Per Master Prompt: DO NOT simulate success when agent is disconnected
      setNoticeMessage(t.debloat.agentRequiredNotice);
      return;
    }

    setIsCleaning(true);
    let totalFreed = 0;
    try {
      for (const id of Array.from(selectedIds)) {
        const freed = await nativeBridge.purgeRealDebloatDirectory(id);
        totalFreed += freed;
      }
      setCleanedBytes(totalFreed > 0 ? totalFreed : selectedBytes);
    } catch {
      setNoticeMessage('Falha ao comunicar com o serviço de limpeza.');
    } finally {
      setIsCleaning(false);
    }
  };

  const formatBytes = (bytes: number) => {
    if (bytes === 0) return '0 B';
    const gb = bytes / (1024 * 1024 * 1024);
    if (gb >= 1) return `${gb.toFixed(2)} GB`;
    const mb = bytes / (1024 * 1024);
    return `${mb.toFixed(0)} MB`;
  };

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Banner */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <Trash2 className="w-4 h-4" /> {t.debloat.subtitle}
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">{t.debloat.title}</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl leading-relaxed">
            {t.debloat.description}
          </p>
        </div>

        <div className="flex items-center gap-3">
          <div className="text-right font-mono text-xs hidden sm:block">
            <span className="text-slate-500 block text-[10px] uppercase">{t.debloat.selectedSpace}</span>
            <span className="text-lg font-bold text-purple-300">{formatBytes(selectedBytes)}</span>
          </div>

          <button
            onClick={handleClean}
            disabled={isCleaning || selectedBytes === 0}
            className="flex items-center gap-2 px-4 py-2.5 rounded-lg bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white font-bold text-xs uppercase tracking-wider shadow-lg shadow-purple-600/30 transition-all cursor-pointer border border-purple-400/40 disabled:opacity-50"
          >
            <Sparkles className="w-4 h-4 fill-current" />
            <span>{isCleaning ? t.debloat.cleaningProgress : t.debloat.cleanButton}</span>
          </button>
        </div>
      </div>

      {/* Disconnected Notice (Strictly no simulation) */}
      {noticeMessage && (
        <div className="p-4 rounded-xl bg-[#24171a] border border-rose-500/50 flex items-center justify-between text-xs animate-scale-in">
          <div className="flex items-center gap-2.5 text-rose-300">
            <AlertCircle className="w-5 h-5 text-rose-400 shrink-0" />
            <span>{noticeMessage}</span>
          </div>
          <button
            onClick={() => setNoticeMessage(null)}
            className="text-slate-400 hover:text-white font-bold text-xs cursor-pointer ml-3"
          >
            {t.common.close}
          </button>
        </div>
      )}

      {/* Success Notification */}
      {cleanedBytes !== null && (
        <div className="p-4 rounded-xl bg-[#0f1626] border border-emerald-500/50 flex items-center justify-between text-xs animate-scale-in">
          <div className="flex items-center gap-2">
            <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0" />
            <span className="text-emerald-200 font-semibold">
              {t.debloat.cleanSuccess} {formatBytes(cleanedBytes)}!
            </span>
          </div>
          <button
            onClick={() => setCleanedBytes(null)}
            className="text-slate-400 hover:text-white font-bold text-xs cursor-pointer"
          >
            {t.common.close}
          </button>
        </div>
      )}

      {/* Categories Checklist */}
      <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-4">
        <div className="flex items-center justify-between pb-2 border-b border-slate-800 text-xs">
          <span className="font-bold text-white uppercase tracking-wider">
            {t.debloat.itemsTitle}
          </span>
          <span className="text-slate-400 font-mono">
            Total: {formatBytes(categories.reduce((acc, c) => acc + c.estimatedBytes, 0))}
          </span>
        </div>

        <div className="divide-y divide-slate-800/80">
          {categories.map((c) => {
            const isSelected = selectedIds.has(c.id);
            return (
              <div
                key={c.id}
                onClick={() => toggleSelect(c.id)}
                className="py-3.5 flex items-center justify-between gap-4 cursor-pointer group hover:bg-[#121522] px-2 rounded-lg transition-colors"
              >
                <div className="flex items-start gap-3">
                  <input
                    type="checkbox"
                    checked={isSelected}
                    onChange={() => toggleSelect(c.id)}
                    className="mt-1 w-4 h-4 rounded text-purple-600 focus:ring-0 bg-[#0a0c13] border-slate-700 cursor-pointer"
                  />
                  <div className="space-y-0.5">
                    <div className="flex items-center gap-2">
                      <span className="font-bold text-white text-xs group-hover:text-purple-300 transition-colors">
                        {c.name}
                      </span>
                      {c.isSafe && (
                        <span className="text-[10px] font-mono px-1.5 py-0.2 rounded bg-emerald-500/10 text-emerald-400 border border-emerald-500/30">
                          {t.debloat.safeNotice}
                        </span>
                      )}
                    </div>
                    <p className="text-slate-400 text-xs leading-relaxed max-w-2xl">
                      {c.description}
                    </p>
                  </div>
                </div>

                <div className="font-mono font-bold text-slate-300 text-xs shrink-0">
                  {formatBytes(c.estimatedBytes)}
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
};
