import { HardwareTelemetry } from '../types';

/**
 * Clean initial state without fictional hardware models, fake temperatures, or demo GPU names.
 * Real values are populated exclusively when connected to the authenticated Windows Agent
 * (LibreHardwareMonitor + WMI). When disconnected, the UI displays "Não disponível" / "Unavailable".
 */
export const initialHardwareTelemetry: HardwareTelemetry = {
  isSimulatedMode: false,
  overallScore: 0,
  fpsCaptureActive: false,
  currentFps: undefined,
  lastUpdated: 0,
  cpu: {
    name: '',
    socket: 'Não disponível',
    temperature: 0,
    load: 0,
    frequencyGHz: 0,
    cores: 0,
    threads: 0,
    powerWatts: 0,
    voltageV: 0,
    coreList: [],
  },
  gpu: {
    name: '',
    vendor: 'Outro',
    temperature: 0,
    hotspot: 0,
    memoryTemp: 0,
    clockMHz: 0,
    memoryClockMHz: 0,
    utilization: 0,
    vramUsedMB: 0,
    vramTotalMB: 0,
    powerWatts: 0,
    fansRPM: 0,
    fanSpeedPercent: 0,
    thermalLimitReported: 0,
    isThrottling: false,
    pcieLane: 'Não disponível',
  },
  ram: {
    totalGB: 0,
    usedGB: 0,
    availableGB: 0,
    percentage: 0,
    speedMT: 0,
    channels: 'Não disponível',
    type: 'Não disponível',
  },
  disks: [],
  motherboard: {
    model: 'Não disponível',
    chipset: 'Não disponível',
    biosVersion: 'Não disponível',
    temperatures: {
      system: 0,
      vrm: 0,
      chipset: 0,
    },
    voltages: {
      v12: 0,
      v5: 0,
      v33: 0,
    },
    fans: {
      cpuFanRPM: 0,
      pumpRPM: 0,
      caseFansRPM: [],
    },
  },
};
