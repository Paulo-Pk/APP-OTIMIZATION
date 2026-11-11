export type TabId =
  | 'dashboard'
  | 'boost-center'
  | 'game-center'
  | 'hardware-monitor'
  | 'gpu-health'
  | 'performance-lab'
  | 'benchmarks'
  | 'diagnostics'
  | 'windows-tweaks'
  | 'debloat'
  | 'network-lab'
  | 'game-network-accelerator'
  | 'peripherals'
  | 'drivers'
  | 'software-hub'
  | 'restore-center'
  | 'ai-assistant'
  | 'dotnet-solution';

export type OptimizationLevel = 'conservative' | 'balanced' | 'advanced';
export type RiskLevel = 'Baixo' | 'Médio' | 'Alto';
export type TweakCategory =
  | 'performance'
  | 'startup'
  | 'background'
  | 'power'
  | 'services'
  | 'memory'
  | 'gaming'
  | 'system';

export interface SystemTweak {
  id: string;
  name: string;
  category: TweakCategory;
  description: string;
  expectedBenefit: string;
  risk: RiskLevel;
  level: OptimizationLevel;
  currentValue: string;
  recommendedValue: string;
  isApplied: boolean;
  registryKey?: string;
  valueName?: string;
  valueKind?: 'DWord' | 'QWord' | 'String' | 'ExpandString' | 'MultiString' | 'Binary';
  targetRawValue?: string;
  commandScript?: string;
  reversible: boolean;
}

export interface GpuTelemetry {
  name: string;
  vendor: 'NVIDIA' | 'AMD' | 'Intel' | 'Outro';
  temperature: number;
  hotspot: number;
  memoryTemp: number;
  clockMHz: number;
  memoryClockMHz: number;
  utilization: number;
  vramUsedMB: number;
  vramTotalMB: number;
  powerWatts: number;
  fansRPM: number;
  fanSpeedPercent: number;
  thermalLimitReported: number;
  isThrottling: boolean;
  pcieLane: string;
}

export interface CpuCore {
  coreId: number;
  load: number;
  temp: number;
  frequencyGHz: number;
}

export interface CpuTelemetry {
  name: string;
  socket: string;
  temperature: number;
  load: number;
  frequencyGHz: number;
  cores: number;
  threads: number;
  powerWatts: number;
  voltageV: number;
  coreList: CpuCore[];
}

export interface RamTelemetry {
  totalGB: number;
  usedGB: number;
  availableGB: number;
  percentage: number;
  speedMT: number;
  channels: string;
  type: string;
}

export interface DiskDrive {
  id: string;
  model: string;
  interface: 'NVMe' | 'SATA SSD' | 'HDD';
  temperature: number;
  capacityGB: number;
  freeSpaceGB: number;
  healthPercent: number;
  readSpeedMBs: number;
  writeSpeedMBs: number;
  smartStatus: 'OK' | 'Atenção' | 'Crítico';
}

export interface MotherboardTelemetry {
  model: string;
  chipset: string;
  biosVersion: string;
  temperatures: {
    system: number;
    vrm: number;
    chipset: number;
  };
  voltages: {
    v12: number;
    v5: number;
    v33: number;
  };
  fans: {
    cpuFanRPM: number;
    pumpRPM: number;
    caseFansRPM: number[];
  };
}

export interface HardwareTelemetry {
  cpu: CpuTelemetry;
  gpu: GpuTelemetry;
  ram: RamTelemetry;
  disks: DiskDrive[];
  motherboard: MotherboardTelemetry;
  overallScore: number;
  fpsCaptureActive: boolean;
  currentFps?: number;
  isSimulatedMode: boolean;
  lastUpdated: number;
}

export interface GameProfile {
  id: string;
  title: string;
  platform: 'Steam' | 'Epic' | 'Xbox' | 'Manual';
  executablePath: string;
  coverImage: string;
  processPriority: 'Normal' | 'Acima do Normal' | 'Alta' | 'Tempo Real';
  cpuAffinity: 'Todos' | 'P-Cores Apenas' | 'Sem E-Cores' | 'Personalizado';
  gpuPreference: 'Alto Desempenho' | 'Padrão do Sistema';
  powerProfile: 'Desempenho Máximo PC Control' | 'Alto Desempenho' | 'Equilibrado';
  monitorFps: boolean;
  monitorTemps: boolean;
  lastPlayed?: string;
  averageFps?: number;
  boostEnabled: boolean;
}

export interface PresentMonSession {
  id: string;
  gameTitle: string;
  date: string;
  durationSeconds: number;
  avgFps: number;
  minFps: number;
  onePercentLow: number;
  pointOnePercentLow: number;
  frametimeAvgMs: number;
  frametimeStDevMs: number;
  cpuLoadAvg: number;
  gpuLoadAvg: number;
  cpuTempAvg: number;
  gpuTempAvg: number;
  isOptimized: boolean;
}

export interface BenchmarkRecord {
  id: string;
  type: 'CPU' | 'GPU' | 'RAM' | 'Disco' | 'Geral';
  score: number;
  metric: string;
  date: string;
  hardware: string;
  durationMs: number;
  cpuTempPeak: number;
  gpuTempPeak: number;
  notes?: string;
}

export type HealthStatus = 'Excelente' | 'Bom' | 'Atenção' | 'Crítico';

export interface DiagnosticIssue {
  id: string;
  component: string;
  severity: HealthStatus;
  title: string;
  description: string;
  recommendation: string;
  autoFixAvailable: boolean;
  tweakIdRef?: string;
}

export interface DebloatCategory {
  id: string;
  name: string;
  description: string;
  estimatedBytes: number;
  isSafe: boolean;
  selected: boolean;
}

export interface RestoreItem {
  id: string;
  tweakId?: string;
  operationId?: string;
  registryKey?: string;
  valueName?: string;
  valueKind?: string;
  wasInitiallyAbsent?: boolean;
  keyCreatedByApp?: boolean;
  snapshotFilePath?: string;
  checksumSha256?: string;
  timestamp: string;
  actionName: string;
  component: string;
  previousValue: string;
  newValue: string;
  reversible: boolean;
  userInitiated: boolean;
}

export interface DriverInfo {
  device: string;
  category: 'GPU' | 'Chipset' | 'Audio' | 'Rede' | 'Armazenamento';
  installedVersion: string;
  releaseDate: string;
  vendor: string;
  status: 'Atualizado' | 'Atualização Recomendada' | 'Desatualizado' | 'Verificado via WMI';
  officialUrl: string;
}

export interface SoftwareApp {
  id: string;
  name: string;
  category: 'Runtimes' | 'Comunicação' | 'Gravação & Streaming' | 'Diagnóstico' | 'Utilitários';
  description: string;
  version: string;
  isInstalled: boolean;
  installerUrl: string;
  officialSite: string;
  isOfficialVendor: boolean;
}

export interface DotnetProjectFile {
  path: string;
  name: string;
  content: string;
  language: 'csharp' | 'xml' | 'json' | 'powershell';
  description: string;
}
