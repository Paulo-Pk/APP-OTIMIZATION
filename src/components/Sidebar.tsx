import React from 'react';
import {
  LayoutDashboard,
  Zap,
  Gamepad2,
  Cpu,
  Flame,
  Gauge,
  BarChart3,
  Stethoscope,
  Sliders,
  Trash2,
  Wifi,
  Keyboard,
  Layers,
  DownloadCloud,
  History,
  Brain,
  Code2,
} from 'lucide-react';
import { TabId } from '../types';

interface SidebarProps {
  currentTab: TabId;
  onSelectTab: (tab: TabId) => void;
  appliedTweaksCount: number;
}

interface NavItem {
  id: TabId;
  label: string;
  icon: React.ElementType;
  badge?: string;
  category?: string;
}

export const Sidebar: React.FC<SidebarProps> = ({
  currentTab,
  onSelectTab,
  appliedTweaksCount,
}) => {
  const navItems: NavItem[] = [
    { id: 'dashboard', label: 'Dashboard', icon: LayoutDashboard },
    {
      id: 'boost-center',
      label: 'Boost Center',
      icon: Zap,
      badge: `${appliedTweaksCount} ativos`,
    },
    { id: 'game-center', label: 'Game Center', icon: Gamepad2 },
    { id: 'hardware-monitor', label: 'Hardware Monitor', icon: Cpu },
    {
      id: 'gpu-health',
      label: 'GPU Health Lab',
      icon: Flame,
      badge: 'Hotspot ΔT',
    },
    {
      id: 'performance-lab',
      label: 'Performance Lab',
      icon: Gauge,
      badge: 'PresentMon',
    },
    { id: 'benchmarks', label: 'Benchmark Center', icon: BarChart3 },
    { id: 'diagnostics', label: 'PC Diagnostics', icon: Stethoscope },
    { id: 'windows-tweaks', label: 'Windows Tweaks', icon: Sliders },
    { id: 'debloat', label: 'Debloat & Cleaner', icon: Trash2 },
    { id: 'network-lab', label: 'Network Lab', icon: Wifi },
    { id: 'peripherals', label: 'Peripheral Test', icon: Keyboard },
    { id: 'drivers', label: 'Driver Center', icon: Layers },
    { id: 'software-hub', label: 'Software Hub', icon: DownloadCloud },
    { id: 'restore-center', label: 'Restore Center', icon: History },
    {
      id: 'ai-assistant',
      label: 'Intelligent Assistant',
      icon: Brain,
      badge: 'Gemini AI',
    },
    {
      id: 'dotnet-solution',
      label: 'Solução .NET 10 (C#)',
      icon: Code2,
      badge: 'Código C#',
    },
  ];

  return (
    <aside className="w-64 bg-[#0a0c13] border-r border-purple-900/20 flex flex-col justify-between shrink-0 select-none overflow-y-auto">
      {/* Top Navigation List */}
      <div className="p-3 space-y-1">
        <div className="px-3 py-2 text-[10px] font-bold tracking-wider text-purple-400/80 uppercase">
          Módulos do Sistema
        </div>

        <nav className="space-y-0.5">
          {navItems.map((item) => {
            const Icon = item.icon;
            const isActive = currentTab === item.id;
            return (
              <button
                key={item.id}
                onClick={() => onSelectTab(item.id)}
                className={`w-full flex items-center justify-between px-3 py-2 rounded-lg text-xs font-medium transition-all text-left cursor-pointer ${
                  isActive
                    ? 'bg-purple-600/20 text-purple-200 border border-purple-500/40 shadow-sm shadow-purple-900/30'
                    : 'text-slate-400 hover:text-slate-200 hover:bg-[#131724]'
                }`}
              >
                <div className="flex items-center gap-2.5 truncate">
                  <Icon
                    className={`w-4 h-4 shrink-0 ${
                      isActive ? 'text-purple-400' : 'text-slate-500'
                    }`}
                  />
                  <span className="truncate">{item.label}</span>
                </div>
                {item.badge && (
                  <span
                    className={`text-[10px] font-mono px-1.5 py-0.5 rounded ${
                      isActive
                        ? 'bg-purple-500/30 text-purple-200'
                        : 'bg-slate-800 text-slate-400'
                    }`}
                  >
                    {item.badge}
                  </span>
                )}
              </button>
            );
          })}
        </nav>
      </div>

      {/* Footer System Status */}
      <div className="p-3 m-2 rounded-lg bg-[#0e111a] border border-slate-800/80 text-[11px] space-y-1.5">
        <div className="flex items-center justify-between text-slate-400">
          <span>Perfil Ativo:</span>
          <span className="font-semibold text-purple-300">Gamer Equilibrado</span>
        </div>
        <div className="flex items-center justify-between text-slate-400">
          <span>Memória RAM:</span>
          <span className="font-mono text-slate-300">35.6% (11.4 / 32 GB)</span>
        </div>
        <div className="w-full bg-slate-800 h-1.5 rounded-full overflow-hidden">
          <div className="bg-purple-500 h-full w-[35.6%]" />
        </div>
      </div>
    </aside>
  );
};
