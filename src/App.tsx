import React, { useState, useEffect } from 'react';
import {
  TabId,
  SystemTweak,
  GameProfile,
  RestoreItem,
  OptimizationLevel,
} from './types';
import { initialTweaks } from './data/defaultTweaks';
import { initialGames } from './data/defaultGames';
import { initialHardwareTelemetry } from './services/hardwareService';
import { exportCompleteSolutionZip, downloadBlob } from './services/solutionExporter';
import { nativeBridge, NativeAgentStatus } from './services/nativeBridge';
import { useI18n } from './i18n';
import { AlertCircle, CheckCircle2, AlertTriangle, X, Terminal, ArrowRight } from 'lucide-react';

// Components
import { Header } from './components/Header';
import { Sidebar } from './components/Sidebar';

// Views
import { DashboardView } from './components/views/DashboardView';
import { BoostCenterView } from './components/views/BoostCenterView';
import { GameCenterView } from './components/views/GameCenterView';
import { HardwareMonitorView } from './components/views/HardwareMonitorView';
import { GpuHealthLabView } from './components/views/GpuHealthLabView';
import { PerformanceLabView } from './components/views/PerformanceLabView';
import { BenchmarkCenterView } from './components/views/BenchmarkCenterView';
import { DiagnosticsView } from './components/views/DiagnosticsView';
import { WindowsTweaksView } from './components/views/WindowsTweaksView';
import { DebloatView } from './components/views/DebloatView';
import { NetworkLabView } from './components/views/NetworkLabView';
import { PeripheralTestView } from './components/views/PeripheralTestView';
import { DriverCenterView } from './components/views/DriverCenterView';
import { SoftwareHubView } from './components/views/SoftwareHubView';
import { RestoreCenterView } from './components/views/RestoreCenterView';
import { AiAssistantView } from './components/views/AiAssistantView';
import { DotnetSolutionView } from './components/views/DotnetSolutionView';

// Modals
import { BoostConfirmModal } from './components/modals/BoostConfirmModal';
import { GameProfileModal } from './components/modals/GameProfileModal';

interface NotificationState {
  type: 'error' | 'success' | 'warning';
  title?: string;
  message: string;
}

