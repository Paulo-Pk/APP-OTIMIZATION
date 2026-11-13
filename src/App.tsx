import React, { useState, useEffect, useRef } from 'react';
import {
  TabId,
  SystemTweak,
  GameProfile,
  RestoreItem,
  OptimizationLevel,
} from './types';
import { initialTweaks } from './data/defaultTweaks';
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
import { GameNetworkAcceleratorView } from './components/views/GameNetworkAcceleratorView';
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
  const [tweaks, setTweaks] = useState<SystemTweak[]>(() =>
    initialTweaks.map((tweak) => ({
      ...tweak,
      currentValue: 'Não consultado',
      registryState: 'Não consultado',
      registryDiagnostic: 'Aguardando conexão autenticada com o agente Windows.',
      canApply: false,
      canRestore: false,
      isUndeterminable: true,
    }))
  );
  const [games, setGames] = useState<GameProfile[]>([]);
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
  const [isRefreshingTweakStates, setIsRefreshingTweakStates] = useState(false);
  const [tweakInspectionSummary, setTweakInspectionSummary] = useState('Aguardando consulta autenticada do Registro.');
  const tweakInspectionInFlight = useRef(false);

  const handleRefreshTweakStates = async () => {
    if (tweakInspectionInFlight.current) return;
    tweakInspectionInFlight.current = true;
    setIsRefreshingTweakStates(true);
    setTweakInspectionSummary('Consultando os ajustes em modo somente leitura...');

    try {
      const result = await nativeBridge.inspectRegistryTweaks();
      if (!result.success) {
        const message = result.error === 'AGENT_DISCONNECTED'
          ? 'Agente Windows desconectado ou não autenticado; nenhum estado foi presumido.'
          : `Falha na consulta do Registro: ${result.error || 'erro desconhecido'}`;
        setTweaks((previous) => previous.map((item) => ({
          ...item,
          canApply: false,
          canRestore: false,
          registryState: 'Consulta indisponível',
          registryDiagnostic: message,
          isUndeterminable: true,
        })));
        setTweakInspectionSummary(message);
        return;
      }

      const byId = new Map(result.inspections.map((item) => [item.tweakId.toLowerCase(), item]));
      setTweaks((previous) => previous.map((item) => {
        const inspection = byId.get(item.id.toLowerCase());
        if (!inspection) {
          return {
            ...item,
            canApply: false,
            canRestore: false,
            registryState: 'Não encontrado no catálogo nativo',
            registryDiagnostic: 'Este item não foi retornado pelo catálogo autorizado do agente.',
            isUndeterminable: true,
          };
        }

        const absenceLabel = inspection.absenceKind === 'ChaveAusente'
          ? 'Chave ausente'
          : inspection.absenceKind === 'ValorAusenteChaveVazia'
            ? 'Valor ausente; chave vazia'
            : inspection.absenceKind === 'ValorAusenteChaveComConteudo'
              ? 'Valor ausente; chave contém outros dados'
              : 'Valor não informado';
        return {
          ...item,
          isApplied: inspection.state === 'Ativo',
          currentValue: inspection.currentValue ?? absenceLabel,
          registryState: inspection.state,
          registryDiagnostic: inspection.diagnosticDetail || '',
          actualRegistryKind: inspection.actualValueKind || 'N/D',
          expectedRegistryKind: inspection.expectedValueKind,
          canApply: inspection.canApply,
          canRestore: inspection.canRestore,
          isUndeterminable: inspection.isUndeterminable,
        };
      }));

      const active = result.inspections.filter((item) => item.state === 'Ativo').length;
      const inactive = result.inspections.filter((item) => item.state === 'Inativo').length;
      const custom = result.inspections.filter((item) => item.state === 'Personalizado').length;
      const blocked = result.inspections.filter((item) =>
        item.isUndeterminable || item.state === 'Acesso Negado' || item.state === 'Erro de Leitura'
      ).length;
      setTweakInspectionSummary(
        `Leitura confirmada: ${result.inspections.length} ajustes — ${active} ativos, ${inactive} inativos, ${custom} personalizados e ${blocked} com estado indeterminável/bloqueado. Nenhuma chave foi gravada.`
      );
    } catch (error: any) {
      const message = `Erro ao consultar o Registro: ${error?.message || 'falha inesperada'}`;
      setTweakInspectionSummary(message);
      setTweaks((previous) => previous.map((item) => ({
        ...item,
        canApply: false,
        canRestore: false,
        registryState: 'Erro de consulta',
        registryDiagnostic: message,
        isUndeterminable: true,
      })));
    } finally {
      tweakInspectionInFlight.current = false;
      setIsRefreshingTweakStates(false);
    }
  };

  useEffect(() => {
    try {
      localStorage.setItem('pc_control_restore_records', JSON.stringify(restoreItems));
    } catch (e) {
      console.warn('Failed to persist restore records:', e);
    }
  }, [restoreItems]);

  useEffect(() => {
    const unsubscribe = nativeBridge.subscribe((status) => {
      setAgentStatus(status);
      if (!status.isConnected) {
        setTelemetry(initialHardwareTelemetry);
        setTweakInspectionSummary('Agente Windows desconectado; aplicação e restauração ficam bloqueadas até nova consulta.');
        setTweaks((previous) => previous.map((item) => ({
          ...item,
          canApply: false,
          canRestore: false,
        })));
      } else {
        void handleRefreshTweakStates();
      }
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

  useEffect(() => {
    if (!agentStatus.isConnected) { setGames([]); return; }
    let cancelled = false;
    nativeBridge.discoverInstalledGames().then((found) => {
      if (cancelled || !found) return;
      const mapped: GameProfile[] = found
        .filter((game) => game.isInstalled && game.title && !/\b(dlcs?|expansions?(?:\s+pack)?|season\s+pass|add[- ]?ons?|soundtracks?|original\s+soundtrack|dedicated\s+server|test\s+server|public\s+test|sdk|editor|mod\s+tools)\b/i.test(game.title))
        .map((game, index) => ({
          id: `installed-${game.launcher.toLowerCase().replace(/[^a-z0-9]+/g, "-")}-${game.appId || index}`,
          title: game.title,
          platform: (["Steam", "Epic", "Epic Games", "Xbox", "GOG", "Ubisoft Connect", "EA app"].includes(game.launcher) ? game.launcher : "Manual") as GameProfile["platform"],
          executablePath: game.executablePath || "", coverImage: game.launcher === 'Steam' && /^\\d+$/.test(game.appId) ? `https://cdn.cloudflare.steamstatic.com/steam/apps/${game.appId}/library_600x900.jpg` : '',
          processPriority: "Normal", cpuAffinity: "Todos", gpuPreference: "Alto Desempenho", powerProfile: "Equilibrado",
          monitorFps: true, monitorTemps: true, boostEnabled: false,
        }));
      setGames(mapped);
    });
    return () => { cancelled = true; };
  }, [agentStatus.isConnected]);

  // Toggle single tweak with authentic Windows Registry catalog verification
  const handleToggleTweak = async (id: string) => {
    const target = tweaks.find((tweak) => tweak.id === id);
    if (!target) return;

    const nextApplied = !target.isApplied;

    if (nextApplied) {
      if (target.canApply !== true) {
        setNotification({
          type: 'warning',
          title: 'Ajuste bloqueado por segurança',
          message: target.registryDiagnostic || 'A aplicação exige leitura confirmada de um estado padrão/inativo conhecido.',
        });
        return;
      }
      const res = await nativeBridge.applyRegistryTweak(
        target.id,
        target.registryKey || '',
        target.valueName || 'TargetDword',
        target.targetRawValue ?? 1,
        target.valueKind || 'DWord'
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
        await handleRefreshTweakStates();
        return;
      }

      const newRestore: RestoreItem = {
        id: `rest-${Date.now()}-${target.id}`,
        tweakId: target.id,
        operationId: res.operationId,
        registryKey: target.registryKey,
        valueName: target.valueName || 'TargetDword',
        valueKind: res.valueKind || target.valueKind || 'DWord',
        wasInitiallyAbsent: !!res.wasInitiallyAbsent,
        keyCreatedByApp: !!res.keyCreatedByApp,
        timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
        actionName: target.name,
        component: target.category.toUpperCase(),
        previousValue: res.wasInitiallyAbsent
          ? '[Originalmente Inexistente]'
          : res.previousValue || target.currentValue,
        newValue: res.verifiedValue || target.recommendedValue,
        reversible: target.reversible,
        userInitiated: true,
      };

      setRestoreItems((r) => [newRestore, ...r]);
      await handleRefreshTweakStates();
      setNotification({
        type: 'success',
        message: `${target.name}: ${t.common.tweakAppliedSuccess}`,
      });
    } else {
      if (target.canRestore !== true) {
        setNotification({
          type: 'warning',
          title: 'Restauração indisponível',
          message: target.registryDiagnostic || 'Não existe uma operação confiável pendente para restaurar este ajuste.',
        });
        return;
      }
      const existingRecord = restoreItems.find(
        (r) => r.tweakId === target.id || r.actionName === target.name
      );

      const res = await nativeBridge.restoreRegistryTweak(
        target.id,
        existingRecord?.operationId
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
        await handleRefreshTweakStates();
        return;
      }

      setRestoreItems((r) =>
        r.filter((item) => item.tweakId !== target.id && item.actionName !== target.name)
      );
      await handleRefreshTweakStates();
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
    const failedApplyMessages: string[] = [];
    const blockedIds: string[] = [];

    for (const item of tweaks) {
      const shouldApply =
        level === 'advanced'
          ? true
          : level === 'balanced'
          ? item.level === 'conservative' || item.level === 'balanced'
          : item.level === 'conservative';

      if (shouldApply && !item.isApplied && item.canApply !== true) {
        blockedIds.push(item.id);
        continue;
      }

      if (shouldApply && !item.isApplied && item.canApply === true) {
        const res = await nativeBridge.applyRegistryTweak(
          item.id,
          item.registryKey || '',
          item.valueName || 'TargetDword',
          item.targetRawValue ?? 1,
          item.valueKind || 'DWord'
        );

        if (res.success) {
          appliedIds.push(item.id);
          const newRestore: RestoreItem = {
            id: `rest-${Date.now()}-${item.id}`,
            tweakId: item.id,
            operationId: res.operationId,
            registryKey: item.registryKey,
            valueName: item.valueName || 'TargetDword',
            valueKind: res.valueKind || item.valueKind || 'DWord',
            wasInitiallyAbsent: !!res.wasInitiallyAbsent,
            keyCreatedByApp: !!res.keyCreatedByApp,
            timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
            actionName: item.name,
            component: item.category.toUpperCase(),
            previousValue: res.wasInitiallyAbsent
              ? '[Originalmente Inexistente]'
              : res.previousValue || item.currentValue,
            newValue: res.verifiedValue || item.recommendedValue,
            reversible: item.reversible,
            userInitiated: true,
          };
          setRestoreItems((r) => [newRestore, ...r]);
        } else {
          failedApplyMessages.push(`${item.name}: ${res.error || 'falha sem confirmação do agente'}`);
        }
      }
    }

    if (appliedIds.length > 0) {
      await handleRefreshTweakStates();
      if (failedApplyMessages.length > 0 || blockedIds.length > 0) {
        setNotification({
          type: 'warning',
          title: 'Aplicação parcial do perfil',
          message: `${appliedIds.length} aplicados e verificados; ${failedApplyMessages.length} falharam; ${blockedIds.length} bloqueados por estado não elegível. ${failedApplyMessages.slice(0, 3).join(' | ')}`,
        });
      } else {
        setNotification({
          type: 'success',
          message: `${appliedIds.length} otimizações verificadas e aplicadas no Registro do Windows.`,
        });
      }
    } else if (failedApplyMessages.length > 0 || blockedIds.length > 0) {
      await handleRefreshTweakStates();
      setNotification({
        type: 'warning',
        title: 'Nenhum ajuste aplicado',
        message: `${failedApplyMessages.length} falhas e ${blockedIds.length} ajustes bloqueados. Atualize os estados e confira os diagnósticos antes de tentar novamente. ${failedApplyMessages.slice(0, 3).join(' | ')}`,
      });
    }
  };

  // Restore all tweaks to default with strict failure tracking and record preservation
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
    const restoredActionNames = new Set<string>();
    const failedIds: string[] = [];

    for (const item of tweaks) {
      if (item.isApplied) {
        if (item.canRestore !== true) {
          failedIds.push(item.id);
          continue;
        }
        const existingRecord = restoreItems.find(
          (r) => r.tweakId === item.id || r.actionName === item.name
        );

        const res = await nativeBridge.restoreRegistryTweak(
          item.id,
          existingRecord?.operationId
        );

        if (res.success && res.verified) {
          restoredIds.push(item.id);
          restoredActionNames.add(item.name);
        } else {
          failedIds.push(item.id);
        }
      }
    }

    if (restoredIds.length > 0) {
      setTweaks((prev) =>
        prev.map((tweak) => (restoredIds.includes(tweak.id) ? { ...tweak, isApplied: false } : tweak))
      );
    }

    // Remove ONLY confirmed restored tweaks from history; preserve failed records intact
    setRestoreItems((prev) => prev.filter((item) => !restoredActionNames.has(item.actionName)));
    await handleRefreshTweakStates();

    if (failedIds.length > 0 && restoredIds.length > 0) {
      setNotification({
        type: 'warning',
        title: 'Restauração Parcial',
        message: t.restoreCenter.partialRestoreWarning
          .replace('{success}', String(restoredIds.length))
          .replace('{failed}', String(failedIds.length)),
      });
    } else if (failedIds.length > 0 && restoredIds.length === 0) {
      setNotification({
        type: 'error',
        title: 'Falha na Restauração',
        message: t.restoreCenter.restoreFailedAll.replace('{count}', String(failedIds.length)),
      });
    } else if (restoredIds.length > 0 && failedIds.length === 0) {
      setNotification({
        type: 'success',
        message: t.restoreCenter.restoreAllComplete.replace('{count}', String(restoredIds.length)),
      });
    }
  };

  // Rollback single item or full backup from Restore Center
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
    if (!item) return;

    // Handle full supported-settings backup file restoration
    if (item.component === 'REGISTRY BACKUP' && item.snapshotFilePath) {
      const verify = await nativeBridge.verifySnapshotIntegrity(item.snapshotFilePath);
      if (!verify.success || !verify.isValid) {
        setNotification({
          type: 'error',
          title: 'Falha de Integridade SHA-256',
          message: verify.error || 'O arquivo de backup falhou na verificação de integridade ou whitelist.',
        });
        return;
      }

      const snapRes = await nativeBridge.restoreRegistrySnapshot(item.snapshotFilePath);
      if (!snapRes.success) {
        setNotification({
          type: snapRes.partialSuccess ? 'warning' : 'error',
          title: snapRes.partialSuccess ? 'Restauração Parcial do Backup' : 'Falha na Restauração do Backup',
          message:
            snapRes.error ||
            `Restauradas: ${snapRes.restoredKeysCount ?? 0}, Falhas: ${snapRes.failedKeysCount ?? 0}`,
        });
        return;
      }

      setNotification({
        type: 'success',
        message: `Backup restaurado e verificado com sucesso (${snapRes.restoredKeysCount ?? 0} chaves).`,
      });
      return;
    }

    const matchedTweak = tweaks.find(
      (tweak) => tweak.id === item.tweakId || tweak.name === item.actionName
    );
    const targetTweakId = item.tweakId || matchedTweak?.id || '';

    const res = await nativeBridge.restoreRegistryTweak(
      targetTweakId,
      item.operationId
    );

    if (!res.success || !res.verified) {
      setNotification({
        type: 'error',
        message: `${t.common.tweakExecutionFailed}: ${res.error || 'Erro no Agente Windows'}`,
      });
      return;
    }

    if (matchedTweak) {
      setTweaks((prev) =>
        prev.map((tweak) => (tweak.id === matchedTweak.id ? { ...tweak, isApplied: false } : tweak))
      );
    }

    setRestoreItems((prev) => prev.filter((r) => r.id !== id));
    await handleRefreshTweakStates();
    setNotification({
      type: 'success',
      message: `${item.actionName}: ${t.common.tweakRestoredSuccess}`,
    });
  };

  // Real Backup of Supported Registry Settings (Canonical SHA-256)
  const handleCreateSnapshot = async () => {
    if (!agentStatus.isConnected) {
      setNotification({
        type: 'error',
        title: t.restoreCenter.snapshotUnavailableTitle,
        message: t.restoreCenter.snapshotUnavailableDesc,
      });
      return;
    }

    const res = await nativeBridge.createRegistrySnapshot();
    if (!res.success) {
      setNotification({
        type: 'error',
        title: t.restoreCenter.snapshotFailed,
        message: res.error || 'Falha ao comunicar com o serviço de backup dos ajustes suportados.',
      });
      return;
    }

    const snapItem: RestoreItem = {
      id: res.snapshotId || `snap-${Date.now()}`,
      snapshotFilePath: res.filePath,
      checksumSha256: res.checksumSha256,
      timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
      actionName: `Backup dos Ajustes Suportados (${res.filePath ? res.filePath.split('\\').pop() : 'HKLM+HKCU'})`,
      component: 'REGISTRY BACKUP',
      previousValue: res.checksumSha256
        ? `SHA-256: ${res.checksumSha256.substring(0, 16)}...`
        : 'Estado Original Capturado',
      newValue: `${res.capturedKeysCount ?? 0} chaves · ${res.fileSizeBytes ? (res.fileSizeBytes / 1024).toFixed(1) : '0'} KB`,
      reversible: true,
      userInitiated: true,
    };
    setRestoreItems((prev) => [snapItem, ...prev]);
    setNotification({
      type: 'success',
      message: `${t.restoreCenter.snapshotCreatedSuccess}`,
    });
  };

  // Apply diagnostic fix with verified registry execution and restore record
  const handleApplyDiagnosticFix = async (
    tweakId: string,
    issueTitle: string,
    component: string
  ): Promise<{ success: boolean; error?: string }> => {
    if (!agentStatus.isConnected) {
      setNotification({
        type: 'error',
        title: t.common.agentDisconnectedAlert,
        message: t.diagnostics.agentRequiredDiag,
      });
      return { success: false, error: 'AGENT_DISCONNECTED' };
    }

    const target = tweaks.find((tw) => tw.id === tweakId);
    if (!target) {
      return { success: false, error: 'Configuração não encontrada no catálogo de otimizações.' };
    }

    const res = await nativeBridge.applyRegistryTweak(
      target.id,
      target.registryKey || '',
      target.valueName || 'TargetDword',
      target.targetRawValue ?? 1,
      target.valueKind || 'DWord'
    );

    if (!res.success) {
      setNotification({
        type: 'error',
        title: t.diagnostics.fixFailed,
        message: res.error || 'Erro na chamada com o Agente Windows',
      });
      return { success: false, error: res.error };
    }

    const newRestore: RestoreItem = {
      id: `rest-${Date.now()}-${target.id}`,
      tweakId: target.id,
      registryKey: target.registryKey,
      valueName: target.valueName || 'TargetDword',
      valueKind: res.valueKind || target.valueKind || 'DWord',
      wasInitiallyAbsent: !!res.wasInitiallyAbsent,
      keyCreatedByApp: !!res.keyCreatedByApp,
      timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
      actionName: `${issueTitle} (${target.name})`,
      component: component.toUpperCase(),
      previousValue: res.wasInitiallyAbsent
        ? '[Originalmente Inexistente]'
        : res.previousValue || target.currentValue,
      newValue: res.verifiedValue || target.recommendedValue,
      reversible: target.reversible,
      userInitiated: true,
    };

    setRestoreItems((prev) => [newRestore, ...prev]);
    setTweaks((prev) =>
      prev.map((tw) => (tw.id === target.id ? { ...tw, isApplied: true } : tw))
    );

    setNotification({
      type: 'success',
      message: `${issueTitle}: ${t.common.tweakAppliedSuccess}`,
    });

    return { success: true };
  };

  const handleConfirmBoost = () => {
    setIsBoostModalOpen(false);
    handleApplyAllLevel('balanced');
  };

  const handleSaveGameProfile = (updated: GameProfile) => {
    setGames((prev) => prev.map((g) => (g.id === updated.id ? updated : g)));
    setNotification({
      type: 'success',
      message: `${updated.title}: Perfil de afinidade e prioridade salvo com sucesso.`,
    });
  };

  const handleDownloadSolution = async () => {
    try {
      const blob = await exportCompleteSolutionZip();
      downloadBlob(blob, 'PCControlUltimate_DotNet10_Solution.zip');
    } catch (err) {
      console.error('Error generating solution zip:', err);
    }
  };

  const appliedTweaksCount = tweaks.filter((tweak) => tweak.isApplied).length;
  const pendingTweaks = tweaks.filter((tweak) => !tweak.isApplied && tweak.canApply === true);

  return (
    <div className="flex flex-col h-screen bg-[#07090e] text-slate-100 font-sans overflow-hidden select-none">
      <Header
        currentTab={currentTab}
        onNavigate={setCurrentTab}
        onOpenBoostModal={() => setIsBoostModalOpen(true)}
        isSimulatedMode={!agentStatus.isConnected}
        onDownloadSolution={handleDownloadSolution}
        overallScore={telemetry.overallScore}
      />

      <div className="flex-1 flex overflow-hidden relative">
        <Sidebar
          currentTab={currentTab}
          onSelectTab={setCurrentTab}
          appliedTweaksCount={appliedTweaksCount}
        />

        <main className="flex-1 overflow-y-auto p-4 lg:p-6 bg-[#080a11]">
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
                  title: 'Custom Game Process',
                  platform: 'Manual',
                  executablePath: 'C:\\Games\\Game.exe',
                  coverImage: '',
                  processPriority: 'Alta',
                  cpuAffinity: 'P-Cores Apenas',
                  gpuPreference: 'Alto Desempenho',
                  powerProfile: 'Desempenho Máximo PC Control',
                  monitorFps: true,
                  monitorTemps: true,
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
            <GpuHealthLabView gpu={telemetry.gpu} sensors={telemetry.sensors || []} />
          )}

          {currentTab === 'performance-lab' && (
            <PerformanceLabView telemetry={telemetry} />
          )}

          {currentTab === 'benchmarks' && (
            <BenchmarkCenterView telemetry={telemetry} />
          )}

          {currentTab === 'diagnostics' && (
            <DiagnosticsView
              onNavigate={setCurrentTab}
              onOpenBoostModal={() => setIsBoostModalOpen(true)}
              onApplyDiagnosticFix={handleApplyDiagnosticFix}
            />
          )}

          {currentTab === 'windows-tweaks' && (
            <WindowsTweaksView
              tweaks={tweaks}
              onToggleTweak={handleToggleTweak}
              onRestoreAll={handleRestoreAll}
              onRefreshTweakStates={handleRefreshTweakStates}
              isRefreshingTweakStates={isRefreshingTweakStates}
              inspectionSummary={tweakInspectionSummary}
            />
          )}

          {currentTab === 'debloat' && (
            <DebloatView />
          )}

          {currentTab === 'network-lab' && (
            <NetworkLabView />
          )}

          {currentTab === 'game-network-accelerator' && (
            <GameNetworkAcceleratorView />
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

      <BoostConfirmModal
        isOpen={isBoostModalOpen}
        onClose={() => setIsBoostModalOpen(false)}
        onConfirm={handleConfirmBoost}
        pendingTweaks={pendingTweaks.slice(0, 8)}
      />

      <GameProfileModal
        game={gameProfileToEdit}
        isOpen={gameProfileToEdit !== null}
        onClose={() => setGameProfileToEdit(null)}
        onSave={handleSaveGameProfile}
      />
    </div>
  );
}
