import React, { useState } from 'react';
import {
  Gamepad2,
  Zap,
  SlidersHorizontal,
  Plus,
  ShieldCheck,
} from 'lucide-react';
import { GameProfile } from '../../types';
import { useI18n } from '../../i18n';

interface GameCenterViewProps {
  games: GameProfile[];
  onOpenProfileModal: (game: GameProfile) => void;
  onAddCustomGame: () => void;
}

export const GameCenterView: React.FC<GameCenterViewProps> = ({
  games,
  onOpenProfileModal,
  onAddCustomGame,
}) => {
  const { t } = useI18n();
  const [activePlatformFilter, setActivePlatformFilter] = useState<string>('all');

  const filteredGames = games.filter((g) => {
    if (activePlatformFilter === 'all') return true;
    return g.platform.toLowerCase() === activePlatformFilter.toLowerCase();
  });


  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Banner */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <Gamepad2 className="w-4 h-4" /> {t.gameCenter.subtitle}
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">{t.gameCenter.title}</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl leading-relaxed">
            {t.gameCenter.description}
          </p>
        </div>

        <div className="flex items-center gap-3">
          <button
            onClick={onAddCustomGame}
            className="flex items-center gap-1.5 px-3 py-2 rounded-lg bg-[#141724] hover:bg-[#1b2033] border border-slate-700 text-slate-300 hover:text-white text-xs font-semibold transition-colors cursor-pointer"
          >
            <Plus className="w-4 h-4 text-purple-400" />
            <span>{t.gameCenter.addCustomExe}</span>
          </button>
        </div>
      </div>

      {/* Platform Filter Tabs */}
      <div className="flex items-center justify-between gap-4 p-3 rounded-xl bg-[#0f121b] border border-slate-800">
        <div className="flex items-center gap-2">
          <span className="text-xs text-slate-400 font-semibold mr-1">{t.gameCenter.platformLabel}</span>
          {['all', 'Steam', 'Epic', 'Epic Games', 'Xbox', 'GOG', 'Ubisoft Connect', 'EA app', 'Manual'].map((platform) => {
            const isActive = activePlatformFilter === platform;
            return (
              <button
                key={platform}
                onClick={() => setActivePlatformFilter(platform)}
                className={`px-3 py-1.5 rounded-lg text-xs font-semibold transition-all cursor-pointer ${
                  isActive
                    ? 'bg-purple-600 text-white shadow-md shadow-purple-600/30 border border-purple-400/40'
                    : 'bg-[#151926] text-slate-400 hover:text-slate-200 hover:bg-[#1a2030] border border-slate-800'
                }`}
              >
                {platform === 'all' ? t.gameCenter.allGames : platform}
              </button>
            );
          })}
        </div>

        <div className="flex items-center gap-2 text-xs text-slate-400">
          <ShieldCheck className="w-4 h-4 text-emerald-400" />
          <span className="hidden sm:inline">{t.gameCenter.antiCheatNotice}</span>
        </div>
      </div>

      {/* Games Visual Grid */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
        {filteredGames.length === 0 && (
          <div className="col-span-full p-8 rounded-xl bg-[#0f121b] border border-slate-800 text-center space-y-2">
            <Gamepad2 className="w-9 h-9 text-purple-400 mx-auto" />
            <p className="text-sm font-semibold text-white">Nenhum jogo instalado foi encontrado ainda</p>
            <p className="text-xs text-slate-400">Conecte o agente Windows. O PC Control consulta as bibliotecas suportadas sem varrer o disco inteiro.</p>
          </div>
        )}
        {filteredGames.map((game) => {
          return (
            <div
              key={game.id}
              className="rounded-xl overflow-hidden border transition-all flex flex-col justify-between bg-[#0f121b] border-slate-800 hover:border-purple-600/40"
            >
              {/* Cover Image */}
              <div className="relative h-40 bg-slate-900 overflow-hidden group">
                {game.coverImage ? (
                  <img src={game.coverImage} alt={game.title} className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-500" />
                ) : (
                  <div className="w-full h-full flex items-center justify-center bg-gradient-to-br from-[#21163b] via-[#111827] to-[#0b1020]">
                    <Gamepad2 className="w-14 h-14 text-purple-300/80" />
                  </div>
                )}
                <div className="absolute inset-0 bg-gradient-to-t from-[#0f121b] via-transparent to-black/60" />

                <div className="absolute top-2.5 left-2.5 px-2 py-0.5 rounded bg-black/70 backdrop-blur-md text-[10px] font-bold text-slate-300 border border-slate-700/60 uppercase">
                  {game.platform}
                </div>

                {game.boostEnabled && (
                  <div className="absolute top-2.5 right-2.5 px-2 py-0.5 rounded bg-purple-600/80 backdrop-blur-md text-[10px] font-bold text-white border border-purple-400/40 flex items-center gap-1">
                    <Zap className="w-3 h-3 fill-current" /> {t.gameCenter.boostActive}
                  </div>
                )}

              </div>

              {/* Body */}
              <div className="p-4 space-y-3 flex-1 flex flex-col justify-between">
                <div>
                  <h3 className="font-bold text-sm text-white truncate" title={game.title}>
                    {game.title}
                  </h3>
                  <div className="text-[11px] font-mono text-slate-500 truncate mt-0.5" title={game.executablePath}>
                    {game.executablePath.split('\\').pop()}
                  </div>
                </div>

                {/* Per-Game Config Badges */}
                <div className="grid grid-cols-2 gap-2 text-xs pt-1 border-t border-slate-800/80">
                  <div className="p-2 rounded bg-[#0a0c13] border border-slate-900">
                    <span className="text-slate-500 block text-[9px] uppercase">{t.gameCenter.priorityCpu}</span>
                    <span className="font-semibold text-purple-300">{game.processPriority}</span>
                  </div>
                  <div className="p-2 rounded bg-[#0a0c13] border border-slate-900">
                    <span className="text-slate-500 block text-[9px] uppercase">{t.gameCenter.affinity}</span>
                    <span className="font-semibold text-cyan-300">{game.cpuAffinity}</span>
                  </div>
                </div>

                {/* FPS History Metric */}
                {game.averageFps && (
                  <div className="flex items-center justify-between text-xs px-2.5 py-1.5 rounded-lg bg-[#0a0c13] border border-slate-800/60 font-mono">
                    <span className="text-slate-400 text-[11px]">{t.gameCenter.averageFps}</span>
                    <span className="font-bold text-emerald-400">{game.averageFps} FPS</span>
                  </div>
                )}

                {/* Actions: Iniciar / Perfil */}
                <div className="flex items-center gap-2 pt-2 border-t border-slate-800/60">
                  <button
                    onClick={() => onOpenProfileModal(game)}
                    className="p-2 rounded-lg bg-[#141724] hover:bg-[#1a2032] border border-slate-700 text-slate-300 hover:text-white transition-colors cursor-pointer"
                    title={t.gameCenter.editProfile}
                  >
                    <SlidersHorizontal className="w-4 h-4" />
                  </button>

                  <button
                    type="button"
                    disabled
                    title="A inicialização nativa de jogos ainda não está disponível."
                    className="flex-1 py-2 px-3 rounded-lg bg-slate-800 text-slate-500 text-xs font-bold uppercase tracking-wider cursor-not-allowed border border-slate-700"
                  >
                    Inicialização indisponível
                  </button>
                </div>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
};
