import { HardwareTelemetry, DriverInfo } from '../types';

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
        return data as HardwareTelemetry;
      }
    } catch (e) {
      console.warn('Failed to fetch real telemetry from native agent:', e);
    }
    return null;
  }

  public async applyRegistryTweak(
    tweakId: string,
    _keyPath: string,
    _valueName: string,
    targetValue: string | number,
    valueKind: string = 'DWord'
  ): Promise<{
    success: boolean;
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
    keyPath: string,
    valueName: string,
    originalValue: string | number | null,
    valueKind: string = 'DWord',
    wasInitiallyAbsent: boolean = false,
    keyCreatedByApp: boolean = false,
    tweakId?: string
  ): Promise<{ success: boolean; verified: boolean; error?: string }> {
    if (!this.agentStatus.isConnected) {
      return {
        success: false,
        verified: false,
        error: 'AGENT_DISCONNECTED',
      };
    }

    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/tweaks/restore`, {
        method: 'POST',
        headers: this.getAuthHeaders(),
        body: JSON.stringify({
          tweakId,
          keyPath,
          valueName,
          originalValue: originalValue !== null ? String(originalValue) : null,
          valueKind,
          wasInitiallyAbsent,
          keyCreatedByApp,
        }),
      });
      const data = await res.json();
      return {
        success: !!data.success,
        verified: !!data.verified,
        error: data.error,
      };
    } catch (err: any) {
      return {
        success: false,
        verified: false,
        error: err.message || 'Falha na restauração com o agente Windows',
      };
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
