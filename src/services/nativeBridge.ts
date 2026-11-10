import { HardwareTelemetry, SystemTweak, DriverInfo } from '../types';

export interface NativeAgentStatus {
  isConnected: boolean;
  version: string | null;
  platform: string;
  isElevated: boolean; // Running as Administrator
  agentUrl: string;
}

const LOCAL_AGENT_PORT = 52194;
const LOCAL_AGENT_URL = `http://127.0.0.1:${LOCAL_AGENT_PORT}`;

class NativeBridgeService {
  private agentStatus: NativeAgentStatus = {
    isConnected: false,
    version: null,
    platform: 'Web / Sandbox',
    isElevated: false,
    agentUrl: LOCAL_AGENT_URL,
  };

  private listeners: ((status: NativeAgentStatus) => void)[] = [];

  constructor() {
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

  public async checkConnection(): Promise<boolean> {
    try {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), 1200);

      const res = await fetch(`${LOCAL_AGENT_URL}/api/status`, {
        signal: controller.signal,
      });
      clearTimeout(timeoutId);

      if (res.ok) {
        const data = await res.json();
        this.agentStatus = {
          isConnected: true,
          version: data.version || '2.4.0',
          platform: data.os || 'Windows 10/11 x64',
          isElevated: !!data.isElevated,
          agentUrl: LOCAL_AGENT_URL,
        };
        this.notify();
        return true;
      }
    } catch {
      // In web sandbox or service not started yet
    }

    this.agentStatus = {
      isConnected: false,
      version: null,
      platform: 'Web / Sandbox',
      isElevated: false,
      agentUrl: LOCAL_AGENT_URL,
    };
    this.notify();
    return false;
  }

  public getStatus(): NativeAgentStatus {
    return this.agentStatus;
  }

  // Real Hardware Telemetry from LibreHardwareMonitor
  public async fetchRealTelemetry(): Promise<HardwareTelemetry | null> {
    if (!this.agentStatus.isConnected) return null;
    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/telemetry`);
      if (res.ok) {
        const data = await res.json();
        return data as HardwareTelemetry;
      }
    } catch (e) {
      console.warn('Failed to fetch real telemetry from native agent:', e);
    }
    return null;
  }

  // Real Registry Tweak Application
  public async applyRegistryTweak(
    tweakId: string,
    keyPath: string,
    valueName: string,
    targetValue: string | number
  ): Promise<{ success: boolean; previousValue: string; verifiedValue: string; error?: string }> {
    if (!this.agentStatus.isConnected) {
      return {
        success: false,
        previousValue: '',
        verifiedValue: '',
        error: 'AGENT_DISCONNECTED',
      };
    }

    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/tweaks/apply`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tweakId, keyPath, valueName, targetValue }),
      });
      const data = await res.json();
      return {
        success: !!data.success,
        previousValue: data.previousValue || '',
        verifiedValue: data.verifiedValue || '',
        error: data.error,
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

  // Real Registry Tweak Restoration
  public async restoreRegistryTweak(
    keyPath: string,
    valueName: string,
    originalValue: string | number
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
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ keyPath, valueName, originalValue }),
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

  // Real PresentMon Session Capture
  public async runPresentMonCapture(
    processName: string,
    durationSeconds: number
  ): Promise<{
    avgFps: number;
    onePercentLow: number;
    frametimeAvgMs: number;
    frametimeStDevMs: number;
    capturedFrames: number;
  } | null> {
    if (!this.agentStatus.isConnected) return null;

    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/presentmon/capture`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ processName, durationSeconds }),
      });
      if (res.ok) {
        return await res.json();
      }
    } catch (err) {
      console.warn('PresentMon capture native error:', err);
    }
    return null;
  }

  // Real Debloat Scanner & Purger
  public async scanRealDebloatDirectories(): Promise<{
    d3dCacheBytes: number;
    tempWindowsBytes: number;
    tempUserBytes: number;
    softwareDistributionBytes: number;
  } | null> {
    if (!this.agentStatus.isConnected) return null;

    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/debloat/scan`);
      if (res.ok) return await res.json();
    } catch {
      // fallback
    }
    return null;
  }

  public async purgeRealDebloatDirectory(categoryKey: string): Promise<number> {
    if (!this.agentStatus.isConnected) return 0;
    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/debloat/purge`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ categoryKey }),
      });
      if (res.ok) {
        const data = await res.json();
        return data.freedBytes || 0;
      }
    } catch {
      // fallback
    }
    return 0;
  }

  // Real Network ICMP / TCP Pings
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
      const res = await fetch(`${LOCAL_AGENT_URL}/api/network/diagnostics`);
      if (res.ok) return await res.json();
    } catch {
      // fallback
    }
    return null;
  }

  // Real Drivers Scan from Win32_PnPSignedDriver
  public async queryInstalledDrivers(): Promise<DriverInfo[] | null> {
    if (!this.agentStatus.isConnected) return null;
    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/drivers`);
      if (res.ok) {
        const data = await res.json();
        return data.drivers as DriverInfo[];
      }
    } catch {
      // fallback
    }
    return null;
  }
}

export const nativeBridge = new NativeBridgeService();
