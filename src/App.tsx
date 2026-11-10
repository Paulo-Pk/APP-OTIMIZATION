import React, { useState } from 'react';
import { TabId, SystemTweak, GameProfile, RestoreItem, OptimizationLevel } from './types';
import { initialTweaks } from './data/defaultTweaks';
import { initialGames } from './data/defaultGames';
import { initialHardwareTelemetry } from './services/hardwareService';
import { exportCompleteSolutionZip, downloadBlob } from './services/solutionExporter';

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

export default function App() {
  const [currentTab, setCurrentTab] = useState<TabId>('dashboard');
  const [telemetry, setTelemetry] = useState(initialHardwareTelemetry);
  const [tweaks, setTweaks] = useState<SystemTweak[]>(initialTweaks);
  const [games, setGames] = useState<GameProfile[]>(initialGames);
  const [restoreItems, setRestoreItems] = useState<RestoreItem[]>([
    {
      id: 'rest-1',
      timestamp: 'Hoje, 09:30',
      actionName: 'Otimização Multimedia Class Scheduler (MMCSS)',
      component: 'Registro HKLM',
      previousValue: 'NetworkThrottling=10',
      newValue: 'NetworkThrottling=0xFFFFFFFF',
      reversible: true,
      userInitiated: true,
    },
    {
      id: 'rest-2',
      timestamp: 'Hoje, 09:32',
      actionName: 'Desativar Algoritmo de Nagle (TCP NoDelay)',
      component: 'Adaptador de Rede',
      previousValue: 'TcpAckFrequency=2',
      newValue: 'TcpAckFrequency=1',
      reversible: true,
      userInitiated: true,
    },
  ]);

  const [isBoostModalOpen, setIsBoostModalOpen] = useState<boolean>(false);
  const [gameProfileToEdit, setGameProfileToEdit] = useState<GameProfile | null>(null);

  // Toggle single tweak
  const handleToggleTweak = (id: string) => {
    setTweaks((prev) =>
      prev.map((t) => {
        if (t.id === id) {
          const nextApplied = !t.isApplied;
          if (nextApplied) {
            // Record restore point
            const newRestore: RestoreItem = {
              id: `rest-${Date.now()}`,
              timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
              actionName: t.name,
              component: t.category.toUpperCase(),
              previousValue: t.currentValue,
              newValue: t.recommendedValue,
              reversible: t.reversible,
              userInitiated: true,
            };
            setRestoreItems((r) => [newRestore, ...r]);
          }
          return { ...t, isApplied: nextApplied };
        }
        return t;
      })
    );
  };

  // Apply all tweaks of a level
  const handleApplyAllLevel = (level: OptimizationLevel) => {
    setTweaks((prev) =>
      prev.map((t) => {
        const shouldApply =
          level === 'advanced'
            ? true
            : level === 'balanced'
            ? t.level === 'conservative' || t.level === 'balanced'
            : t.level === 'conservative';

        if (shouldApply && !t.isApplied) {
          const newRestore: RestoreItem = {
            id: `rest-${Date.now()}-${t.id}`,
            timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
            actionName: t.name,
            component: t.category.toUpperCase(),
            previousValue: t.currentValue,
            newValue: t.recommendedValue,
            reversible: t.reversible,
            userInitiated: true,
          };
          setRestoreItems((r) => [newRestore, ...r]);
          return { ...t, isApplied: true };
        }
        return t;
      })
    );
  };

  // Restore all tweaks to default
  const handleRestoreAll = () => {
    setTweaks((prev) => prev.map((t) => ({ ...t, isApplied: false })));
  };

  // Rollback single item from Restore Center
  const handleRollbackItem = (id: string) => {
    const item = restoreItems.find((r) => r.id === id);
    if (item) {
      setTweaks((prev) =>
        prev.map((t) => (t.name === item.actionName ? { ...t, isApplied: false } : t))
      );
      setRestoreItems((prev) => prev.filter((r) => r.id !== id));
    }
  };

  // Create Snapshot
  const handleCreateSnapshot = () => {
    const snapItem: RestoreItem = {
      id: `snap-${Date.now()}`,
      timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
      actionName: 'Snapshot Completo de Registro do Windows (Manual)',
      component: 'Backup de Chaves HKLM/HKCU',
      previousValue: 'Snapshot Atual',
      newValue: 'Salvo em SQLite',
      reversible: true,
      userInitiated: true,
    };
    setRestoreItems((prev) => [snapItem, ...prev]);
  };

  // Confirm Boost Modal
  const handleConfirmBoost = () => {
    handleApplyAllLevel('balanced');
    setIsBoostModalOpen(false);
  };

  // Save game profile edit
  const handleSaveGameProfile = (updated: GameProfile) => {
    setGames((prev) => prev.map((g) => (g.id === updated.id ? updated : g)));
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

  const appliedTweaksCount = tweaks.filter((t) => t.isApplied).length;
  const pendingTweaks = tweaks.filter((t) => !t.isApplied);

  return (
    <div className="flex flex-col h-screen bg-[#07090e] text-slate-100 font-sans overflow-hidden select-none">
      {/* Top Application Header */}
      <Header
        currentTab={currentTab}
        onNavigate={setCurrentTab}
        onOpenBoostModal={() => setIsBoostModalOpen(true)}
        isSimulatedMode={telemetry.isSimulatedMode}
        onDownloadSolution={handleDownloadSolution}
        overallScore={telemetry.overallScore}
      />

      {/* Main Content Area */}
      <div className="flex-1 flex overflow-hidden">
        {/* Navigation Sidebar */}
        <Sidebar
          currentTab={currentTab}
          onSelectTab={setCurrentTab}
          appliedTweaksCount={appliedTweaksCount}
        />

        {/* Dynamic Viewport */}
        <main className="flex-1 overflow-y-auto p-4 lg:p-6 bg-[#080a11]">
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
