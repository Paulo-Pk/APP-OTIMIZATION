import React, { useState } from 'react';
import {
  Gamepad2,
  SlidersHorizontal,
  Plus,
  ShieldCheck,
} from 'lucide-react';
import { GameProfile } from '../../types';
import { useI18n } from '../../i18n';

interface GameCenterViewProps {
  games: GameProfile[];
  onOpenProfileModal: (game: GameProfile) => void;
  onAddCustomGame: (gameId?: string) => void;
  onLaunchGame: (game: GameProfile) => void;
  onStopGame: (sessionId: string) => void;
  onToggleGameBoost: (sessionId: string, enabled: boolean) => void;
  gameSessions: Array<{ sessionId: string; title: string; executablePath: string; processId: number; isRunning: boolean; boostEnabled: boolean }>;
}

export const GameCenterView: React.FC<GameCenterViewProps> = ({
  games,
  onOpenProfileModal,
  onAddCustomGame,
  onLaunchGame,
  onStopGame,
  onToggleGameBoost,
  gameSessions,
}) => {
  const { t } = useI18n();
  const [activePlatformFilter, setActivePlatformFilter] = useState<string>('all');

  // Storefront labels vary between discovery providers (for example, "Epic"
  // and "Epic Games"). Normalize aliases before filtering to avoid split lists.
  const normalizePlatform = (platform: string) => {
    const value = platform.trim().toLowerCase();
    if (value === 'epic' || value === 'epic games' || value === 'epicgames') return 'epic';
    if (value === 'ea' || value === 'ea app' || value === 'origin') return 'ea app';
    if (value === 'ubisoft' || value === 'ubisoft connect' || value === 'uplay') return 'ubisoft connect';
    if (value === 'xbox' || value === 'microsoft store' || value === 'pc game pass') return 'xbox';
    if (value === 'manual' || value === 'custom') return 'manual';
    return value;
  };

  const platformFilters = [
    { id: 'all', label: t.gameCenter.allGames },
    { id: 'steam', label: 'Steam' },
    { id: 'epic', label: 'Epic Games' },
    { id: 'xbox', label: 'Xbox' },
    { id: 'gog', label: 'GOG' },
    { id: 'ubisoft connect', label: 'Ubisoft Connect' },
    { id: 'ea app', label: 'EA app' },
    { id: 'manual', label: 'Manual' },
  ];

  const filteredGames = games.filter((game) =>
    activePlatformFilter === 'all' || normalizePlatform(game.platform) === activePlatformFilter
  );


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
            onClick={() => onAddCustomGame()}
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
          {platformFilters.map((platform) => {
            const isActive = activePlatformFilter === platform.id;
            return (
              <button
                key={platform}
                onClick={() => setActivePlatformFilter(platform.id)}
                className={`px-3 py-1.5 rounded-lg text-xs font-semibold transition-all cursor-pointer ${
                  isActive
                    ? 'bg-purple-600 text-white shadow-md shadow-purple-600/30 border border-purple-400/40'
                    : 'bg-[#151926] text-slate-400 hover:text-slate-200 hover:bg-[#1a2030] border border-slate-800'
                }`}
              >
                {platform.label}
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

                {/* Native process session controls */}
                {(() => {
                  const runningSession = gameSessions.find((session) =>
                    session.isRunning && session.executablePath.toLowerCase() === game.executablePath.toLowerCase()
                  );
                  return (
                    <div className="space-y-2 pt-2 border-t border-slate-800/60">
                      <div className="flex items-center gap-2">
                        <button
                          onClick={() => onOpenProfileModal(game)}
                          className="p-2 rounded-lg bg-[#141724] hover:bg-[#1a2032] border border-slate-700 text-slate-300 hover:text-white transition-colors cursor-pointer"
                          title={t.gameCenter.editProfile}
                        >
                          <SlidersHorizontal className="w-4 h-4" />
                        </button>
                        {runningSession ? (
                          <button
                            onClick={() => onStopGame(runningSession.sessionId)}
                            className="flex-1 py-2 px-3 rounded-lg bg-rose-950/60 hover:bg-rose-900/70 text-rose-200 text-xs font-bold border border-rose-800"
                          >
                            Solicitar fechamento · PID {runningSession.processId}
                          </button>
                        ) : (
                          <button
                            onClick={() => onLaunchGame(game)}
                            disabled={!game.approvalId}
                            title={!game.approvalId ? "Selecione novamente o executável para autorizar a inicialização nesta sessão." : "Iniciar o executável aprovado pelo seletor nativo do Windows."}
                            className="flex-1 py-2 px-3 rounded-lg bg-purple-700 hover:bg-purple-600 disabled:bg-slate-800 disabled:text-slate-500 text-white text-xs font-bold border border-purple-500/40 disabled:border-slate-700 disabled:cursor-not-allowed"
                          >
                            {game.approvalId ? "Iniciar jogo" : "Autorizar executável"}
                          </button>
                        )}
                      </div>
                      {runningSession && (
                        <button
                          onClick={() => onToggleGameBoost(runningSession.sessionId, !runningSession.boostEnabled)}
                          className="w-full py-2 px-3 rounded-lg bg-[#141724] hover:bg-[#1a2032] border border-slate-700 text-xs font-semibold text-slate-200"
                        >
                          {runningSession.boostEnabled ? "Desativar Game Boost · restaurar prioridade" : "Ativar Game Boost · Acima do Normal"}
                        </button>
                      )}
                      {!runningSession && !game.approvalId && (
                        <button
                          onClick={() => onAddCustomGame(game.id)}
                          className="w-full text-[10px] text-purple-300 hover:text-purple-200 underline"
                        >
                          Selecionar executável novamente para autorizar
                        </button>
                      )}
                    </div>
                  );
                })()}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
};
