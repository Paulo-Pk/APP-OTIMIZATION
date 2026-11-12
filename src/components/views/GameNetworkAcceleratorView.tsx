import React, { useState } from 'react';
import {
  Globe,
  ShieldCheck,
  Activity,
  Server,
  Play,
  ChevronDown,
  ChevronUp,
  AlertCircle,
} from 'lucide-react';

interface TargetMeasurement {
  id: string;
  server: string;
  location: string;
  host: string;
  protocol?: string;
  pingMs: number | null;
  jitterMs: number | null;
  packetLossPercent: number;
  tcpHandshakeState?: string;
  samples: number[];
  isSuccess: boolean;
}

interface MeasurementResponse {
  measurementMethod?: string;
  tcpReachabilityPercent?: number;
  structuredDiagnostic?: {
    whatWasTested?: string;
    whatFailed?: string;
    evidenceFound?: string;
    probableCause?: string;
    recommendedSolution?: string;
    verificationSteps?: string;
  };
  results?: TargetMeasurement[];
}

export const GameNetworkAcceleratorView: React.FC = () => {
  const [isProbing, setIsProbing] = useState(false);
  const [showTechnicalDetails, setShowTechnicalDetails] = useState(false);
  const [results, setResults] = useState<TargetMeasurement[]>([]);
  const [summary, setSummary] = useState('Nenhuma medição executada nesta sessão.');
  const [diagnostic, setDiagnostic] = useState<MeasurementResponse['structuredDiagnostic']>();
  const [measurementMethod, setMeasurementMethod] = useState<string | null>(null);
  const [measuredAt, setMeasuredAt] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const handleEvaluateRoute = async () => {
    setIsProbing(true);
    setError(null);
    try {
      const response = await fetch('/api/network/measure-targets', { cache: 'no-store' });
      if (!response.ok) {
        throw new Error(`O servidor de medição respondeu HTTP ${response.status}.`);
      }

      const data = (await response.json()) as MeasurementResponse;
      if (!Array.isArray(data.results)) {
        throw new Error('A resposta não contém resultados de medição válidos.');
      }

      const measured = data.results;
      const successful = measured.filter((item) => item.isSuccess && item.pingMs !== null);
      setResults(measured);
      setDiagnostic(data.structuredDiagnostic);
      setMeasurementMethod(data.measurementMethod ?? 'Método não informado');
      setMeasuredAt(new Date().toLocaleString());

      if (successful.length > 0) {
        const average = successful.reduce((sum, item) => sum + (item.pingMs ?? 0), 0) / successful.length;
        setSummary(
          `${successful.length}/${measured.length} endpoints aceitaram conexão TCP. Média dos handshakes: ${average.toFixed(1)} ms. Isso não é ping ICMP nem medição do servidor do jogo.`
        );
      } else {
        setSummary('Nenhum endpoint aceitou o handshake TCP. Isso não comprova, isoladamente, perda de pacotes ICMP.');
      }
    } catch (err) {
      setResults([]);
      setDiagnostic(undefined);
      setSummary('Medição indisponível; nenhum resultado foi presumido.');
      setError(err instanceof Error ? err.message : 'Falha desconhecida ao executar a medição.');
    } finally {
      setIsProbing(false);
    }
  };

  return (
    <div className="space-y-5 animate-fade-in">
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <Globe className="w-4 h-4" /> Game Network Diagnostics
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">GAME NETWORK ACCELERATOR</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-2xl leading-relaxed">
            Executa medições TCP reais em endpoints de referência. Não altera rotas, não instala túneis e não promete redução de ping.
          </p>
        </div>

        <button
          onClick={handleEvaluateRoute}
          disabled={isProbing}
          className="flex items-center gap-2 px-4 py-2.5 rounded-lg bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white font-bold text-xs uppercase tracking-wider shadow-lg shadow-purple-600/30 transition-all cursor-pointer border border-purple-400/40 disabled:opacity-50"
        >
          <Play className="w-4 h-4 fill-current" />
          <span>{isProbing ? 'Medindo endpoints...' : 'Executar diagnóstico TCP'}</span>
        </button>
      </div>

      <div className="p-4 rounded-xl bg-[#0e121e] border border-amber-500/30 text-xs text-amber-200 leading-relaxed flex items-start gap-3">
        <ShieldCheck className="w-5 h-5 text-amber-400 shrink-0 mt-0.5" />
        <div>
          <strong className="text-amber-400">Limite importante da medição:</strong> os tempos abaixo são handshakes TCP executados pelo processo que hospeda a aplicação. Se o preview estiver em nuvem, a medição não representa a conexão deste PC. Handshake TCP também não equivale a ping ICMP ou latência UDP do jogo.
        </div>
      </div>

      {error && (
        <div className="p-4 rounded-xl bg-[#261316] border border-rose-500/60 text-xs text-rose-300 flex items-center gap-2.5">
          <AlertCircle className="w-5 h-5 text-rose-400 shrink-0" />
          <span>{error}</span>
        </div>
      )}

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-2">
          <div className="flex items-center gap-2 text-xs text-slate-400 font-bold uppercase">
            <Activity className="w-4 h-4 text-purple-400" /> Resultado da medição
          </div>
          <p className="text-sm text-emerald-300 font-semibold leading-relaxed">{summary}</p>
          {measuredAt && <p className="text-[10px] text-slate-500">Executado em: {measuredAt}</p>}
          {measurementMethod && <p className="text-[10px] text-slate-500">Método: {measurementMethod}</p>}
        </div>

        <div className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 space-y-2">
          <div className="flex items-center gap-2 text-xs text-slate-400 font-bold uppercase">
            <Server className="w-4 h-4 text-purple-400" /> Estado do WireGuard
          </div>
          <p className="text-xs text-slate-300 leading-relaxed">
            Esta interface web não está conectada às operações nativas do WireGuard. Nenhuma configuração foi auditada, nenhum serviço foi instalado/removido e nenhuma rota foi alterada por esta tela.
          </p>
          <p className="text-[10px] text-amber-300">
            A auditoria e o controle do túnel exigem integração autenticada com o agente Windows; não exibimos um falso status de segurança.
          </p>
        </div>
      </div>

      <div className="rounded-xl bg-[#0f121b] border border-slate-800 overflow-hidden">
        <button
          onClick={() => setShowTechnicalDetails((previous) => !previous)}
          className="w-full px-5 py-3.5 flex items-center justify-between text-xs font-bold text-purple-300 hover:bg-[#131724] transition-colors cursor-pointer"
        >
          <span>Ver endpoints, amostras e diagnóstico técnico</span>
          {showTechnicalDetails ? (
            <ChevronUp className="w-4 h-4 text-purple-400" />
          ) : (
            <ChevronDown className="w-4 h-4 text-purple-400" />
          )}
        </button>

        {showTechnicalDetails && (
          <div className="p-5 border-t border-slate-800 space-y-4">
            {results.length === 0 ? (
              <p className="text-xs text-slate-500">Ainda não há resultados reais nesta sessão.</p>
            ) : (
              <div className="space-y-3">
                {results.map((item) => (
                  <div key={item.id} className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800 space-y-1.5">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <strong className="text-xs text-white">{item.server}</strong>
                      <span className={`text-[10px] font-semibold ${item.isSuccess ? 'text-emerald-400' : 'text-amber-300'}`}>
                        {item.tcpHandshakeState ?? (item.isSuccess ? 'Connected' : 'Sem conexão')}
                      </span>
                    </div>
                    <div className="text-[11px] text-slate-400">{item.host} · {item.location} · {item.protocol ?? 'TCP'}</div>
                    <div className="flex flex-wrap gap-x-5 gap-y-1 text-[11px] font-mono">
                      <span className="text-cyan-300">Handshake: {item.pingMs === null ? 'N/M' : `${item.pingMs} ms`}</span>
                      <span className="text-purple-300">Jitter TCP: {item.jitterMs === null ? 'N/M' : `${item.jitterMs} ms`}</span>
                      <span className="text-slate-400">Falhas de conexão: {item.packetLossPercent}%</span>
                    </div>
                    <div className="text-[10px] text-slate-500">Amostras TCP: {item.samples.length ? item.samples.join(' · ') + ' ms' : 'Nenhum handshake concluído'}</div>
                  </div>
                ))}
              </div>
            )}

            {diagnostic && (
              <div className="p-3 rounded-lg bg-[#0a0c13] border border-slate-800 space-y-1 text-xs">
                <strong className="text-purple-300">Diagnóstico estruturado</strong>
                <p className="text-slate-300">Testado: {diagnostic.whatWasTested ?? 'N/D'}</p>
                <p className="text-slate-300">Falhas: {diagnostic.whatFailed ?? 'N/D'}</p>
                <p className="text-slate-300">Evidência: {diagnostic.evidenceFound ?? 'N/D'}</p>
                <p className="text-slate-300">Causa provável: {diagnostic.probableCause ?? 'N/D'}</p>
                <p className="text-slate-300">Recomendação: {diagnostic.recommendedSolution ?? 'N/D'}</p>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
};
