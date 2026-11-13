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
        // The Windows agent returns HardwareSnapshot (C# domain model), not the
        // presentation HardwareTelemetry shape. Normalize explicitly instead of
        // casting incompatible objects and silently rendering zero/empty values.
        const sensors = Array.isArray(data.sensors) ? data.sensors : [];
        const cpuSensors = sensors.filter((s: any) => String(s.hardwareType || s.hardwareCategory || '').toLowerCase() === 'cpu' && s.isAvailable && Number.isFinite(Number(s.value)));
        const gpuSensors = sensors.filter((s: any) => /^gpu/i.test(String(s.hardwareType || s.hardwareCategory || '')) && s.isAvailable && Number.isFinite(Number(s.value)));
        const findSensor = (items: any[], type: string, pattern: RegExp) => items.find((s) => String(s.sensorType || '').toLowerCase() === type.toLowerCase() && pattern.test(String(s.name || '')))?.value;
        const numeric = (value: any, fallback = 0) => Number.isFinite(Number(value)) ? Number(value) : fallback;
        const gpuAdapter = Array.isArray(data.gpuAdapters) ? data.gpuAdapters[0] : null;
        const cpuClockMhz = numeric(data.cpuClockMhz, numeric(findSensor(cpuSensors, 'Clock', /core/i)));
        const cpuTemp = numeric(data.cpuPackageTemp, numeric(findSensor(cpuSensors, 'Temperature', /tctl|tdie|package|cpu|core/i)));
        const gpuCoreTemp = numeric(data.gpuCoreTemp, numeric(gpuAdapter?.coreTempC, numeric(findSensor(gpuSensors, 'Temperature', /core|gpu(?!.*memory)/i))));
        const gpuHotspot = numeric(data.gpuHotspotTemp, numeric(gpuAdapter?.hotspotTempC, numeric(findSensor(gpuSensors, 'Temperature', /hot.?spot|junction/i))));
        const gpuMemoryTemp = numeric(findSensor(gpuSensors, 'Temperature', /memory|vram/i));
        const gpuMemoryClock = numeric(findSensor(gpuSensors, 'Clock', /memory/i));
        const gpuFanRpm = numeric(data.gpuFanRpm, numeric(gpuAdapter?.fanRpm, numeric(findSensor(gpuSensors, 'Fan', /fan/i))));
        const ramTotal = numeric(data.ramTotalGb);
        const ramUsed = numeric(data.ramUsedGb);
        const storage = Array.isArray(data.storageDrives) ? data.storageDrives : [];
        const vendorText = String(gpuAdapter?.hardwareType || '').toLowerCase();
        const gpuVendor = vendorText.includes('nvidia') ? 'NVIDIA' : vendorText.includes('amd') ? 'AMD' : vendorText.includes('intel') ? 'Intel' : 'Outro';
        return {
          isSimulatedMode: false,
          overallScore: 0,
          fpsCaptureActive: false,
          lastUpdated: Date.now(),
          cpu: {
            name: data.cpuModelName || '', socket: 'Não informado', temperature: cpuTemp,
            load: numeric(data.cpuTotalLoad), frequencyGHz: cpuClockMhz / 1000,
            cores: 0, threads: 0, powerWatts: numeric(data.cpuPowerWatts), voltageV: 0, coreList: [],
          },
          gpu: {
            name: data.gpuModelName || gpuAdapter?.model || '', vendor: gpuVendor,
            temperature: gpuCoreTemp, hotspot: gpuHotspot, memoryTemp: gpuMemoryTemp,
            clockMHz: numeric(data.gpuClockMhz, numeric(gpuAdapter?.coreClockMhz)),
            memoryClockMHz: gpuMemoryClock, utilization: numeric(data.gpuLoadPercent, numeric(gpuAdapter?.loadPercent)),
            vramUsedMB: numeric(data.gpuVramUsedMb, numeric(gpuAdapter?.vramUsedMb)),
            vramTotalMB: numeric(data.gpuVramTotalMb, numeric(gpuAdapter?.vramTotalMb)),
            powerWatts: numeric(data.gpuPowerWatts, numeric(gpuAdapter?.powerWatts)),
            fansRPM: gpuFanRpm, fanSpeedPercent: numeric(findSensor(gpuSensors, 'Load', /fan/i)),
            thermalLimitReported: 0, isThrottling: false, pcieLane: 'Não informado',
          },
          ram: {
            totalGB: ramTotal, usedGB: ramUsed, availableGB: Math.max(0, ramTotal - ramUsed),
            percentage: ramTotal > 0 ? (ramUsed / ramTotal) * 100 : 0,
            speedMT: 0, channels: 'Não informado', type: 'Não informado',
          },
          disks: storage.map((drive: any, index: number) => ({
            id: String(index), model: String(drive.model || `Unidade ${index + 1}`),
            interface: /nvme/i.test(String(drive.model || '')) ? 'NVMe' : /ssd/i.test(String(drive.model || '')) ? 'SATA SSD' : /hdd|hard disk/i.test(String(drive.model || '')) ? 'HDD' : 'Outro',
            temperature: numeric(drive.temperatureC), capacityGB: numeric(drive.capacityGb),
            freeSpaceGB: numeric(drive.freeSpaceGb), healthPercent: 0, readSpeedMBs: 0, writeSpeedMBs: 0, smartStatus: 'N/D',
          })),
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
  ): Promise<number> {
    if (!this.agentStatus.isConnected) return 0;
    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/debloat/purge`, {
        method: 'POST',
        headers: this.getAuthHeaders(),
        body: JSON.stringify({ categoryKey, confirmUpdateCache }),
      });
      if (res.ok) {
        const data = await res.json();
        return data.freedBytes || 0;
      }
    } catch {
      // unavailable
    }
    return 0;
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
