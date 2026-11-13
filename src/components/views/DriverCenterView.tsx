import React, { useState, useEffect, useCallback } from 'react';
import {
  Layers,
  ExternalLink,
  CheckCircle2,
  AlertTriangle,
  ShieldCheck,
} from 'lucide-react';
import { useI18n } from '../../i18n';
import { nativeBridge } from '../../services/nativeBridge';
import { DriverInfo } from '../../types';

export const DriverCenterView: React.FC = () => {
  const { t } = useI18n();
  const [drivers, setDrivers] = useState<DriverInfo[]>([]);
  const [isScanning, setIsScanning] = useState(false);
  const [scanError, setScanError] = useState<string | null>(null);

  const scanDrivers = useCallback(async () => {
    setIsScanning(true);
    setScanError(null);
    try {
      const result = await nativeBridge.queryInstalledDrivers();
      if (result) {
        setDrivers(result.drivers);
        if (result.scanError) setScanError(result.scanError);
        else if (result.drivers.length === 0) setScanError('O Windows não retornou drivers nesta consulta. Verifique se o serviço de Instrumentação de Gerenciamento do Windows está ativo e tente novamente.');
      } else setScanError('Conecte o agente Windows para consultar os drivers instalados.');
    } catch {
      setScanError('Não foi possível consultar os drivers nesta sessão.');
    } finally {
      setIsScanning(false);
    }
  }, []);

  useEffect(() => { void scanDrivers(); }, [scanDrivers]);

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Banner */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <Layers className="w-4 h-4" /> {t.driverCenter.subtitle}
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">{t.driverCenter.title}</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl leading-relaxed">
            {t.driverCenter.description}
          </p>
          <p className="text-[10px] text-slate-500 mt-1">A verificação confirma o que o Windows informa sobre o driver instalado; não significa que seja a versão mais recente disponível.</p>
        </div>

        <button onClick={() => void scanDrivers()} disabled={isScanning} className="flex items-center gap-2 text-xs text-purple-200 bg-[#141829] px-3 py-2 rounded-lg border border-purple-500/40 font-semibold disabled:opacity-50">
          <ShieldCheck className={`w-4 h-4 ${isScanning ? 'animate-pulse' : ''}`} />
          <span>{isScanning ? 'Lendo drivers...' : 'Escanear drivers instalados'}</span>
        </button>
      </div>

      {scanError && <div className="rounded-lg border border-amber-500/30 bg-amber-500/5 px-4 py-3 text-xs text-amber-200">{scanError}</div>}

      {/* Drivers List Table */}
      <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-4">
        <div className="flex items-center justify-between pb-2 border-b border-slate-800 text-xs">
          <span className="font-bold text-white uppercase tracking-wider">
            Dispositivos e drivers reportados pelo Windows
          </span>
          <span className="text-slate-400 font-mono">
            {isScanning ? 'Consultando Windows...' : `${drivers.length} itens no inventário`}
          </span>
        </div>

        <div className="divide-y divide-slate-800/80">
          {drivers.length === 0 && !isScanning ? <div className="py-8 text-center text-xs text-slate-500">Nenhum dispositivo ou driver foi retornado pelo Windows nesta consulta. Verifique o serviço WMI e tente novamente.</div> : null}
          {drivers.map((driver, idx) => {
            const isVerified = driver.status === 'Verificado via WMI';

            return (
              <div
                key={idx}
                className="py-4 flex flex-col md:flex-row items-start md:items-center justify-between gap-4 text-xs"
              >
                <div className="space-y-1">
                  <div className="flex items-center gap-2">
                    <span className="font-bold text-white text-sm">{driver.device}</span>
                    <span
                      className={`text-[10px] font-mono font-bold px-2 py-0.5 rounded ${
                        isVerified
                          ? 'bg-cyan-500/10 text-cyan-300 border border-cyan-500/30'
                          : 'bg-slate-500/10 text-slate-300 border border-slate-500/30'
                      }`}
                    >
                      {isVerified ? 'Verificado pelo Windows' : driver.status}
                    </span>
                  </div>
                  <div className="text-slate-400 text-[11px]">
                    Categoria: <span className="text-slate-200">{driver.category || 'Outros'}</span> · {t.hardwareMonitor.vendor} {driver.vendor} · {t.common.versionLabel}: <span className="text-slate-200 font-mono font-semibold">{driver.installedVersion}</span> ({driver.releaseDate}) · {driver.signatureStatus || 'Assinatura não informada'}
                  </div>
                </div>

                <div className="flex items-center gap-3 shrink-0">
                  <a
                    href={driver.officialUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="flex items-center gap-1.5 px-3.5 py-1.5 rounded-lg bg-[#141829] hover:bg-[#1b223d] border border-purple-500/40 text-purple-200 hover:text-white text-xs font-semibold transition-colors cursor-pointer"
                  >
                    <ExternalLink className="w-3.5 h-3.5 text-purple-400" />
                    <span>{t.driverCenter.officialPortal}</span>
                  </a>
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
};
