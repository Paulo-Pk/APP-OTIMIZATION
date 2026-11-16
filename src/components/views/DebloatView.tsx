import React, { useState, useEffect, useCallback } from 'react';
import {
  Trash2,
  CheckCircle2,
  AlertTriangle,
  Sparkles,
  ShieldCheck,
  AlertCircle,
  X,
} from 'lucide-react';
import { useI18n } from '../../i18n';
import { nativeBridge } from '../../services/nativeBridge';

export const DebloatView: React.FC = () => {
  const { t } = useI18n();

  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set(['cache-dx-shaders', 'temp-user', 'temp-prefetch']));
  const [scan, setScan] = useState<{ d3dCacheBytes: number; tempWindowsBytes: number; tempUserBytes: number; softwareDistributionBytes: number; prefetchBytes: number; deliveryOptimizationBytes: number; werQueueBytes: number; werArchiveBytes: number } | null>(null);
  const [isScanning, setIsScanning] = useState(false);

  const [isCleaning, setIsCleaning] = useState<boolean>(false);
  const [cleanedBytes, setCleanedBytes] = useState<number | null>(null);
  const [cleanWasPartial, setCleanWasPartial] = useState(false);
  const [noticeMessage, setNoticeMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isConfirmModalOpen, setIsConfirmModalOpen] = useState<boolean>(false);

  const scanNow = useCallback(async () => {
    setIsScanning(true);
    try {
      const result = await nativeBridge.scanRealDebloatDirectories();
      setScan(result ? {
        d3dCacheBytes: Number(result.d3dCacheBytes) || 0,
        tempWindowsBytes: Number(result.tempWindowsBytes) || 0,
        tempUserBytes: Number(result.tempUserBytes) || 0,
        softwareDistributionBytes: Number(result.softwareDistributionBytes) || 0,
        prefetchBytes: Number(result.prefetchBytes) || 0,
        deliveryOptimizationBytes: Number(result.deliveryOptimizationBytes) || 0,
        werQueueBytes: Number(result.werQueueBytes) || 0,
        werArchiveBytes: Number(result.werArchiveBytes) || 0,
      } : null);
      if (!result) setNoticeMessage('Conecte o agente Windows para medir o espaço real que pode ser liberado.');

    } catch {
      setScan(null);
      setErrorMessage('Não foi possível verificar as pastas de limpeza.');
    } finally { setIsScanning(false); }
  }, []);

  useEffect(() => { void scanNow(); }, [scanNow]);

  const categories = [
    { id: 'cache-dx-shaders', name: t.debloat.dxShaderTitle, description: t.debloat.dxShaderDesc, estimatedBytes: scan?.d3dCacheBytes || 0, isSafe: true },
    { id: 'temp-user', name: t.debloat.tempUserTitle, description: 'Arquivos temporários criados por aplicativos na sua conta. Arquivos em uso serão ignorados.', estimatedBytes: scan?.tempUserBytes || 0, isSafe: true },
    { id: 'temp-windows', name: t.debloat.tempWindowsTitle, description: 'Arquivos temporários do Windows. Itens bloqueados pelo sistema serão preservados.', estimatedBytes: scan?.tempWindowsBytes || 0, isSafe: true },
    { id: 'temp-prefetch', name: 'Cache Prefetch do Windows', description: 'Arquivos auxiliares que o Windows recria. A limpeza pode deixar a primeira abertura de programas mais lenta temporariamente.', estimatedBytes: scan?.prefetchBytes || 0, isSafe: true },
    { id: 'update-cache', name: t.debloat.updateCacheTitle, description: t.debloat.updateCacheDesc, estimatedBytes: scan?.softwareDistributionBytes || 0, isSafe: true },
    { id: 'cache-delivery-optimization', name: 'Cache de otimização de entrega', description: 'Arquivos temporários usados pelo Windows para distribuir atualizações. O que estiver em uso será preservado.', estimatedBytes: scan?.deliveryOptimizationBytes || 0, isSafe: true },
    { id: 'wer-report-queue', name: 'Relatórios de erro pendentes', description: 'Relatórios de falhas do Windows ainda não enviados. Apagar remove apenas os relatórios locais, não corrige a causa da falha.', estimatedBytes: scan?.werQueueBytes || 0, isSafe: true },
    { id: 'wer-report-archive', name: 'Relatórios antigos de erro', description: 'Relatórios antigos já arquivados pelo Windows. Podem ser apagados para liberar espaço, mas deixam de estar disponíveis para diagnóstico.', estimatedBytes: scan?.werArchiveBytes || 0, isSafe: true },
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

  const handleStartCleanRequest = () => {
    const isConnected = nativeBridge.getStatus().isConnected;
    if (!isConnected) {
      setNoticeMessage(t.debloat.agentRequiredNotice);
      return;
    }
    setNoticeMessage(null);
    setErrorMessage(null);
    setIsConfirmModalOpen(true);
  };

  const handleExecuteClean = async () => {
    setIsConfirmModalOpen(false);
    setIsCleaning(true);
    setErrorMessage(null);
    setNoticeMessage(null);
    setCleanedBytes(null);
    setCleanWasPartial(false);

    let totalFreed = 0;
    let deletedFiles = 0;
    let skippedLocked = 0;
    let skippedSymlinks = 0;
    let hasPartialResult = false;
    const failures: string[] = [];

    // Process every selected category even if one request fails. The bridge returns
    // structured failures for HTTP errors, so success must be checked explicitly.
    for (const id of Array.from(selectedIds)) {
      const categoryName = categories.find((category) => category.id === id)?.name || id;
      try {
        const result = await nativeBridge.purgeRealDebloatDirectory(id);
        totalFreed += result.freedBytes;
        deletedFiles += result.deletedFilesCount;
        skippedLocked += result.skippedLockedCount;
        skippedSymlinks += result.skippedSymlinksCount;

        if (!result.success || result.partialSuccess || result.errors.length > 0 ||
            result.skippedLockedCount > 0 || result.skippedSymlinksCount > 0) {
          hasPartialResult = true;
        }

        if (result.errors.length > 0) {
          failures.push(...result.errors.slice(0, 3).map((error) => `${categoryName}: ${error}`));
        } else if (!result.success) {
          failures.push(`${categoryName}: operação não concluída (${result.status || 'falha sem detalhes'}).`);
        }
      } catch (err) {
        hasPartialResult = true;
        failures.push(`${categoryName}: ${err instanceof Error ? err.message : 'falha de comunicação com o agente'}.`);
      }
    }

    setCleanedBytes(totalFreed);
    setCleanWasPartial(hasPartialResult);
    setNoticeMessage(
      hasPartialResult
        ? `Limpeza parcial: ${deletedFiles} arquivo(s) removido(s); ${formatBytes(totalFreed)} liberados; ${skippedLocked} arquivo(s) em uso preservado(s); ${skippedSymlinks} link(s) simbólico(s) preservado(s).`
        : null
    );
    if (failures.length > 0) {
      setErrorMessage(failures.slice(0, 5).join(' | '));
    }

    try {
      await scanNow();
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
            <span className="text-slate-500 block text-[10px] uppercase">{t.debloat.estimatedSpaceLabel}</span>
            <span className="text-lg font-bold text-purple-300">{formatBytes(selectedBytes)}</span>
          </div>

          <button
            onClick={handleStartCleanRequest}
            disabled={isCleaning || isScanning || !scan || selectedBytes === 0}
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

      {/* Error Message */}
      {errorMessage && (
        <div className="p-4 rounded-xl bg-[#261316] border border-rose-500/60 flex items-center justify-between text-xs animate-scale-in">
          <div className="flex items-center gap-2.5 text-rose-300">
            <AlertTriangle className="w-5 h-5 text-rose-400 shrink-0" />
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

      {/* Verified Clean Result (Only from Real Windows Service) */}
      {cleanedBytes !== null && (
        <div className={`p-4 rounded-xl bg-[#0f1626] border ${cleanWasPartial ? 'border-amber-500/50' : 'border-emerald-500/50'} flex items-center justify-between text-xs animate-scale-in`}>
          <div className="flex items-center gap-2">
            {cleanWasPartial
              ? <AlertTriangle className="w-4 h-4 text-amber-400 shrink-0" />
              : <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0" />}
            <div className={cleanWasPartial ? 'text-amber-200' : 'text-emerald-200'}>
              <span className="font-bold">{t.debloat.actualFreedLabel}: </span>
              <span className={`font-mono font-bold ${cleanWasPartial ? 'text-amber-300' : 'text-emerald-400'}`}>{formatBytes(cleanedBytes)}</span>
              {cleanWasPartial ? (
                <span className="ml-2">— resultado parcial; confira os detalhes acima</span>
              ) : cleanedBytes === 0 ? (
                <span className="text-slate-400 ml-2">({t.debloat.zeroBytesCleaned})</span>
              ) : (
                <span className="ml-2">— {t.debloat.cleanSuccess}</span>
              )}
            </div>
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
            {isScanning ? 'Verificando pastas...' : scan ? `Encontrado: ${formatBytes(categories.reduce((acc, c) => acc + c.estimatedBytes, 0))}` : 'Faça uma verificação para ver o espaço real'}
          </span>
          <button onClick={() => void scanNow()} disabled={isScanning || isCleaning} className="px-3 py-1.5 rounded-md bg-[#171b2a] border border-slate-700 text-slate-200 disabled:opacity-50">{isScanning ? 'Verificando...' : 'Verificar novamente'}</button>
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
                    onClick={(event) => event.stopPropagation()}
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
                  {scan ? formatBytes(c.estimatedBytes) : 'N/D'}
                </div>
              </div>
            );
          })}
        </div>
      </div>

      {/* Explicit Confirmation Modal */}
      {isConfirmModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/80 backdrop-blur-sm animate-fade-in">
          <div className="w-full max-w-md bg-[#0f121b] border border-purple-500/40 rounded-xl p-6 shadow-2xl space-y-4 animate-scale-in">
            <div className="flex items-start justify-between gap-3">
              <div className="flex items-center gap-2 text-purple-400">
                <ShieldCheck className="w-5 h-5 text-purple-400" />
                <h3 className="font-bold text-base text-white">{t.debloat.confirmModalTitle}</h3>
              </div>
              <button
                onClick={() => setIsConfirmModalOpen(false)}
                className="text-slate-400 hover:text-white cursor-pointer"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            <p className="text-xs text-slate-300 leading-relaxed">
              {t.debloat.confirmModalSubtitle}
            </p>

            <div className="p-3 rounded-lg bg-[#141829] border border-slate-800 text-xs space-y-2">
              <span className="text-slate-400 block font-semibold">Pastas que serão verificadas e purgadas:</span>
              <ul className="list-disc list-inside text-slate-300 space-y-1 text-[11px] font-mono">
                {Array.from(selectedIds).map((id) => {
                  const cat = categories.find((c) => c.id === id);
                  return <li key={id}>{cat?.name || id}</li>;
                })}
              </ul>
            </div>

            <div className="p-3 rounded-lg bg-emerald-950/30 border border-emerald-500/30 text-emerald-300 text-xs flex items-start gap-2">
              <ShieldCheck className="w-4 h-4 shrink-0 mt-0.5" />
              <span>{t.debloat.confirmModalPersonalDataSafe}</span>
            </div>

            <div className="flex justify-end items-center gap-3 pt-2">
              <button
                onClick={() => setIsConfirmModalOpen(false)}
                className="px-4 py-2 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 text-xs font-semibold cursor-pointer transition-colors"
              >
                {t.debloat.confirmModalCancel}
              </button>
              <button
                onClick={handleExecuteClean}
                className="px-4 py-2 rounded-lg bg-purple-600 hover:bg-purple-500 text-white text-xs font-bold cursor-pointer transition-colors shadow-lg shadow-purple-600/30 border border-purple-400/40"
              >
                {t.debloat.confirmModalConfirm}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
