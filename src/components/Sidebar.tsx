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
  Globe,
} from 'lucide-react';
import { TabId } from '../types';
import { useI18n } from '../i18n';

interface SidebarProps {
  currentTab: TabId;
  onSelectTab: (tab: TabId) => void;
  appliedTweaksCount: number;
}

interface NavItem {
  id: TabId;
  labelKey: string;
  icon: React.ElementType;
  badge?: string;
}

export const Sidebar: React.FC<SidebarProps> = ({
  currentTab,
  onSelectTab,
  appliedTweaksCount,
}) => {
  const { language, setLanguage, t } = useI18n();

  const navItems: { id: TabId; label: string; icon: React.ElementType; badge?: string }[] = [
    { id: 'dashboard', label: t.navigation.dashboard, icon: LayoutDashboard },
    {
      id: 'boost-center',
      label: t.navigation.boostCenter,
      icon: Zap,
      badge: `${appliedTweaksCount} ${t.common.active}`,
    },
    { id: 'game-center', label: t.navigation.gameCenter, icon: Gamepad2 },
    { id: 'hardware-monitor', label: t.navigation.hardwareMonitor, icon: Cpu },
    {
      id: 'gpu-health',
      label: t.navigation.gpuHealthLab,
      icon: Flame,
      badge: 'Hotspot ΔT',
    },
    {
      id: 'performance-lab',
      label: t.navigation.performanceLab,
      icon: Gauge,
      badge: 'PresentMon',
    },
    { id: 'benchmarks', label: t.navigation.benchmarkCenter, icon: BarChart3 },
    { id: 'diagnostics', label: t.navigation.pcDiagnostics, icon: Stethoscope },
    { id: 'windows-tweaks', label: t.navigation.windowsTweaks, icon: Sliders },
    { id: 'debloat', label: t.navigation.debloatCleaner, icon: Trash2 },
    { id: 'network-lab', label: t.navigation.networkLab, icon: Wifi },
    { id: 'peripherals', label: t.navigation.peripheralTest, icon: Keyboard },
    { id: 'drivers', label: t.navigation.driverCenter, icon: Layers },
    { id: 'software-hub', label: t.navigation.softwareHub, icon: DownloadCloud },
    { id: 'restore-center', label: t.navigation.restoreCenter, icon: History },
    {
      id: 'ai-assistant',
      label: t.navigation.aiAssistant,
      icon: Brain,
      badge: 'Gemini AI',
    },
    {
      id: 'dotnet-solution',
      label: t.navigation.dotnetSolution,
      icon: Code2,
      badge: 'C# .NET 10',
    },
  ];

  return (
    <aside className="w-64 bg-[#0a0c13] border-r border-purple-900/20 flex flex-col justify-between shrink-0 select-none overflow-y-auto">
      {/* Top Navigation List */}
      <div className="p-3 space-y-1">
        <div className="px-3 py-2 text-[10px] font-bold tracking-wider text-purple-400/80 uppercase">
          {t.navigation.modules}
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

      {/* Footer System Status & Language Selector */}
      <div className="p-3 m-2 rounded-lg bg-[#0e111a] border border-slate-800/80 text-[11px] space-y-2">
        {/* Language selector option inside sidebar */}
        <div className="flex items-center justify-between text-slate-400 pb-1.5 border-b border-slate-800/60">
          <span className="flex items-center gap-1.5 text-xs text-slate-300">
            <Globe className="w-3.5 h-3.5 text-purple-400" />
            <span>{t.common.language}</span>
          </span>
          <div className="flex items-center gap-1 bg-[#141724] p-0.5 rounded border border-slate-800">
            <button
              onClick={() => setLanguage('pt-BR')}
              className={`px-1.5 py-0.5 rounded text-[10px] font-bold cursor-pointer transition-colors ${
                language === 'pt-BR'
                  ? 'bg-purple-600 text-white'
                  : 'text-slate-400 hover:text-white'
              }`}
            >
              PT
            </button>
            <button
              onClick={() => setLanguage('en-US')}
              className={`px-1.5 py-0.5 rounded text-[10px] font-bold cursor-pointer transition-colors ${
                language === 'en-US'
                  ? 'bg-purple-600 text-white'
                  : 'text-slate-400 hover:text-white'
              }`}
            >
              EN
            </button>
          </div>
        </div>

        <div className="flex items-center justify-between text-slate-400">
          <span>{t.navigation.activeProfile}</span>
          <span className="font-semibold text-purple-300">{t.navigation.profileGamer}</span>
        </div>
        <div className="flex items-center justify-between text-slate-400">
          <span>{t.navigation.ramUsage}</span>
          <span className="font-mono text-slate-300">35.6% (11.4 / 32 GB)</span>
        </div>
        <div className="w-full bg-slate-800 h-1.5 rounded-full overflow-hidden">
          <div className="bg-purple-500 h-full w-[35.6%]" />
        </div>
      </div>
    </aside>
  );
};
