import React, { useState } from 'react';
import {
  Gamepad2,
  X,
  Check,
  Cpu,
  Flame,
  Zap,
  Activity,
} from 'lucide-react';
import { GameProfile } from '../../types';

interface GameProfileModalProps {
  game: GameProfile | null;
  isOpen: boolean;
  onClose: () => void;
  onSave: (updated: GameProfile) => void;
}

export const GameProfileModal: React.FC<GameProfileModalProps> = ({
  game,
  isOpen,
  onClose,
  onSave,
}) => {
  if (!isOpen || !game) return null;

  const [priority, setPriority] = useState(game.processPriority);
  const [affinity, setAffinity] = useState(game.cpuAffinity);
  const [powerProfile, setPowerProfile] = useState(game.powerProfile);
  const [monitorFps, setMonitorFps] = useState(game.monitorFps);
  const [boostEnabled, setBoostEnabled] = useState(game.boostEnabled);

  const handleSave = () => {
    onSave({
      ...game,
      processPriority: priority,
      cpuAffinity: affinity,
      powerProfile,
      monitorFps,
      boostEnabled,
    });
    onClose();
  };

  return (
    <div className="fixed inset-0 z-50 bg-black/80 backdrop-blur-xs flex items-center justify-center p-4">
      <div className="w-full max-w-lg bg-[#0f121b] border border-purple-500/50 rounded-xl p-6 space-y-5 shadow-2xl animate-scale-in">
        {/* Header */}
        <div className="flex items-center justify-between pb-3 border-b border-slate-800">
          <div className="flex items-center gap-2.5">
            <Gamepad2 className="w-5 h-5 text-purple-400" />
            <div>
              <h2 className="text-sm font-black text-white uppercase tracking-wider">
                Perfil Gamer Individual
              </h2>
              <span className="text-xs text-purple-300 font-semibold">{game.title}</span>
            </div>
          </div>

          <button
            onClick={onClose}
            className="text-slate-400 hover:text-white font-bold text-lg cursor-pointer"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Form Options */}
        <div className="space-y-4 text-xs">
          {/* Process Priority */}
          <div className="space-y-1.5">
            <label className="text-slate-300 font-bold block uppercase text-[11px]">
              Prioridade do Processo no Windows
            </label>
            <select
              value={priority}
              onChange={(e) => setPriority(e.target.value as any)}
              className="w-full bg-[#0a0c13] border border-slate-800 rounded-lg p-2.5 text-slate-200 focus:outline-none focus:border-purple-500 cursor-pointer"
            >
              <option value="Normal">Normal (Padrão)</option>
              <option value="Acima do Normal">Acima do Normal</option>
              <option value="Alta">Alta (Recomendado para Jogos Competitivos)</option>
              <option value="Tempo Real">Tempo Real (Win32 Exclusivo)</option>
            </select>
          </div>

          {/* CPU Affinity */}
          <div className="space-y-1.5">
            <label className="text-slate-300 font-bold block uppercase text-[11px]">
              Afinidade de Núcleos (CPU Affinity)
            </label>
            <select
              value={affinity}
              onChange={(e) => setAffinity(e.target.value as any)}
              className="w-full bg-[#0a0c13] border border-slate-800 rounded-lg p-2.5 text-slate-200 focus:outline-none focus:border-purple-500 cursor-pointer"
            >
              <option value="Todos">Todos os Núcleos e Threads</option>
              <option value="P-Cores Apenas">P-Cores Apenas (Isola E-Cores / Híbridos)</option>
              <option value="Sem E-Cores">Sem E-Cores (Desativa threads de segundo plano)</option>
            </select>
          </div>

          {/* Power Plan */}
          <div className="space-y-1.5">
            <label className="text-slate-300 font-bold block uppercase text-[11px]">
              Esquema de Energia Durante a Partida
            </label>
            <select
              value={powerProfile}
              onChange={(e) => setPowerProfile(e.target.value as any)}
              className="w-full bg-[#0a0c13] border border-slate-800 rounded-lg p-2.5 text-slate-200 focus:outline-none focus:border-purple-500 cursor-pointer"
            >
              <option value="Desempenho Máximo PC Control">Desempenho Máximo PC Control (Trava Clocks)</option>
              <option value="Alto Desempenho">Alto Desempenho Windows</option>
              <option value="Equilibrado">Equilibrado (Balanceado)</option>
            </select>
          </div>

          {/* Toggles */}
          <div className="space-y-2 pt-2 border-t border-slate-800/80">
            <label className="flex items-center gap-2 cursor-pointer text-slate-300">
              <input
                type="checkbox"
                checked={monitorFps}
                onChange={(e) => setMonitorFps(e.target.checked)}
                className="w-4 h-4 rounded text-purple-600 bg-[#0a0c13] border-slate-700"
              />
              <span>Ativar captura automática de telemetria PresentMon ao iniciar</span>
            </label>

            <label className="flex items-center gap-2 cursor-pointer text-slate-300">
              <input
                type="checkbox"
                checked={boostEnabled}
                onChange={(e) => setBoostEnabled(e.target.checked)}
                className="w-4 h-4 rounded text-purple-600 bg-[#0a0c13] border-slate-700"
              />
              <span>Ativar Game Boost temporário antes de inicializar</span>
            </label>
          </div>
        </div>

        {/* Footer */}
        <div className="flex justify-end gap-3 pt-3 border-t border-slate-800">
          <button
            onClick={onClose}
            className="px-4 py-2 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 text-xs font-semibold cursor-pointer"
          >
            Cancelar
          </button>
          <button
            onClick={handleSave}
            className="px-5 py-2 rounded-lg bg-purple-600 hover:bg-purple-500 text-white font-bold text-xs uppercase tracking-wider cursor-pointer flex items-center gap-1.5"
          >
            <Check className="w-4 h-4" />
            <span>Salvar Perfil</span>
          </button>
        </div>
      </div>
    </div>
  );
};
