import React, { useEffect, useState } from 'react';
import {
  Gamepad2,
  X,
  Check,
} from 'lucide-react';
import { GameProfile } from '../../types';
import { useI18n } from '../../i18n';

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
  const { t } = useI18n();
  const [priority, setPriority] = useState<GameProfile['processPriority']>('Normal');
  const [affinity, setAffinity] = useState<GameProfile['cpuAffinity']>('Todos');
  const [powerProfile, setPowerProfile] = useState<GameProfile['powerProfile']>('Equilibrado');
  const [monitorFps, setMonitorFps] = useState(false);

  // Keep the editor synchronized when the selected game changes. All hooks run
  // on every render, including while the modal is closed.
  useEffect(() => {
    if (!game) return;
    setPriority(game.processPriority);
    setAffinity(game.cpuAffinity);
    setPowerProfile(game.powerProfile);
    setMonitorFps(game.monitorFps);
  }, [game]);

  const handleSave = () => {
    if (!game) return;
    onSave({
      ...game,
      processPriority: priority,
      cpuAffinity: affinity,
      powerProfile,
      monitorFps,
      boostEnabled: false,
    });
    onClose();
  };

  if (!isOpen || !game) return null;

  return (
    <div className="fixed inset-0 z-50 bg-black/80 backdrop-blur-xs flex items-center justify-center p-4">
      <div className="w-full max-w-lg bg-[#0f121b] border border-purple-500/50 rounded-xl p-6 space-y-5 shadow-2xl animate-scale-in">
        {/* Header */}
        <div className="flex items-center justify-between pb-3 border-b border-slate-800">
          <div className="flex items-center gap-2.5">
            <Gamepad2 className="w-5 h-5 text-purple-400" />
            <div>
              <h2 className="text-sm font-black text-white uppercase tracking-wider">
                {t.gameCenter.modalTitle}
              </h2>
              <span className="text-xs text-purple-300 font-semibold">{game.title}</span>
            </div>
          </div>

          <button
            onClick={onClose}
            className="text-slate-400 hover:text-white font-bold text-lg cursor-pointer"
            aria-label={t.common.cancel}
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Form Options */}
        <div className="space-y-4 text-xs">
          <div className="space-y-1.5">
            <label className="text-slate-300 font-bold block uppercase text-[11px]">
              {t.gameCenter.priorityLabel}
            </label>
            <select
              value={priority}
              onChange={(e) => setPriority(e.target.value as GameProfile['processPriority'])}
              className="w-full bg-[#0a0c13] border border-slate-800 rounded-lg p-2.5 text-slate-200 focus:outline-none focus:border-purple-500 cursor-pointer"
            >
              <option value="Normal">{t.gameCenter.priorityNormal}</option>
              <option value="Acima do Normal">{t.gameCenter.priorityAboveNormal}</option>
              <option value="Alta">{t.gameCenter.priorityHigh}</option>
            </select>
          </div>

          <div className="space-y-1.5">
            <label className="text-slate-300 font-bold block uppercase text-[11px]">
              {t.gameCenter.affinityLabel}
            </label>
            <select
              value={affinity}
              onChange={(e) => setAffinity(e.target.value as GameProfile['cpuAffinity'])}
              className="w-full bg-[#0a0c13] border border-slate-800 rounded-lg p-2.5 text-slate-200 focus:outline-none focus:border-purple-500 cursor-pointer"
            >
              <option value="Todos">{t.gameCenter.affinityAll}</option>
              <option value="P-Cores Apenas">{t.gameCenter.affinityPCoresOnly}</option>
              <option value="Sem E-Cores">{t.gameCenter.affinityNoECores}</option>
            </select>
          </div>

          <div className="space-y-1.5">
            <label className="text-slate-300 font-bold block uppercase text-[11px]">
              {t.gameCenter.powerPlanLabel}
            </label>
            <select
              value={powerProfile}
              onChange={(e) => setPowerProfile(e.target.value as GameProfile['powerProfile'])}
              className="w-full bg-[#0a0c13] border border-slate-800 rounded-lg p-2.5 text-slate-200 focus:outline-none focus:border-purple-500 cursor-pointer"
            >
              <option value="Desempenho Máximo PC Control">{t.gameCenter.powerPlanUltimate}</option>
              <option value="Alto Desempenho">{t.gameCenter.powerPlanHigh}</option>
              <option value="Equilibrado">{t.gameCenter.powerPlanBalanced}</option>
            </select>
          </div>

          <div className="space-y-2 pt-2 border-t border-slate-800/80">
            <label className="flex items-center gap-2 cursor-pointer text-slate-300">
              <input
                type="checkbox"
                checked={monitorFps}
                onChange={(e) => setMonitorFps(e.target.checked)}
                className="w-4 h-4 rounded text-purple-600 bg-[#0a0c13] border-slate-700"
              />
              <span>{t.gameCenter.autoCapturePresentMon}</span>
            </label>

          </div>
        </div>

        <div className="flex justify-end gap-3 pt-3 border-t border-slate-800">
          <button
            onClick={onClose}
            className="px-4 py-2 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 text-xs font-semibold cursor-pointer"
          >
            {t.common.cancel}
          </button>
          <button
            onClick={handleSave}
            className="px-5 py-2 rounded-lg bg-purple-600 hover:bg-purple-500 text-white font-bold text-xs uppercase tracking-wider cursor-pointer flex items-center gap-1.5"
          >
            <Check className="w-4 h-4" />
            <span>{t.gameCenter.saveProfile}</span>
          </button>
        </div>
      </div>
    </div>
  );
};
