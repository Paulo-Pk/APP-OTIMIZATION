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

  private updateStatus(nextStatus: NativeAgentStatus) {
    const previous = this.agentStatus;
    const changed =
      previous.isConnected !== nextStatus.isConnected ||
      previous.isAuthenticated !== nextStatus.isAuthenticated ||
      previous.version !== nextStatus.version ||
      previous.platform !== nextStatus.platform ||
      previous.isElevated !== nextStatus.isElevated ||
      previous.agentUrl !== nextStatus.agentUrl;

    this.agentStatus = nextStatus;
    if (changed) this.notify();
  }

  private async fetchWithTimeout(url: string, init: RequestInit = {}): Promise<Response> {
    const path = new URL(url).pathname;
    const timeoutMs =
      path === '/api/software/install' ? 5 * 60 * 1000 :
      path === '/api/presentmon/capture' ? 135 * 1000 :
      path === '/api/debloat/purge' ? 120 * 1000 :
      path === '/api/debloat/scan' || path === '/api/snapshot/create' || path === '/api/snapshot/restore' || path === '/api/diagnostics/repair' ? 60 * 1000 :
      path === '/api/drivers' || path === '/api/games/discover' ? 45 * 1000 :
      path === '/api/network/throughput' ? 45 * 1000 :
      path === '/api/network/game-targets' || path === '/api/network/diagnostics' || path === '/api/tweaks/inspect' || path === '/api/diagnostics/system-doctor' ? 30 * 1000 :
      15 * 1000;

    // Keep the deadline active while the caller consumes the response body,
    // not just until the server sends response headers.
    return fetch(url, { ...init, signal: init.signal ?? AbortSignal.timeout(timeoutMs) });
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
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 1200);

    try {
      const res = await fetch(`${LOCAL_AGENT_URL}/api/status`, {
        headers: this.getAuthHeaders(),
        signal: controller.signal,
      });

      if (res.ok) {
        const data = await res.json();
        const isAuthenticated = data.isAuthenticated === true;

        // Never report a connected agent until its status endpoint confirms authentication.
        this.updateStatus({
          isConnected: isAuthenticated,
          isAuthenticated,
          version: data.version || '2.5.0',
          platform: data.os || 'Windows 10/11 x64',
          isElevated: data.isElevated === true,
          agentUrl: LOCAL_AGENT_URL,
        });
        return isAuthenticated;
      }
    } catch {
      // Agent not reachable or its status endpoint did not respond in time.
    } finally {
      clearTimeout(timeoutId);
    }

    this.updateStatus({
      isConnected: false,
      isAuthenticated: false,
      version: null,
      platform: 'Web / Sandbox (Agente Desconectado)',
      isElevated: false,
      agentUrl: LOCAL_AGENT_URL,
    });
    return false;
  }

  public getStatus(): NativeAgentStatus {
    return this.agentStatus;
  }

  public async fetchRealTelemetry(): Promise<HardwareTelemetry | null> {
    if (!this.agentStatus.isConnected) return null;
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 5000);
    try {
      const res = await this.fetchWithTimeout(`${LOCAL_AGENT_URL}/api/telemetry`, {
        headers: this.getAuthHeaders(),
        signal: controller.signal,
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
        // System.Text.Json emits numeric sensor values as JSON numbers. Reject null,
        // strings and non-finite values instead of coercing them (Number(null) is 0),
        // so an unavailable sensor can never become a fabricated zero reading.
        const hasNumericReading = (sensor: any) =>
          sensor.isAvailable && typeof sensor.value === 'number' && Number.isFinite(sensor.value);
        const cpuSensors = sensors.filter((sensor: any) => sensor.hardwareType.toLowerCase() === 'cpu' && hasNumericReading(sensor));
        const gpuSensors = sensors.filter((sensor: any) => /^gpu/i.test(sensor.hardwareType) && hasNumericReading(sensor));
        const findSensor = (items: any[], type: string, pattern: RegExp) => items.find((sensor) => sensor.sensorType.toLowerCase() === type.toLowerCase() && pattern.test(sensor.name))?.value;
        const numeric = (value: any, fallback = 0) => value !== null && value !== undefined && Number.isFinite(Number(value)) ? Number(value) : fallback;
        const firstAvailableReading = (...values: any[]): number | null => {
          const value = values.find((candidate) => typeof candidate === 'number' && Number.isFinite(candidate));
          return value === undefined ? null : value;
        };
        // Preserve a legitimate zero from the first available telemetry source.
        const firstNumeric = (...values: any[]) => {
          const value = values.find((candidate) =>
            candidate !== null && candidate !== undefined && Number.isFinite(Number(candidate)));
          return value === undefined ? 0 : Number(value);
        };
        const gpuAdaptersRaw = read(data, 'gpuAdapters');
        const gpuAdapter = Array.isArray(gpuAdaptersRaw) ? gpuAdaptersRaw[0] : null;
        // Sensor snapshots contain readings for every installed GPU. When the primary
        // adapter exposes its hardware identifier, scope fallback readings to that
        // adapter so a secondary GPU cannot silently supply its temperature/fan data.
        const primaryGpuIdentifier = String(read(gpuAdapter, 'sensorIdentifier') || '').replace(/\\/g, '/').replace(/\/$/, '').toLowerCase();
        const primaryGpuSensors = primaryGpuIdentifier
          ? gpuSensors.filter((sensor: any) => {
              const sensorIdentifier = String(sensor.identifier || '').replace(/\\/g, '/').toLowerCase();
              return sensorIdentifier === primaryGpuIdentifier || sensorIdentifier.startsWith(primaryGpuIdentifier + '/');
            })
          : gpuSensors;
        const cpuClockCandidates = cpuSensors
          .filter((sensor: any) => sensor.sensorType.toLowerCase() === 'clock' && !/\bbus\b/i.test(String(sensor.name || '')))
          .map((sensor: any) => Number(sensor.value))
          .filter((value: number) => Number.isFinite(value));
        const cpuClockFallback = cpuClockCandidates.length ? Math.max(...cpuClockCandidates) : 0;
        // Build per-core readings only from sensors explicitly labeled with a core index.
        // A core is included only when an actual per-core Load reading exists; missing
        // temperature or clock sensors remain null instead of being displayed as zero.
        const cpuCoresById = new Map<number, { coreId: number; load: number | null; temp: number | null; frequencyGHz: number | null }>();
        for (const sensor of cpuSensors) {
          const coreMatch = sensor.name.match(/\bcore\s*#?\s*(\d+)\b/i);
          if (!coreMatch) continue;
          const coreId = Number(coreMatch[1]);
          if (!Number.isSafeInteger(coreId) || coreId < 0) continue;
          const sensorType = sensor.sensorType.toLowerCase();
          let core = cpuCoresById.get(coreId);
          if (!core) {
            core = { coreId, load: null, temp: null, frequencyGHz: null };
            cpuCoresById.set(coreId, core);
          }
          const value = Number(sensor.value);
          if (!Number.isFinite(value)) continue;
          if (sensorType === 'load') core.load = value;
          else if (sensorType === 'temperature') core.temp = value;
          else if (sensorType === 'clock') core.frequencyGHz = value / 1000;
        }
        const cpuCoreList = Array.from(cpuCoresById.values())
          .filter((core): core is { coreId: number; load: number; temp: number | null; frequencyGHz: number | null } => core.load !== null)
          .sort((a, b) => a.coreId - b.coreId);
        const cpuClockMhz = numeric(read(data, 'cpuClockMhz'), cpuClockFallback);
        const cpuTempReading = firstAvailableReading(read(data, 'cpuPackageTemp'), findSensor(cpuSensors, 'Temperature', /tctl|tdie|package|cpu|core/i));
        const gpuCoreTempReading = firstAvailableReading(read(data, 'gpuCoreTemp'), read(gpuAdapter, 'coreTempC'), findSensor(primaryGpuSensors, 'Temperature', /core|gpu(?!.*memory)/i));
        const gpuHotspotReading = firstAvailableReading(read(data, 'gpuHotspotTemp'), read(gpuAdapter, 'hotspotTempC'), findSensor(primaryGpuSensors, 'Temperature', /hot.?spot|junction/i));
        const gpuMemoryTempReading = firstAvailableReading(findSensor(primaryGpuSensors, 'Temperature', /memory|vram/i));
        // Motherboard sensor names vary by vendor; only map explicit labels and keep
        // unavailable readings distinct from a legitimate 0°C numeric value.
        const motherboardSensors = sensors.filter((sensor: any) => sensor.hardwareType.toLowerCase() === 'motherboard' && hasNumericReading(sensor));
        const motherboardSystemTemp = firstAvailableReading(findSensor(motherboardSensors, 'Temperature', /^(system|motherboard|board)\b/i));
        const motherboardVrmTemp = firstAvailableReading(findSensor(motherboardSensors, 'Temperature', /\b(vrm|mos)\b/i));
        const motherboardChipsetTemp = firstAvailableReading(findSensor(motherboardSensors, 'Temperature', /chipset|pch/i));
        const motherboardCpuFan = firstAvailableReading(findSensor(motherboardSensors, 'Fan', /cpu.*fan|fan.*cpu/i));
        const motherboardPumpFan = firstAvailableReading(findSensor(motherboardSensors, 'Fan', /pump|aio|water/i));
        const motherboardCaseFans = motherboardSensors
          .filter((sensor: any) => sensor.sensorType.toLowerCase() === 'fan' && /case|cha.*fan|chassis|sys.*fan/i.test(sensor.name))
          .map((sensor: any) => Number(sensor.value))
          .filter((value: number) => Number.isFinite(value));
        const motherboard12v = firstAvailableReading(findSensor(motherboardSensors, 'Voltage', /(?:^|\s|\+)12(?:v|\b)/i));
        const motherboard5v = firstAvailableReading(findSensor(motherboardSensors, 'Voltage', /(?:^|\s|\+)5(?:v|\b)/i));
        const motherboard33v = firstAvailableReading(findSensor(motherboardSensors, 'Voltage', /(?:^|\s|\+)3[.,]3(?:v|\b)/i));
        const cpuTemp = cpuTempReading ?? 0;
        const gpuCoreTemp = gpuCoreTempReading ?? 0;
        const gpuHotspot = gpuHotspotReading ?? 0;
        const gpuMemoryTemp = gpuMemoryTempReading ?? 0;
        const gpuMemoryClock = numeric(findSensor(primaryGpuSensors, 'Clock', /memory/i));
        // Some adapters expose dedicated VRAM only as SmallData sensors rather
        // than through the aggregate snapshot fields. Resolve those fallbacks
        // against the primary adapter only; never mix in a secondary GPU.
        const gpuVramUsed = firstNumeric(
          read(data, 'gpuVramUsedMb'),
          read(gpuAdapter, 'vramUsedMb'),
          findSensor(primaryGpuSensors, 'SmallData', /^(GPU Memory Used|D3D Dedicated Memory Used)$/i),
        );
        const gpuVramTotal = firstNumeric(
          read(data, 'gpuVramTotalMb'),
          read(gpuAdapter, 'vramTotalMb'),
          findSensor(primaryGpuSensors, 'SmallData', /^(GPU Memory Total|D3D Dedicated Memory Total)$/i),
        );
        const gpuFanRpm = numeric(read(data, 'gpuFanRpm'), numeric(read(gpuAdapter, 'fanRpm'), numeric(findSensor(primaryGpuSensors, 'Fan', /fan/i))));
        const ramTotal = numeric(read(data, 'ramTotalGb'));
        const ramUsed = numeric(read(data, 'ramUsedGb'));
        const rawStorage = read(data, 'storageDrives');
        const storage = Array.isArray(rawStorage) ? rawStorage : [];
        const vendorText = String(read(gpuAdapter, 'hardwareType') || '').toLowerCase();
        const gpuVendor = vendorText.includes('nvidia') ? 'NVIDIA' : vendorText.includes('amd') ? 'AMD' : vendorText.includes('intel') ? 'Intel' : 'Outro';
        const cpuVoltageReading = firstAvailableReading(
          findSensor(cpuSensors, 'Voltage', /core|vcore|svi2|vid|cpu/i),
          findSensor(motherboardSensors, 'Voltage', /vcore|cpu\s*core/i),
        );
        const cpuPhysicalCores = numeric(read(data, 'cpuPhysicalCores'));
        const cpuLogicalProcessors = numeric(read(data, 'cpuLogicalProcessors'));
        const ramSpeedMt = numeric(read(data, 'ramSpeedMt'));
        const ramGeneration = String(read(data, 'ramGeneration') || 'Não informado');
        const motherboardModel = String(read(data, 'motherboardModelName') || 'Não informado');
        return {
          isSimulatedMode: false,
          overallScore: 0,
          fpsCaptureActive: false,
          lastUpdated: Date.now(),
          cpu: {
            name: String(read(data, 'cpuModelName') || ''), socket: 'Não informado', temperature: cpuTemp, temperatureAvailable: cpuTempReading !== null,
            load: numeric(read(data, 'cpuTotalLoad')), frequencyGHz: cpuClockMhz / 1000,
            cores: cpuPhysicalCores, threads: cpuLogicalProcessors, powerWatts: numeric(read(data, 'cpuPowerWatts')), voltageV: cpuVoltageReading ?? 0, coreList: cpuCoreList,
          },
          gpu: {
            name: String(read(data, 'gpuModelName') || read(gpuAdapter, 'model') || ''), vendor: gpuVendor,
            temperature: gpuCoreTemp, temperatureAvailable: gpuCoreTempReading !== null, hotspot: gpuHotspot, hotspotAvailable: gpuHotspotReading !== null, memoryTemp: gpuMemoryTemp, memoryTempAvailable: gpuMemoryTempReading !== null,
            clockMHz: numeric(read(data, 'gpuClockMhz'), numeric(read(gpuAdapter, 'coreClockMhz'))),
            memoryClockMHz: gpuMemoryClock, utilization: numeric(read(data, 'gpuLoadPercent'), numeric(read(gpuAdapter, 'loadPercent'))),
            vramUsedMB: gpuVramUsed,
            vramTotalMB: gpuVramTotal,
            powerWatts: numeric(read(data, 'gpuPowerWatts'), numeric(read(gpuAdapter, 'powerWatts'))),
            fansRPM: gpuFanRpm, fanSpeedPercent: numeric(findSensor(primaryGpuSensors, 'Load', /fan/i)),
            thermalLimitReported: 0, isThrottling: false, pcieLane: 'Não informado',
          },
          ram: {
            totalGB: ramTotal, usedGB: ramUsed, availableGB: Math.max(0, ramTotal - ramUsed),
            percentage: ramTotal > 0 ? (ramUsed / ramTotal) * 100 : 0,
            speedMT: ramSpeedMt, channels: 'Não informado', type: ramGeneration,
          },
          disks: storage.map((drive: any, index: number) => {
            const model = String(read(drive, 'model') || 'Unidade ' + (index + 1));
            return {
              id: String(index), model,
              interface: /nvme/i.test(model) ? 'NVMe' : /ssd/i.test(model) ? 'SATA SSD' : /hdd|hard disk/i.test(model) ? 'HDD' : 'Outro',
              temperature: numeric(read(drive, 'temperatureC')), capacityGB: numeric(read(drive, 'capacityGb')),
              freeSpaceGB: numeric(read(drive, 'freeSpaceGb')), healthPercent: 0, readSpeedMBs: 0, writeSpeedMBs: 0, smartStatus: 'N/D',
              isSystemDisk: Boolean(read(drive, 'isSystemDisk')),
            };
          }),
          motherboard: {
            model: motherboardModel, chipset: 'Não informado', biosVersion: 'Não informado',
            temperatures: { system: motherboardSystemTemp ?? 0, vrm: motherboardVrmTemp ?? 0, chipset: motherboardChipsetTemp ?? 0, systemAvailable: motherboardSystemTemp !== null, vrmAvailable: motherboardVrmTemp !== null, chipsetAvailable: motherboardChipsetTemp !== null },
            voltages: { v12: motherboard12v ?? 0, v5: motherboard5v ?? 0, v33: motherboard33v ?? 0 },
            fans: { cpuFanRPM: motherboardCpuFan ?? 0, pumpRPM: motherboardPumpFan ?? 0, caseFansRPM: motherboardCaseFans },
          },
          sensors,
        } as HardwareTelemetry;
      }
    } catch (e) {
      console.warn('Failed to fetch real telemetry from native agent:', e);
    } finally {
      clearTimeout(timeoutId);
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
      const res = await this.fetchWithTimeout(`${LOCAL_AGENT_URL}/api/tweaks/inspect`, {
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
      const res = await this.fetchWithTimeout(`${LOCAL_AGENT_URL}/api/tweaks/apply`, {
        method: 'POST',
        headers: this.getAuthHeaders(),
        body: JSON.stringify({
          tweakId,
          targetValue: String(targetValue),
        }),
      });
      const data = await res.json();
      return {
        success: res.ok && data.success === true,
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
      const res = await this.fetchWithTimeout(`${LOCAL_AGENT_URL}/api/tweaks/restore`, {
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
        success: res.ok && data.success === true && data.verified === true,
        verified: res.ok && data.verified === true,
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
      const res = await this.fetchWithTimeout(`${LOCAL_AGENT_URL}/api/games/discover`, { headers: this.getAuthHeaders() });
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
      const res = await this.fetchWithTimeout(`${LOCAL_AGENT_URL}/api/presentmon/processes`, { headers: this.getAuthHeaders() });
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
      const res = await this.fetchWithTimeout(`${LOCAL_AGENT_URL}/api/presentmon/capture`, {
        method: 'POST',
        headers: this.getAuthHeaders(),
        body: JSON.stringify({ processName, durationSeconds }),
      });
      const data = await res.json().catch(() => null);
      if (res.ok && data?.success) return data;
      return {
        avgFps: 0,
        onePercentLow: 0,
        frametimeAvgMs: 0,
        frametimeStDevMs: 0,
        capturedFrames: 0,
        error: typeof data?.error === 'string' ? data.error : `Agente respondeu HTTP ${res.status} durante a captura PresentMon.`,
      };
    } catch (err) {
      console.warn('PresentMon capture native error:', err);
      return {
        avgFps: 0,
        onePercentLow: 0,
        frametimeAvgMs: 0,
        frametimeStDevMs: 0,
        capturedFrames: 0,
        error: err instanceof Error ? err.message : 'Falha de comunicação com o agente durante a captura PresentMon.',
      };
    }
  }

  public async installVerifiedSoftware(
    packageId: string,
    userConfirmed: boolean
  ): Promise<{ success: boolean; status?: string; message?: string; error?: string; exitCode?: number } | null> {
    if (!this.agentStatus.isConnected) return null;
    if (!packageId.trim() || !userConfirmed) {
      return { success: false, error: 'EXPLICIT_INSTALL_CONFIRMATION_REQUIRED' };
    }

    try {
      const res = await this.fetchWithTimeout(`${LOCAL_AGENT_URL}/api/software/install`, {
        method: 'POST',
        headers: this.getAuthHeaders(),
        body: JSON.stringify({ packageId: packageId.trim(), userConfirmed: true }),
      });
      const data = await res.json().catch(() => ({}));
      return {
        success: res.ok && !!data.success,
        status: data.status ?? data.Status,
        message: data.message ?? data.Message,
        error: data.error ?? data.Error,
        exitCode: data.exitCode ?? data.ExitCode,
      };
    } catch (err) {
      console.warn('Verified Winget installation failed:', err);
      return { success: false, error: 'AGENT_COMMUNICATION_FAILED' };
    }
  }

  public async scanRealDebloatDirectories(): Promise<{
    d3dCacheBytes: number;
    tempWindowsBytes: number;
    tempUserBytes: number;
    softwareDistributionBytes: number;
    prefetchBytes: number;
    deliveryOptimizationBytes: number;
    werQueueBytes: number;
    werArchiveBytes: number;
  } | null> {
    if (!this.agentStatus.isConnected) return null;

    try {
      const res = await this.fetchWithTimeout(`${LOCAL_AGENT_URL}/api/debloat/scan`, {
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
  ): Promise<{ success: boolean; partialSuccess: boolean; freedBytes: number; deletedFilesCount: number; skippedLockedCount: number; skippedSymlinksCount: number; errors: string[]; status: string }> {
    if (!this.agentStatus.isConnected) return { success: false, partialSuccess: false, freedBytes: 0, deletedFilesCount: 0, skippedLockedCount: 0, skippedSymlinksCount: 0, errors: ['Agente Windows desconectado.'], status: 'Unavailable' };
    try {
      const res = await this.fetchWithTimeout(`${LOCAL_AGENT_URL}/api/debloat/purge`, {
        method: 'POST', headers: this.getAuthHeaders(),
        body: JSON.stringify({ categoryKey, confirmUpdateCache }),
      });
      if (res.ok) {
        const data = await res.json();
        return { success: !!data.success, partialSuccess: !!data.partialSuccess, freedBytes: Number(data.freedBytes) || 0, deletedFilesCount: Number(data.deletedFilesCount) || 0, skippedLockedCount: Number(data.skippedLockedCount) || 0, skippedSymlinksCount: Number(data.skippedSymlinksCount) || 0, errors: Array.isArray(data.errors) ? data.errors : [], status: String(data.status || 'Unknown') };
      }
      return { success: false, partialSuccess: false, freedBytes: 0, deletedFilesCount: 0, skippedLockedCount: 0, skippedSymlinksCount: 0, errors: [`Agente respondeu HTTP ${res.status}.`], status: 'Failed' };
    } catch (err: any) {
      return { success: false, partialSuccess: false, freedBytes: 0, deletedFilesCount: 0, skippedLockedCount: 0, skippedSymlinksCount: 0, errors: [err?.message || 'Falha de comunicação com o agente.'], status: 'Failed' };
    }
  }

  public async measureGameReferenceTargets(gameIds: string[]): Promise<{
    measurementMethod?: string;
    tcpReachabilityPercent?: number;
    structuredDiagnostic?: Record<string, string>;
    results: Array<{
      id: string; server: string; location: string; host: string; protocol: string;
      pingMs: number | null; jitterMs: number | null; tcpFailurePercent: number;
      tcpHandshakeState: string; samples: number[]; isSuccess: boolean;
    }>;
  } | null> {
    if (!this.agentStatus.isConnected || gameIds.length === 0) return null;
    try {
      const query = encodeURIComponent(gameIds.join(','));
      const res = await this.fetchWithTimeout(`${LOCAL_AGENT_URL}/api/network/game-targets?games=${query}`, {
        headers: this.getAuthHeaders(),
      });
      const data = await res.json();
      if (!res.ok) return null;
      const read = (object: any, key: string) => object?.[key] ?? object?.[key.charAt(0).toUpperCase() + key.slice(1)];
      const rawResults = read(data, 'results');
      const rawDiagnostic = read(data, 'structuredDiagnostic');
      const diagnostic = rawDiagnostic ? {
        whatWasTested: String(read(rawDiagnostic, 'whatWasTested') || ''),
        whatFailed: String(read(rawDiagnostic, 'whatFailed') || ''),
        evidenceFound: String(read(rawDiagnostic, 'evidenceFound') || ''),
        probableCause: String(read(rawDiagnostic, 'probableCause') || ''),
        recommendedSolution: String(read(rawDiagnostic, 'recommendedSolution') || ''),
        verificationSteps: String(read(rawDiagnostic, 'verificationSteps') || ''),
      } : undefined;
      return {
        measurementMethod: String(read(data, 'measurementMethod') || ''),
        tcpReachabilityPercent: Number(read(data, 'tcpReachabilityPercent') || 0),
        structuredDiagnostic: diagnostic,
        results: (Array.isArray(rawResults) ? rawResults : []).map((item: any) => ({
          id: String(read(item, 'id') || ''),
          server: String(read(item, 'server') || ''),
          location: String(read(item, 'location') || ''),
          host: String(read(item, 'host') || ''),
          protocol: String(read(item, 'protocol') || 'TCP'),
          pingMs: read(item, 'pingMs') == null ? null : Number(read(item, 'pingMs')),
          jitterMs: read(item, 'jitterMs') == null ? null : Number(read(item, 'jitterMs')),
          tcpFailurePercent: Number(read(item, 'tcpFailurePercent') || 0),
          tcpHandshakeState: String(read(item, 'tcpHandshakeState') || 'Não informado'),
          samples: Array.isArray(read(item, 'samples')) ? read(item, 'samples').map(Number) : [],
          isSuccess: Boolean(read(item, 'isSuccess')),
        })),
      };
    } catch {
      return null;
    }
  }

  public async runRealNetworkDiagnostics(): Promise<any | null> {
    if (!this.agentStatus.isConnected) return null;

    try {
      const res = await this.fetchWithTimeout(`${LOCAL_AGENT_URL}/api/network/diagnostics`, {
        headers: this.getAuthHeaders(),
      });
      if (res.ok) return await res.json();
    } catch {
      // unavailable
    }
    return null;
  }

  public async measureExternalThroughput(): Promise<{ available: boolean; downloadMbps: number | null; error?: string } | null> {
    if (!this.agentStatus.isConnected) return null;
    try {
      const res = await this.fetchWithTimeout(`${LOCAL_AGENT_URL}/api/network/throughput`, {
        headers: this.getAuthHeaders(),
      });
      const data = await res.json();
      if (!res.ok) return { available: false, downloadMbps: null, error: data?.error || `HTTP ${res.status}` };
      return {
        available: Boolean(data.available ?? data.Available),
        downloadMbps: data.downloadMbps ?? data.DownloadMbps ?? null,
        error: data.errorReason ?? data.ErrorReason ?? undefined,
      };
    } catch (error) {
      return { available: false, downloadMbps: null, error: error instanceof Error ? error.message : 'Falha de comunicação com o agente.' };
    }
  }

  public async queryInstalledDrivers(): Promise<{ drivers: DriverInfo[]; scanError?: string } | null> {
    if (!this.agentStatus.isConnected) return null;
    try {
      const res = await this.fetchWithTimeout(`${LOCAL_AGENT_URL}/api/drivers`, {
        headers: this.getAuthHeaders(),
      });
      if (res.ok) {
        const data = await res.json();
        const rawDrivers = Array.isArray(data.drivers) ? data.drivers : [];
        const drivers: DriverInfo[] = rawDrivers.map((driver: any) => ({
          device: String(driver.device ?? driver.Device ?? 'Dispositivo sem nome'),
          category: String(driver.category ?? driver.Category ?? 'Outros') as DriverInfo['category'],
          installedVersion: String(driver.installedVersion ?? driver.InstalledVersion ?? 'N/D'),
          releaseDate: String(driver.releaseDate ?? driver.ReleaseDate ?? 'N/D'),
          vendor: String(driver.vendor ?? driver.Vendor ?? 'Fabricante não informado'),
          // WMI inventory verifies that Windows reports the driver; it does not compare the latest release.
          status: 'Inventariado via WMI',
          signatureStatus: String(driver.status ?? driver.Status ?? 'Assinatura não informada'),
          officialUrl: String(driver.officialUrl ?? driver.OfficialUrl ?? 'https://support.microsoft.com/windows'),
        }));
        return { drivers, scanError: data.scanError ?? data.ScanError ?? undefined };
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
    isComplete?: boolean;
    readFailuresCount?: number;
    unreadableItems?: Array<{ tweakId?: string; keyPath?: string; valueName?: string; reason?: string }>;
    error?: string;
  }> {
    if (!this.agentStatus.isConnected) {
      return {
        success: false,
        error: 'AGENT_DISCONNECTED',
      };
    }

    try {
      const res = await this.fetchWithTimeout(`${LOCAL_AGENT_URL}/api/snapshot/create`, {
        method: 'POST',
        headers: this.getAuthHeaders(),
        body: JSON.stringify({}),
      });
      const data = await res.json();
      return {
        success: res.ok && data.success === true,
        snapshotId: data.snapshotId,
        filePath: data.filePath,
        fileSizeBytes: data.fileSizeBytes,
        checksumSha256: data.checksumSha256,
        capturedKeysCount: data.capturedKeysCount,
        isComplete: data.isComplete ?? data.IsComplete ?? undefined,
        readFailuresCount: data.readFailuresCount ?? data.ReadFailuresCount ?? undefined,
        unreadableItems: Array.isArray(data.unreadableItems ?? data.UnreadableItems)
          ? (data.unreadableItems ?? data.UnreadableItems)
          : undefined,
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
      const res = await this.fetchWithTimeout(`${LOCAL_AGENT_URL}/api/snapshot/restore`, {
        method: 'POST',
        headers: this.getAuthHeaders(),
        body: JSON.stringify({ filePath: filePathOrId }),
      });
      const data = await res.json();
      return {
        success: res.ok && data.success === true,
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
      const res = await this.fetchWithTimeout(
        `${LOCAL_AGENT_URL}/api/snapshot/verify?path=${encodeURIComponent(filePathOrId)}`,
        { headers: this.getAuthHeaders() }
      );
      const data = await res.json();
      return {
        success: res.ok && data.success === true,
        isValid: res.ok && data.isValid === true,
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
      const res = await this.fetchWithTimeout(`${LOCAL_AGENT_URL}/api/display/info`, {
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

  public async runSystemDoctorAudit(): Promise<{
    success: boolean;
    healthScore?: number;
    executiveSummary?: string;
    isRunningAsAdministrator?: boolean;
    findings: Array<{
      category: string;
      title: string;
      severity: string;
      evidence: string;
      probableCause: string;
      recommendedAction: string;
      requiresAdminForRepair: boolean;
      repairCommandId?: string | null;
    }>;
    recommendations: Array<{
      recommendationId: string;
      title: string;
      problemIdentified: string;
      evidenceCollected: string;
      expectedImpact: string;
      riskLevel: string;
      recommendedAction: string;
      targetModule: string;
    }>;
    detectedProfile?: string;
    profileJustification?: string;
    error?: string;
  }> {
    if (!this.agentStatus.isConnected) {
      return { success: false, findings: [], recommendations: [], error: 'AGENT_DISCONNECTED' };
    }

    try {
      const res = await this.fetchWithTimeout(
        `${LOCAL_AGENT_URL}/api/diagnostics/system-doctor`,
        { headers: this.getAuthHeaders() }
      );
      const data = await res.json();
      if (!res.ok || data.success !== true) {
        return {
          success: false,
          findings: [],
          recommendations: [],
          error: data.error || data.message || `HTTP ${res.status}`,
        };
      }
      const read = (obj: any, key: string) => obj?.[key] ?? obj?.[key.charAt(0).toUpperCase() + key.slice(1)];
      const report = read(data, 'report') || {};
      const rawFindings = read(report, 'findings');
      const rawRecs = read(data, 'recommendations');
      const severityNames = ['Informativo', 'Baixo', 'Moderado', 'Alto', 'Critico'];
      const findings = (Array.isArray(rawFindings) ? rawFindings : []).map((f: any) => {
        const rawSev = read(f, 'severity');
        const sevStr = typeof rawSev === 'number' ? (severityNames[rawSev] || 'Informativo') : String(rawSev || 'Informativo');
        return {
          category: String(read(f, 'category') || 'Sistema'),
          title: String(read(f, 'title') || ''),
          severity: sevStr,
          evidence: String(read(f, 'evidence') || ''),
          probableCause: String(read(f, 'probableCause') || ''),
          recommendedAction: String(read(f, 'recommendedAction') || ''),
          requiresAdminForRepair: Boolean(read(f, 'requiresAdminForRepair')),
          repairCommandId: read(f, 'repairCommandId') ?? null,
        };
      });
      const recommendations = (Array.isArray(rawRecs) ? rawRecs : []).map((r: any) => ({
        recommendationId: String(read(r, 'recommendationId') || ''),
        title: String(read(r, 'title') || ''),
        problemIdentified: String(read(r, 'problemIdentified') || ''),
        evidenceCollected: String(read(r, 'evidenceCollected') || ''),
        expectedImpact: String(read(r, 'expectedImpact') || ''),
        riskLevel: String(read(r, 'riskLevel') || 'Seguro'),
        recommendedAction: String(read(r, 'recommendedAction') || ''),
        targetModule: String(read(r, 'targetModule') || ''),
      }));
      return {
        success: true,
        healthScore: Number(read(report, 'healthScore') ?? 0),
        executiveSummary: String(read(report, 'executiveSummary') || ''),
        isRunningAsAdministrator: Boolean(read(report, 'isRunningAsAdministrator')),
        findings,
        recommendations,
        detectedProfile: read(data, 'detectedProfile') ? String(read(data, 'detectedProfile')) : undefined,
        profileJustification: read(data, 'profileJustification') ? String(read(data, 'profileJustification')) : undefined,
      };
    } catch (err: any) {
      return {
        success: false,
        findings: [],
        recommendations: [],
        error: err?.message || 'Falha ao executar auditoria System Doctor no agente Windows.',
      };
    }
  }

  public async executeSystemRepair(
    repairCommandId: string,
    userConfirmed: boolean
  ): Promise<{
    success: boolean;
    commandExecuted?: string;
    requiresAdminElevation?: boolean;
    exitCode?: number;
    outputLog?: string;
    error?: string;
  }> {
    if (!this.agentStatus.isConnected) {
      return { success: false, error: 'AGENT_DISCONNECTED' };
    }

    try {
      const res = await this.fetchWithTimeout(
        `${LOCAL_AGENT_URL}/api/diagnostics/repair`,
        {
          method: 'POST',
          headers: this.getAuthHeaders(),
          body: JSON.stringify({ repairCommandId, userConfirmed }),
        }
      );
      const data = await res.json();
      return {
        success: res.ok && data.success === true,
        commandExecuted: data.commandExecuted,
        requiresAdminElevation: Boolean(data.requiresAdminElevation),
        exitCode: data.exitCode,
        outputLog: data.outputLog,
        error: data.error || (!data.success ? data.outputLog : undefined),
      };
    } catch (err: any) {
      return {
        success: false,
        error: err?.message || 'Falha ao executar ferramenta de reparo no agente Windows.',
      };
    }
  }
}

export const nativeBridge = new NativeBridgeService();
