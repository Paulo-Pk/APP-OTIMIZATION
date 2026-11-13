import { HardwareTelemetry, DriverInfo, RegistryTweakInspection } from '../types';

export interface NativeAgentStatus {
  isConnected: boolean;
  isAuthenticated: boolean;
  version: string | null;
  platform: string;
  isElevated: boolean;
  agentUrl: string;
}

const LOCAL_AGENT_PORT = 52194;
const LOCAL_AGENT_URL = `http://127.0.0.1:${LOCAL_AGENT_PORT}`;

class NativeBridgeService {
  private agentStatus: NativeAgentStatus = {
    isConnected: false,
    isAuthenticated: false,
    version: null,
    platform: 'Web / Sandbox (Agente Desconectado)',
    isElevated: false,
    agentUrl: LOCAL_AGENT_URL,
  };

  private listeners: ((status: NativeAgentStatus) => void)[] = [];
  private sessionToken: string | null = null;

  constructor() {
    // Allow injection from desktop host WebView2 / WPF host if provided
    const injectedToken = (window as unknown as { __PCCONTROL_AGENT_TOKEN__?: string }).__PCCONTROL_AGENT_TOKEN__;
    if (injectedToken) {
      this.sessionToken = injectedToken;
    }
    this.checkConnection();
  }

  public setSessionToken(token: string) {
    this.sessionToken = token.trim();
    this.checkConnection();
  }

  public subscribe(listener: (status: NativeAgentStatus) => void) {
    this.listeners.push(listener);
    listener(this.agentStatus);
    return () => {
      this.listeners = this.listeners.filter((l) => l !== listener);
    };
  }

  private notify() {
    this.listeners.forEach((l) => l(this.agentStatus));
  }