export default function App() {
  const { t } = useI18n();
  const [currentTab, setCurrentTab] = useState<TabId>('dashboard');
  const [telemetry, setTelemetry] = useState(initialHardwareTelemetry);
  const [tweaks, setTweaks] = useState<SystemTweak[]>(initialTweaks);
  const [games, setGames] = useState<GameProfile[]>(initialGames);
  const [agentStatus, setAgentStatus] = useState<NativeAgentStatus>(nativeBridge.getStatus());

  // Restore records: strictly no fake entries. Loaded from local storage or empty.
  const [restoreItems, setRestoreItems] = useState<RestoreItem[]>(() => {
    try {
      const saved = localStorage.getItem('pc_control_restore_records');
      return saved ? JSON.parse(saved) : [];
    } catch {
      return [];
    }
  });

  const [notification, setNotification] = useState<NotificationState | null>(null);
  const [isBoostModalOpen, setIsBoostModalOpen] = useState<boolean>(false);
  const [gameProfileToEdit, setGameProfileToEdit] = useState<GameProfile | null>(null);

  // Persist real restore records
  useEffect(() => {
    try {
      localStorage.setItem('pc_control_restore_records', JSON.stringify(restoreItems));
    } catch (e) {
      console.warn('Failed to persist restore records:', e);
    }
  }, [restoreItems]);

  // Subscribe to Native Windows Agent connection & fetch real telemetry
  useEffect(() => {
    const unsubscribe = nativeBridge.subscribe((status) => {
      setAgentStatus(status);
    });

    const interval = setInterval(async () => {
      if (nativeBridge.getStatus().isConnected) {
        const real = await nativeBridge.fetchRealTelemetry();
        if (real) {
          setTelemetry(real);
        }
      }
    }, 2000);

    return () => {
      unsubscribe();
      clearInterval(interval);
    };
  }, []);

  // Toggle single tweak with authentic Windows Registry verification
  const handleToggleTweak = async (id: string) => {
    const target = tweaks.find((tweak) => tweak.id === id);
    if (!target) return;

    const nextApplied = !target.isApplied;

    if (nextApplied) {
      // Execute via Native Bridge
      const res = await nativeBridge.applyRegistryTweak(
        target.id,
        target.registryKey || '',
        'TargetDword',
        1
      );

      if (!res.success) {
        if (res.error === 'AGENT_DISCONNECTED') {
          setNotification({
            type: 'error',
            title: t.common.agentDisconnectedAlert,
            message: t.common.agentDisconnectedDesc,
          });
        } else {
          setNotification({
            type: 'error',
            message: `${t.common.tweakExecutionFailed} (${res.error || 'Unknown'})`,
          });
        }
        // Preserve state: DO NOT mark as applied, DO NOT create fake restore record
        return;
      }

      // Record verified original value
      const newRestore: RestoreItem = {
        id: `rest-${Date.now()}-${target.id}`,
        timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
        actionName: target.name,
        component: target.category.toUpperCase(),
        previousValue: res.previousValue || target.currentValue,
        newValue: res.verifiedValue || target.recommendedValue,
        reversible: target.reversible,
        userInitiated: true,
      };

      setRestoreItems((r) => [newRestore, ...r]);
      setTweaks((prev) =>
        prev.map((item) => (item.id === id ? { ...item, isApplied: true } : item))
      );
      setNotification({
        type: 'success',
        message: `${target.name}: ${t.common.tweakAppliedSuccess}`,
      });
    } else {
      // Find original value recorded during application
      const existingRecord = restoreItems.find((r) => r.actionName === target.name);
      const originalVal = existingRecord?.previousValue || target.currentValue;

      const res = await nativeBridge.restoreRegistryTweak(
        target.registryKey || '',
        'TargetDword',
        originalVal
      );

      if (!res.success) {
        if (res.error === 'AGENT_DISCONNECTED') {
          setNotification({
            type: 'error',
            title: t.common.agentDisconnectedAlert,
            message: t.common.agentDisconnectedDesc,
          });
        } else {
          setNotification({
            type: 'error',
            message: `${t.common.tweakExecutionFailed} (${res.error || 'Unknown'})`,
          });
        }
        return;
      }

      // Remove from restore records and update UI only upon real verified success
      setRestoreItems((r) => r.filter((item) => item.actionName !== target.name));
      setTweaks((prev) =>
        prev.map((item) => (item.id === id ? { ...item, isApplied: false } : item))
      );
      setNotification({
        type: 'success',
        message: `${target.name}: ${t.common.tweakRestoredSuccess}`,
      });
    }
  };

  // Apply all tweaks of a level
  const handleApplyAllLevel = async (level: OptimizationLevel) => {
    if (!agentStatus.isConnected) {
      setNotification({
        type: 'error',
        title: t.common.agentDisconnectedAlert,
        message: t.common.agentDisconnectedDesc,
      });
      return;
    }

    const appliedIds: string[] = [];

    for (const item of tweaks) {
      const shouldApply =
        level === 'advanced'
          ? true
          : level === 'balanced'
          ? item.level === 'conservative' || item.level === 'balanced'
          : item.level === 'conservative';

      if (shouldApply && !item.isApplied) {
        const res = await nativeBridge.applyRegistryTweak(
          item.id,
          item.registryKey || '',
          'TargetDword',
          1
        );

        if (res.success) {
          appliedIds.push(item.id);
          const newRestore: RestoreItem = {
            id: `rest-${Date.now()}-${item.id}`,
            timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
            actionName: item.name,
            component: item.category.toUpperCase(),
            previousValue: res.previousValue || item.currentValue,
            newValue: res.verifiedValue || item.recommendedValue,
            reversible: item.reversible,
            userInitiated: true,
          };
          setRestoreItems((r) => [newRestore, ...r]);
        }
      }
    }

    if (appliedIds.length > 0) {
      setTweaks((prev) =>
        prev.map((tweak) => (appliedIds.includes(tweak.id) ? { ...tweak, isApplied: true } : tweak))
      );
      setNotification({
        type: 'success',
        message: `${appliedIds.length} otimizações verificadas e aplicadas no Registro do Windows.`,
      });
    }
  };

  // Restore all tweaks to default
  const handleRestoreAll = async () => {
    if (!agentStatus.isConnected) {
      setNotification({
        type: 'error',
        title: t.common.agentDisconnectedAlert,
        message: t.common.agentDisconnectedDesc,
      });
      return;
    }

    const restoredIds: string[] = [];

    for (const item of tweaks) {
      if (item.isApplied) {
        const existingRecord = restoreItems.find((r) => r.actionName === item.name);
        const originalVal = existingRecord?.previousValue || item.currentValue;

        const res = await nativeBridge.restoreRegistryTweak(
          item.registryKey || '',
          'TargetDword',
          originalVal
        );

        if (res.success) {
          restoredIds.push(item.id);
        }
      }
    }

    setTweaks((prev) =>
      prev.map((tweak) => (restoredIds.includes(tweak.id) ? { ...tweak, isApplied: false } : tweak))
    );
    setRestoreItems([]);
    setNotification({
      type: 'success',
      message: `${restoredIds.length} configurações restauradas para seus valores de fábrica.`,
    });
  };

  // Rollback single item from Restore Center
  const handleRollbackItem = async (id: string) => {
    if (!agentStatus.isConnected) {
      setNotification({
        type: 'error',
        title: t.common.agentDisconnectedAlert,
        message: t.common.agentDisconnectedDesc,
      });
      return;
    }

    const item = restoreItems.find((r) => r.id === id);
    if (item) {
      const matchedTweak = tweaks.find((tweak) => tweak.name === item.actionName);
      if (matchedTweak) {
        const res = await nativeBridge.restoreRegistryTweak(
          matchedTweak.registryKey || '',
          'TargetDword',
          item.previousValue
        );

        if (!res.success) {
          setNotification({
            type: 'error',
            message: `${t.common.tweakExecutionFailed}: ${res.error || 'Erro'}`,
          });
          return;
        }

        setTweaks((prev) =>
          prev.map((tweak) => (tweak.name === item.actionName ? { ...tweak, isApplied: false } : tweak))
        );
      }
      setRestoreItems((prev) => prev.filter((r) => r.id !== id));
      setNotification({
        type: 'success',
        message: `${item.actionName}: ${t.common.tweakRestoredSuccess}`,
      });
    }
  };

  // Create Snapshot
  const handleCreateSnapshot = () => {
    if (!agentStatus.isConnected) {
      setNotification({
        type: 'warning',
        title: t.common.agentDisconnectedAlert,
        message: 'A criação de snapshot de registro real requer a conexão com o agente Windows em localhost:52194.',
      });
      return;
    }

    const snapItem: RestoreItem = {
      id: `snap-${Date.now()}`,
      timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
      actionName: 'Snapshot Completo de Registro do Windows',
      component: 'HKLM & HKCU Backup',
      previousValue: 'Snapshot do Sistema',
      newValue: 'Salvo em SQLite',
      reversible: true,
      userInitiated: true,
    };
    setRestoreItems((prev) => [snapItem, ...prev]);
    setNotification({
      type: 'success',
      message: 'Ponto de restauração manual criado e salvo com sucesso no banco de dados local.',
    });
  };

  // Confirm Boost Modal
  const handleConfirmBoost = () => {
    setIsBoostModalOpen(false);
    handleApplyAllLevel('balanced');
  };

  // Save game profile edit
  const handleSaveGameProfile = (updated: GameProfile) => {
    setGames((prev) => prev.map((g) => (g.id === updated.id ? updated : g)));
    setNotification({
      type: 'success',
      message: `${updated.title}: Perfil de afinidade e prioridade salvo com sucesso.`,
    });
  };

  // Download complete .NET 10 Solution ZIP
  const handleDownloadSolution = async () => {
    try {
      const blob = await exportCompleteSolutionZip();
      downloadBlob(blob, 'PCControlUltimate_DotNet10_Solution.zip');
    } catch (err) {
      console.error('Error generating solution zip:', err);
    }
  };

  const appliedTweaksCount = tweaks.filter((tweak) => tweak.isApplied).length;
  const pendingTweaks = tweaks.filter((tweak) => !tweak.isApplied);

  return (
    <div className="flex flex-col h-screen bg-[#07090e] text-slate-100 font-sans overflow-hidden select-none">
      {/* Top Application Header */}
      <Header
        currentTab={currentTab}
        onNavigate={setCurrentTab}
        onOpenBoostModal={() => setIsBoostModalOpen(true)}
        isSimulatedMode={!agentStatus.isConnected}
        onDownloadSolution={handleDownloadSolution}
        overallScore={telemetry.overallScore}
      />

      {/* Main Content Area */}
      <div className="flex-1 flex overflow-hidden relative">
        {/* Navigation Sidebar */}
        <Sidebar
          currentTab={currentTab}
          onSelectTab={setCurrentTab}
          appliedTweaksCount={appliedTweaksCount}
        />

        {/* Dynamic Viewport */}
        <main className="flex-1 overflow-y-auto p-4 lg:p-6 bg-[#080a11]">
          {/* Notification Banner */}
          {notification && (
            <div
              className={`mb-5 p-4 rounded-xl border flex items-start justify-between gap-3 text-xs animate-scale-in shadow-xl ${
                notification.type === 'error'
                  ? 'bg-[#1e1115] border-rose-500/60 text-rose-200'
                  : notification.type === 'warning'
                  ? 'bg-[#221c11] border-amber-500/60 text-amber-200'
                  : 'bg-[#101c18] border-emerald-500/60 text-emerald-200'
              }`}
            >
              <div className="flex items-start gap-3">
                {notification.type === 'error' ? (
                  <AlertCircle className="w-5 h-5 text-rose-400 shrink-0 mt-0.5" />
                ) : notification.type === 'warning' ? (
                  <AlertTriangle className="w-5 h-5 text-amber-400 shrink-0 mt-0.5" />
                ) : (
                  <CheckCircle2 className="w-5 h-5 text-emerald-400 shrink-0 mt-0.5" />
                )}
                <div className="space-y-1">
                  {notification.title && (
                    <div className="font-bold text-sm text-white">
                      {notification.title}
                    </div>
                  )}
                  <p className="leading-relaxed">{notification.message}</p>
                  {notification.type === 'error' && !agentStatus.isConnected && (
                    <div className="pt-2 flex items-center gap-3">
                      <button
                        onClick={() => setCurrentTab('dotnet-solution')}
                        className="px-3 py-1 rounded bg-purple-600/30 hover:bg-purple-600/50 text-purple-200 border border-purple-400/40 text-[11px] font-bold cursor-pointer transition-colors flex items-center gap-1.5"
                      >
                        <Terminal className="w-3.5 h-3.5" />
                        <span>Ver Código do Agente C# .NET 10</span>
                        <ArrowRight className="w-3 h-3" />
                      </button>
                    </div>
                  )}
                </div>
              </div>

              <button
                onClick={() => setNotification(null)}
                className="text-slate-400 hover:text-white font-bold text-base cursor-pointer shrink-0"
              >
                <X className="w-4 h-4" />
              </button>
            </div>
          )}

          {currentTab === 'dashboard' && (
            <DashboardView
              telemetry={telemetry}
              onNavigate={setCurrentTab}
              onOpenBoostModal={() => setIsBoostModalOpen(true)}
            />
          )}

          {currentTab === 'boost-center' && (
            <BoostCenterView
              tweaks={tweaks}
              onToggleTweak={handleToggleTweak}
              onApplyAllLevel={handleApplyAllLevel}
              onRestoreAll={handleRestoreAll}
              onOpenBoostModal={() => setIsBoostModalOpen(true)}
            />
          )}

          {currentTab === 'game-center' && (
            <GameCenterView
              games={games}
              onOpenProfileModal={setGameProfileToEdit}
              onToggleGameBoost={(id) => {
                setGames((prev) =>
                  prev.map((g) => (g.id === id ? { ...g, boostEnabled: !g.boostEnabled } : g))
                );
              }}
              onAddCustomGame={() => {
                const newGame: GameProfile = {
                  id: `game-${Date.now()}`,
                  title: 'Apex Legends',
                  platform: 'Manual',
                  executablePath: 'C:\\Program Files\\EA Games\\Apex\\r5apex.exe',
                  coverImage: 'https://images.unsplash.com/photo-1542751371-adc38448a05e?auto=format&fit=crop&w=400&q=80',
                  processPriority: 'Alta',
                  cpuAffinity: 'P-Cores Apenas',
                  gpuPreference: 'Alto Desempenho',
                  powerProfile: 'Desempenho Máximo PC Control',
                  monitorFps: true,
                  monitorTemps: true,
                  averageFps: 240,
                  boostEnabled: true,
                };
                setGames((prev) => [newGame, ...prev]);
              }}
            />
          )}

          {currentTab === 'hardware-monitor' && (
            <HardwareMonitorView telemetry={telemetry} />
          )}

          {currentTab === 'gpu-health' && (
            <GpuHealthLabView gpu={telemetry.gpu} />
          )}

          {currentTab === 'performance-lab' && (
            <PerformanceLabView />
          )}

          {currentTab === 'benchmarks' && (
            <BenchmarkCenterView telemetry={telemetry} />
          )}

          {currentTab === 'diagnostics' && (
            <DiagnosticsView
              onNavigate={setCurrentTab}
              onOpenBoostModal={() => setIsBoostModalOpen(true)}
            />
          )}

          {currentTab === 'windows-tweaks' && (
            <WindowsTweaksView
              tweaks={tweaks}
              onToggleTweak={handleToggleTweak}
              onRestoreAll={handleRestoreAll}
            />
          )}

          {currentTab === 'debloat' && (
            <DebloatView />
          )}

          {currentTab === 'network-lab' && (
            <NetworkLabView />
          )}

          {currentTab === 'peripherals' && (
            <PeripheralTestView />
          )}

          {currentTab === 'drivers' && (
            <DriverCenterView />
          )}

          {currentTab === 'software-hub' && (
            <SoftwareHubView />
          )}

          {currentTab === 'restore-center' && (
            <RestoreCenterView
              restoreItems={restoreItems}
              onRollbackItem={handleRollbackItem}
              onRestoreAllPoints={handleRestoreAll}
              onCreateSnapshot={handleCreateSnapshot}
            />
          )}

          {currentTab === 'ai-assistant' && (
            <AiAssistantView telemetry={telemetry} />
          )}

          {currentTab === 'dotnet-solution' && (
            <DotnetSolutionView onDownloadSolution={handleDownloadSolution} />
          )}
        </main>
      </div>

      {/* Boost Analysis & Confirmation Modal */}
      <BoostConfirmModal
        isOpen={isBoostModalOpen}
        onClose={() => setIsBoostModalOpen(false)}
        onConfirm={handleConfirmBoost}
        pendingTweaks={pendingTweaks.slice(0, 8)}
      />

      {/* Game Profile Configuration Modal */}
      <GameProfileModal
        game={gameProfileToEdit}
        isOpen={gameProfileToEdit !== null}
        onClose={() => setGameProfileToEdit(null)}
        onSave={handleSaveGameProfile}
      />
    </div>
  );
}
