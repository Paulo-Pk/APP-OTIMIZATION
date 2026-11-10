import React, { useState } from 'react';
import {
  Trash2,
  HardDrive,
  CheckCircle2,
  AlertTriangle,
  RotateCcw,
  Sparkles,
  ShieldCheck,
  Check,
} from 'lucide-react';
import { DebloatCategory } from '../../types';

export const DebloatView: React.FC = () => {
  const [categories, setCategories] = useState<DebloatCategory[]>([
    {
      id: 'cache-dx-shaders',
      name: 'Cache de Shaders DirectX (D3DSCache)',
      description: 'Shaders pré-compilados corrompidos ou obsoletos de drivers de GPU antigos que causam stuttering.',
      estimatedBytes: 1420 * 1024 * 1024, // 1.42 GB
      isSafe: true,
      selected: true,
    },
    {
      id: 'temp-windows',
      name: 'Arquivos Temporários do Sistema (Windows\\Temp)',
      description: 'Arquivos residuais deixados por instaladores e processos encerrados.',
      estimatedBytes: 2850 * 1024 * 1024, // 2.85 GB
      isSafe: true,
      selected: true,
    },
    {
      id: 'temp-user',
      name: 'Arquivos Temporários do Usuário (%LocalAppData%\\Temp)',
      description: 'Logs efêmeros de navegadores e dumps de travamento descartáveis.',
      estimatedBytes: 3600 * 1024 * 1024, // 3.6 GB
      isSafe: true,
      selected: true,
    },
    {
      id: 'update-cache',
      name: 'Cache de Distribuição de Software do Windows Update',
      description: 'Instaladores de atualizações cumulativas do Windows já aplicadas com sucesso.',
      estimatedBytes: 4200 * 1024 * 1024, // 4.2 GB
      isSafe: true,
      selected: false,
    },
    {
      id: 'delivery-cache',
      name: 'Cache de Otimização de Entrega P2P',
      description: 'Pacotes baixados para distribuição em rede local.',
      estimatedBytes: 890 * 1024 * 1024, // 890 MB
      isSafe: true,
      selected: true,
    },
    {
      id: 'recycle-bin',
      name: 'Lixeira do Windows',
      description: 'Arquivos descartados pelo usuário nos volumes C: e D:.',
      estimatedBytes: 1100 * 1024 * 1024, // 1.1 GB
      isSafe: false,
      selected: false,
    },
  ]);

  const [isCleaning, setIsCleaning] = useState<boolean>(false);
  const [cleanedBytes, setCleanedBytes] = useState<number | null>(null);

  const toggleSelect = (id: string) => {
    setCategories((prev) =>
      prev.map((c) => (c.id === id ? { ...c, selected: !c.selected } : c))
    );
  };

  const selectedBytes = categories
    .filter((c) => c.selected)
    .reduce((acc, c) => acc + c.estimatedBytes, 0);

  const handleClean = () => {
    setIsCleaning(true);
    setTimeout(() => {
      setCleanedBytes(selectedBytes);
      setCategories((prev) =>
        prev.map((c) => (c.selected ? { ...c, estimatedBytes: 0, selected: false } : c))
      );
      setIsCleaning(false);
    }, 1800);
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
            <Trash2 className="w-4 h-4" /> Limpeza Segura de Resíduos & Cache de Shaders
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">DEBLOAT & CLEANER</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl">
            Exclui exclusivamente caches efêmeros seguros e shaders corrompidos. Nenhum documento pessoal ou savegame é tocado.
          </p>
        </div>

        <div className="flex items-center gap-3">
          <div className="text-right font-mono text-xs hidden sm:block">
            <span className="text-slate-500 block text-[10px] uppercase">Espaço Selecionado</span>
            <span className="text-lg font-bold text-purple-300">{formatBytes(selectedBytes)}</span>
          </div>

          <button
            onClick={handleClean}
            disabled={isCleaning || selectedBytes === 0}
            className="flex items-center gap-2 px-4 py-2.5 rounded-lg bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white font-bold text-xs uppercase tracking-wider shadow-lg shadow-purple-600/30 transition-all cursor-pointer border border-purple-400/40 disabled:opacity-50"
          >
            <Sparkles className="w-4 h-4 fill-current" />
            <span>{isCleaning ? 'Limpando Caches...' : 'Executar Limpeza Segura'}</span>
          </button>
        </div>
      </div>

      {/* Success Notification */}
      {cleanedBytes !== null && (
        <div className="p-4 rounded-xl bg-[#0f1626] border border-emerald-500/50 flex items-center justify-between text-xs animate-scale-in">
          <div className="flex items-center gap-2">
            <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0" />
            <span className="text-emerald-200 font-semibold">
              Limpeza concluída com sucesso! Foram recuperados {formatBytes(cleanedBytes)} de armazenamento NVMe.
            </span>
          </div>
          <button
            onClick={() => setCleanedBytes(null)}
            className="text-slate-400 hover:text-white font-bold text-xs cursor-pointer"
          >
            Fechar
          </button>
        </div>
      )}

      {/* Categories Checklist */}
      <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-4">
        <div className="flex items-center justify-between pb-2 border-b border-slate-800 text-xs">
          <span className="font-bold text-white uppercase tracking-wider">
            Itens Identificados para Remoção
          </span>
          <span className="text-slate-400 font-mono">
            Total Estimado: {formatBytes(categories.reduce((acc, c) => acc + c.estimatedBytes, 0))}
          </span>
        </div>

        <div className="divide-y divide-slate-800/80">
          {categories.map((c) => (
            <div
              key={c.id}
              onClick={() => toggleSelect(c.id)}
              className="py-3.5 flex items-center justify-between gap-4 cursor-pointer group hover:bg-[#121522] px-2 rounded-lg transition-colors"
            >
              <div className="flex items-start gap-3">
                <input
                  type="checkbox"
                  checked={c.selected}
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
                        100% Seguro
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
          ))}
        </div>
      </div>
    </div>
  );
};