  private getAuthHeaders(): Record<string, string> {
    const nonce =
      typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
        ? crypto.randomUUID().replace(/-/g, '')
        : `${Date.now()}${Math.random().toString(16).slice(2, 14)}`;

    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      'X-PCControl-Client': 'WPF-Native-v2.4',
      'X-PCControl-Timestamp': Math.floor(Date.now() / 1000).toString(),
      'X-PCControl-Nonce': nonce,
    };
    if (this.sessionToken) {
      headers['Authorization'] = `Bearer ${this.sessionToken}`;
    }
    return headers;
  }

  public async checkConnection(): Promise<boolean> {
    try {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), 1200);

      const res = await fetch(`${LOCAL_AGENT_URL}/api/status`, {
        headers: this.getAuthHeaders(),
        signal: controller.signal,
      });
      clearTimeout(timeoutId);

      if (res.ok) {
        const data = await res.json();
        const isAuthenticated = !!data.isAuthenticated;

        // Rule: Never report "Agente conectado" when communication has not been authenticated!
        this.agentStatus = {
          isConnected: isAuthenticated,
          isAuthenticated,
          version: data.version || '2.5.0',
          platform: data.os || 'Windows 10/11 x64',
          isElevated: !!data.isElevated,
          agentUrl: LOCAL_AGENT_URL,
        };
        this.notify();
        return isAuthenticated;
      }
    } catch {
      // Agent not reachable in browser sandbox
    }

    this.agentStatus = {
      isConnected: false,
      isAuthenticated: false,
      version: null,
      platform: 'Web / Sandbox (Agente Desconectado)',
      isElevated: false,
      agentUrl: LOCAL_AGENT_URL,
    };
    this.notify();
    return false;
  }

  public getStatus(): NativeAgentStatus {
    return this.agentStatus;
  }

  public async fetchRealTelemetry(): Promise<HardwareTelemetry | null> {
    if (!this.agentStatus.isConnected) return null;
    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/telemetry`, {
        headers: this.getAuthHeaders(),
      });
      if (res.ok) {
        const data = await res.json();
        // HardwareSnapshot is serialized by System.Text.Json with PascalCase property names.
        // Normalize it explicitly before passing it to the React presentation model.
        const read = (object: any, key: string) => object?.[key] ?? object?.[key.charAt(0).toUpperCase() + key.slice(1)];
        const rawSensors = read(data, 'sensors');
        const sensors = (Array.isArray(rawSensors) ? rawSensors : []).map((sensor: any) => ({
          identifier: String(read(sensor, 'identifier') || ''),
          name: String(read(sensor, 'name') || ''),
          hardwareType: String(read(sensor, 'hardwareType') || read(sensor, 'hardwareCategory') || ''),
          sensorType: String(read(sensor, 'sensorType') || ''),
          value: read(sensor, 'value') ?? null,
          unit: String(read(sensor, 'unit') || ''),
          isAvailable: Boolean(read(sensor, 'isAvailable')),
          status: String(read(sensor, 'status') || ''),
        }));
        const cpuSensors = sensors.filter((sensor: any) => sensor.hardwareType.toLowerCase() === 'cpu' && sensor.isAvailable && Number.isFinite(Number(sensor.value)));
        const gpuSensors = sensors.filter((sensor: any) => /^gpu/i.test(sensor.hardwareType) && sensor.isAvailable && Number.isFinite(Number(sensor.value)));
        const findSensor = (items: any[], type: string, pattern: RegExp) => items.find((sensor) => sensor.sensorType.toLowerCase() === type.toLowerCase() && pattern.test(sensor.name))?.value;
        const numeric = (value: any, fallback = 0) => value !== null && value !== undefined && Number.isFinite(Number(value)) ? Number(value) : fallback;
        const gpuAdaptersRaw = read(data, 'gpuAdapters');
        const gpuAdapter = Array.isArray(gpuAdaptersRaw) ? gpuAdaptersRaw[0] : null;
        const cpuClockCandidates = cpuSensors.filter((sensor: any) => sensor.sensorType.toLowerCase() === 'clock').map((sensor: any) => Number(sensor.value)).filter((value: number) => Number.isFinite(value));
        const cpuClockFallback = cpuClockCandidates.length ? Math.max(...cpuClockCandidates) : 0;
        const cpuClockMhz = numeric(read(data, 'cpuClockMhz'), cpuClockFallback);
        const cpuTemp = numeric(read(data, 'cpuPackageTemp'), numeric(findSensor(cpuSensors, 'Temperature', /tctl|tdie|package|cpu|core/i)));
        const gpuCoreTemp = numeric(read(data, 'gpuCoreTemp'), numeric(read(gpuAdapter, 'coreTempC'), numeric(findSensor(gpuSensors, 'Temperature', /core|gpu(?!.*memory)/i))));
        const gpuHotspot = numeric(read(data, 'gpuHotspotTemp'), numeric(read(gpuAdapter, 'hotspotTempC'), numeric(findSensor(gpuSensors, 'Temperature', /hot.?spot|junction/i))));
        const gpuMemoryTemp = numeric(findSensor(gpuSensors, 'Temperature', /memory|vram/i));
        const gpuMemoryClock = numeric(findSensor(gpuSensors, 'Clock', /memory/i));
        const gpuFanRpm = numeric(read(data, 'gpuFanRpm'), numeric(read(gpuAdapter, 'fanRpm'), numeric(findSensor(gpuSensors, 'Fan', /fan/i))));
        const ramTotal = numeric(read(data, 'ramTotalGb'));
        const ramUsed = numeric(read(data, 'ramUsedGb'));
        const rawStorage = read(data, 'storageDrives');
        const storage = Array.isArray(rawStorage) ? rawStorage : [];
        const vendorText = String(read(gpuAdapter, 'hardwareType') || '').toLowerCase();
        const gpuVendor = vendorText.includes('nvidia') ? 'NVIDIA' : vendorText.includes('amd') ? 'AMD' : vendorText.includes('intel') ? 'Intel' : 'Outro';
        return {
          isSimulatedMode: false,
          overallScore: 0,
          fpsCaptureActive: false,
          lastUpdated: Date.now(),
          cpu: {
            name: String(read(data, 'cpuModelName') || ''), socket: 'Não informado', temperature: cpuTemp,
            load: numeric(read(data, 'cpuTotalLoad')), frequencyGHz: cpuClockMhz / 1000,
            cores: 0, threads: 0, powerWatts: numeric(read(data, 'cpuPowerWatts')), voltageV: 0, coreList: [],
          },
          gpu: {
            name: String(read(data, 'gpuModelName') || read(gpuAdapter, 'model') || ''), vendor: gpuVendor,
            temperature: gpuCoreTemp, hotspot: gpuHotspot, memoryTemp: gpuMemoryTemp,
            clockMHz: numeric(read(data, 'gpuClockMhz'), numeric(read(gpuAdapter, 'coreClockMhz'))),
            memoryClockMHz: gpuMemoryClock, utilization: numeric(read(data, 'gpuLoadPercent'), numeric(read(gpuAdapter, 'loadPercent'))),
            vramUsedMB: numeric(read(data, 'gpuVramUsedMb'), numeric(read(gpuAdapter, 'vramUsedMb'))),
            vramTotalMB: numeric(read(data, 'gpuVramTotalMb'), numeric(read(gpuAdapter, 'vramTotalMb'))),
            powerWatts: numeric(read(data, 'gpuPowerWatts'), numeric(read(gpuAdapter, 'powerWatts'))),
            fansRPM: gpuFanRpm, fanSpeedPercent: numeric(findSensor(gpuSensors, 'Load', /fan/i)),
            thermalLimitReported: 0, isThrottling: false, pcieLane: 'Não informado',
          },
          ram: {
            totalGB: ramTotal, usedGB: ramUsed, availableGB: Math.max(0, ramTotal - ramUsed),
            percentage: ramTotal > 0 ? (ramUsed / ramTotal) * 100 : 0,
            speedMT: 0, channels: 'Não informado', type: 'Não informado',
          },
          disks: storage.map((drive: any, index: number) => {
            const model = String(read(drive, 'model') || 'Unidade ' + (index + 1));
            return {
              id: String(index), model,
              interface: /nvme/i.test(model) ? 'NVMe' : /ssd/i.test(model) ? 'SATA SSD' : /hdd|hard disk/i.test(model) ? 'HDD' : 'Outro',
              temperature: numeric(read(drive, 'temperatureC')), capacityGB: numeric(read(drive, 'capacityGb')),
              freeSpaceGB: numeric(read(drive, 'freeSpaceGb')), healthPercent: 0, readSpeedMBs: 0, writeSpeedMBs: 0, smartStatus: 'N/D',
            };
          }),
          motherboard: {
            model: 'Não informado', chipset: 'Não informado', biosVersion: 'Não informado',
            temperatures: { system: 0, vrm: 0, chipset: 0 }, voltages: { v12: 0, v5: 0, v33: 0 },
            fans: { cpuFanRPM: 0, pumpRPM: 0, caseFansRPM: [] },
          },
          sensors,
        } as HardwareTelemetry;
      }
    } catch (e) {
      console.warn('Failed to fetch real telemetry from native agent:', e);
    }
    return null;
  }

  public async inspectRegistryTweaks(): Promise<{
    success: boolean;
    inspections: RegistryTweakInspection[];
    error?: string;
  }> {
    if (!this.agentStatus.isConnected) {
      return { success: false, inspections: [], error: 'AGENT_DISCONNECTED' };
    }

    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/tweaks/inspect`, {
        headers: this.getAuthHeaders(),
      });
      const data = await res.json();
      if (!res.ok || data.success !== true || !Array.isArray(data.inspections)) {
        return {
          success: false,
          inspections: [],
          error: data.error || data.message || `HTTP ${res.status}`,
        };
      }
      return { success: true, inspections: data.inspections as RegistryTweakInspection[] };
    } catch (err: any) {
      return {
        success: false,
        inspections: [],
        error: err?.message || 'Falha ao consultar o Registro pelo agente Windows',
      };
    }
  }

  public async applyRegistryTweak(
    tweakId: string,
    _keyPath: string,
    _valueName: string,
    targetValue: string | number,
    valueKind: string = 'DWord'
  ): Promise<{
    success: boolean;
    operationId?: string;
    previousValue: string;
    verifiedValue: string;
    valueKind?: string;
    wasInitiallyAbsent?: boolean;
    keyCreatedByApp?: boolean;
    error?: string;
  }> {
    if (!this.agentStatus.isConnected) {
      return {
        success: false,
        previousValue: '',
        verifiedValue: '',
        error: 'AGENT_DISCONNECTED',
      };
    }

    try {
      // Security Policy: Send exclusively tweakId and targetValue; backend determines path, valueName, and valueKind from closed catalog
      const res = await fetch(`${LOCAL_AGENT_URL}/api/tweaks/apply`, {
        method: 'POST',
        headers: this.getAuthHeaders(),
        body: JSON.stringify({
          tweakId,
          targetValue: String(targetValue),
        }),
      });
      const data = await res.json();
      return {
        success: !!data.success,
        operationId: data.operationId,
        previousValue: data.previousValue ?? '',
        verifiedValue: data.verifiedValue ?? '',
        valueKind: data.valueKind ?? valueKind,
        wasInitiallyAbsent: !!data.wasInitiallyAbsent,
        keyCreatedByApp: !!data.keyCreatedByApp,
        error: data.error || data.message,
      };
    } catch (err: any) {
      return {
        success: false,
        previousValue: '',
        verifiedValue: '',
        error: err.message || 'Falha na comunicação com o agente Windows',
      };
    }
  }

  public async restoreRegistryTweak(
    tweakId: string,
    operationId?: string,
    snapshotId?: string
  ): Promise<{ success: boolean; verified: boolean; error?: string }> {
    if (!this.agentStatus.isConnected) {
      return {
        success: false,
        verified: false,
        error: 'AGENT_DISCONNECTED',
      };
    }

    try {
      // Security Policy (Etapa 2): Never send originalValue, valueKind, wasInitiallyAbsent, or keyCreatedByApp from frontend.
      // Backend restores exclusively from its DPAPI+HMAC state ledger or a verified snapshot.
      const res = await fetch(`${LOCAL_AGENT_URL}/api/tweaks/restore`, {
        method: 'POST',
        headers: this.getAuthHeaders(),
        body: JSON.stringify({
          tweakId,
          operationId,
          snapshotId,
        }),
      });
      const data = await res.json();
      return {
        success: !!data.success,
        verified: !!data.verified,
        error: data.error || data.message,
      };
    } catch (err: any) {
      return {
        success: false,
        verified: false,
        error: err.message || 'Falha na restauração com o agente Windows',
      };
    }
  }

  public async discoverInstalledGames(): Promise<Array<{ title: string; launcher: string; installLocation: string; appId: string; executablePath: string | null; isInstalled: boolean }> | null> {
    if (!this.agentStatus.isConnected) return null;
    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/games/discover`, { headers: this.getAuthHeaders() });
      if (!res.ok) return null;
      const data = await res.json();
      return Array.isArray(data.games) ? data.games : [];
    } catch (err) {
      console.warn("Failed to discover installed games:", err);
      return null;
    }
  }

  public async listRunningProcesses(): Promise<Array<{ processId: number; processName: string; mainWindowTitle: string }> | null> {
    if (!this.agentStatus.isConnected) return null;
    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/presentmon/processes`, { headers: this.getAuthHeaders() });
      if (!res.ok) return null;
      const data = await res.json();
      return Array.isArray(data.processes) ? data.processes : [];
    } catch (err) {
      console.warn('Failed to list running processes from native agent:', err);
      return null;
    }
  }

  public async runPresentMonCapture(
    processName: string,
    durationSeconds: number
  ): Promise<{
    avgFps: number;
    minFps?: number;
    onePercentLow: number;
    frametimeAvgMs: number;
    frametimeStDevMs: number;
    capturedFrames: number;
    error?: string;
  } | null> {
    if (!this.agentStatus.isConnected) return null;

    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/presentmon/capture`, {
        method: 'POST',
        headers: this.getAuthHeaders(),
        body: JSON.stringify({ processName, durationSeconds }),
      });
      if (res.ok) {
        const data = await res.json();
        if (data.success) return data;
      }
    } catch (err) {
      console.warn('PresentMon capture native error:', err);
    }
    return null;
  }

  public async scanRealDebloatDirectories(): Promise<{
    d3dCacheBytes: number;
    tempWindowsBytes: number;
    tempUserBytes: number;
    softwareDistributionBytes: number;
    prefetchBytes: number;
    deliveryOptimizationBytes: number;
  } | null> {
    if (!this.agentStatus.isConnected) return null;

    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/debloat/scan`, {
        headers: this.getAuthHeaders(),
      });
      if (res.ok) return await res.json();
    } catch {
      // unavailable
    }
    return null;
  }

  public async purgeRealDebloatDirectory(
    categoryKey: string,
    confirmUpdateCache: boolean = true
  ): Promise<{ success: boolean; partialSuccess: boolean; freedBytes: number; deletedFilesCount: number; skippedLockedCount: number; errors: string[]; status: string }> {
    if (!this.agentStatus.isConnected) return { success: false, partialSuccess: false, freedBytes: 0, deletedFilesCount: 0, skippedLockedCount: 0, errors: ['Agente Windows desconectado.'], status: 'Unavailable' };
    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/debloat/purge`, {
        method: 'POST', headers: this.getAuthHeaders(),
        body: JSON.stringify({ categoryKey, confirmUpdateCache }),
      });
      if (res.ok) {
        const data = await res.json();
        return { success: !!data.success, partialSuccess: !!data.partialSuccess, freedBytes: Number(data.freedBytes) || 0, deletedFilesCount: Number(data.deletedFilesCount) || 0, skippedLockedCount: Number(data.skippedLockedCount) || 0, errors: Array.isArray(data.errors) ? data.errors : [], status: String(data.status || 'Unknown') };
      }
      return { success: false, partialSuccess: false, freedBytes: 0, deletedFilesCount: 0, skippedLockedCount: 0, errors: [`Agente respondeu HTTP ${res.status}.`], status: 'Failed' };
    } catch (err: any) {
      return { success: false, partialSuccess: false, freedBytes: 0, deletedFilesCount: 0, skippedLockedCount: 0, errors: [err?.message || 'Falha de comunicação com o agente.'], status: 'Failed' };
    }
  }

  public async runRealNetworkDiagnostics(): Promise<{
    valvePing: number;
    riotPing: number;
    cloudflarePing: number;
    googleDnsPing: number;
    jitterMs: number;
    packetLoss: number;
  } | null> {
    if (!this.agentStatus.isConnected) return null;

    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/network/diagnostics`, {
        headers: this.getAuthHeaders(),
      });
      if (res.ok) return await res.json();
    } catch {
      // unavailable
    }
    return null;
  }

  public async queryInstalledDrivers(): Promise<DriverInfo[] | null> {
    if (!this.agentStatus.isConnected) return null;
    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/drivers`, {
        headers: this.getAuthHeaders(),
      });
      if (res.ok) {
        const data = await res.json();
        return data.drivers as DriverInfo[];
      }
    } catch {
      // unavailable
    }
    return null;
  }

  public async createRegistrySnapshot(): Promise<{
    success: boolean;
    snapshotId?: string;
    filePath?: string;
    fileSizeBytes?: number;
    checksumSha256?: string;
    capturedKeysCount?: number;
    error?: string;
  }> {
    if (!this.agentStatus.isConnected) {
      return {
        success: false,
        error: 'AGENT_DISCONNECTED',
      };
    }

    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/snapshot/create`, {
        method: 'POST',
        headers: this.getAuthHeaders(),
        body: JSON.stringify({}),
      });
      const data = await res.json();
      return {
        success: !!data.success,
        snapshotId: data.snapshotId,
        filePath: data.filePath,
        fileSizeBytes: data.fileSizeBytes,
        checksumSha256: data.checksumSha256,
        capturedKeysCount: data.capturedKeysCount,
        error: data.error,
      };
    } catch (err: any) {
      return {
        success: false,
        error: err.message || 'Falha na comunicação com o agente Windows para criar backup.',
      };
    }
  }

  public async restoreRegistrySnapshot(filePathOrId: string): Promise<{
    success: boolean;
    partialSuccess?: boolean;
    restoredKeysCount?: number;
    failedKeysCount?: number;
    error?: string;
  }> {
    if (!this.agentStatus.isConnected) {
      return {
        success: false,
        error: 'AGENT_DISCONNECTED',
      };
    }

    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/snapshot/restore`, {
        method: 'POST',
        headers: this.getAuthHeaders(),
        body: JSON.stringify({ filePath: filePathOrId }),
      });
      const data = await res.json();
      return {
        success: !!data.success,
        partialSuccess: !!data.partialSuccess,
        restoredKeysCount: data.restoredKeysCount,
        failedKeysCount: data.failedKeysCount,
        error: data.error,
      };
    } catch (err: any) {
      return {
        success: false,
        error: err.message || 'Falha ao restaurar backup pelo agente Windows.',
      };
    }
  }

  public async verifySnapshotIntegrity(filePathOrId: string): Promise<{
    success: boolean;
    isValid: boolean;
    checksum?: string;
    error?: string;
  }> {
    if (!this.agentStatus.isConnected) {
      return {
        success: false,
        isValid: false,
        error: 'AGENT_DISCONNECTED',
      };
    }

    try {
      const res = await fetch(
        `${LOCAL_AGENT_URL}/api/snapshot/verify?path=${encodeURIComponent(filePathOrId)}`,
        { headers: this.getAuthHeaders() }
      );
      const data = await res.json();
      return {
        success: !!data.success,
        isValid: !!data.isValid,
        checksum: data.checksum,
        error: data.error,
      };
    } catch (err: any) {
      return {
        success: false,
        isValid: false,
        error: err.message,
      };
    }
  }

  public async fetchDisplayInfo(): Promise<{
    available: boolean;
    nativeRefreshRateHz: number | null;
    monitorName?: string;
    resolution?: string;
  }> {
    if (!this.agentStatus.isConnected) {
      return {
        available: false,
        nativeRefreshRateHz: null,
      };
    }

    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/display/info`, {
        headers: this.getAuthHeaders(),
      });
      if (res.ok) {
        const data = await res.json();
        return {
          available: !!data.available,
          nativeRefreshRateHz: data.nativeRefreshRateHz || null,
          monitorName: data.monitorName,
          resolution: data.resolution,
        };
      }
    } catch {
      // not available
    }

    return {
      available: false,
      nativeRefreshRateHz: null,
    };
  }
}

export const nativeBridge = new NativeBridgeService();
