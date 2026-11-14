import React, { useEffect, useState } from 'react';
import {
  DownloadCloud,
  ExternalLink,
  Download,
  ShieldCheck,
} from 'lucide-react';
import { curatedSoftware } from '../../data/softwareHub';
import { useI18n } from '../../i18n';
import { nativeBridge } from '../../services/nativeBridge';

const officialBrandIcons: Record<string, string> = {
  'sw-discord': 'https://cdn.simpleicons.org/discord/5865F2',
  'sw-steam': 'https://cdn.simpleicons.org/steam/FFFFFF',
  'sw-epic': 'https://cdn.simpleicons.org/epicgames/FFFFFF',
  'sw-gog': 'https://cdn.simpleicons.org/gogdotcom/FFFFFF',
  'sw-ea': 'https://cdn.simpleicons.org/ea/FFFFFF',
  'sw-ubisoft': 'https://cdn.simpleicons.org/ubisoft/FFFFFF',
  'sw-xbox': 'https://cdn.simpleicons.org/xbox/107C10',
  'sw-opera-gx': 'https://cdn.simpleicons.org/operagx/FA1E4E',
};

const officialWingetIds: Record<string, string> = {
  'sw-discord': 'Discord.Discord',
  'sw-steam': 'Valve.Steam',
  'sw-epic': 'EpicGames.EpicGamesLauncher',
  'sw-ea': 'ElectronicArts.EADesktop',
  'sw-ubisoft': 'Ubisoft.Connect',
  'sw-gog': 'GOG.Galaxy',
  'sw-xbox': '9MV0B5HZVK9Z',
  'sw-opera-gx': 'Opera.OperaGX',
  'sw-vlc': 'VideoLAN.VLC',
  'sw-firefox': 'Mozilla.Firefox',
  'sw-chrome': 'Google.Chrome',
  'sw-obs': 'OBSProject.OBSStudio',
  'sw-cpuz': 'CPUID.CPU-Z',
  'sw-gpuz': 'TechPowerUp.GPU-Z',
  'sw-hwinfo': 'REALiX.HWiNFO',
};

export const SoftwareHubView: React.FC = () => {
  const { t } = useI18n();
  const [search, setSearch] = useState('');
  const [agentConnected, setAgentConnected] = useState(nativeBridge.getStatus().isConnected);
  const [installingAppId, setInstallingAppId] = useState<string | null>(null);
  const [installStatus, setInstallStatus] = useState<string | null>(null);

  useEffect(() => nativeBridge.subscribe((status) => setAgentConnected(status.isConnected)), []);

  const handleInstall = async (app: (typeof curatedSoftware)[number]) => {
    const packageId = officialWingetIds[app.id];
    if (!packageId) return;
    const confirmed = window.confirm(`Deseja instalar ${app.name} pelo catálogo oficial do Windows (WinGet)?`);
    if (!confirmed) return;

    setInstallingAppId(app.id);
    setInstallStatus(`Preparando a instalação de ${app.name}...`);
    try {
      const result = await nativeBridge.installVerifiedSoftware(packageId, true);
      if (!result) {
        setInstallStatus('Agente Windows desconectado. Use o botão de download oficial para instalar este aplicativo.');
      } else if (result.success) {
        setInstallStatus(`${app.name} foi instalado com sucesso pelo WinGet.`);
      } else {
        const detail = result.message || result.error || result.status || 'O WinGet não concluiu a instalação.';
        setInstallStatus(`Não foi possível instalar ${app.name}: ${detail}`);
      }
    } catch (error) {
      setInstallStatus(error instanceof Error ? error.message : `Falha ao instalar ${app.name}.`);
    } finally {
      setInstallingAppId(null);
    }
  };
  const filteredSoftware = curatedSoftware.filter((app) => (app.name + ' ' + app.category + ' ' + app.description).toLowerCase().includes(search.toLowerCase()));

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Banner */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <DownloadCloud className="w-4 h-4" /> {t.softwareHub.subtitle}
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">{t.softwareHub.title}</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl leading-relaxed">
            {t.softwareHub.description}
          </p>
        </div>

        <div className="flex items-center gap-2 text-xs text-purple-300 bg-[#141829] px-3.5 py-1.5 rounded-lg border border-purple-500/30 font-semibold">
          <ShieldCheck className="w-4 h-4 text-purple-400" />
          <span>{t.softwareHub.verifiedNotice}</span>
        </div>
      </div>

      <div className="flex items-center gap-3 p-3 rounded-xl bg-[#0f121b] border border-slate-800">
        <DownloadCloud className="w-4 h-4 text-purple-400" />
        <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Buscar aplicativo ou categoria..." className="w-full bg-transparent text-sm text-white outline-none placeholder:text-slate-500" />
        <span className="text-[11px] text-slate-500 whitespace-nowrap">{filteredSoftware.length} aplicativos</span>
      </div>

      {installStatus && (
        <div role="status" className="rounded-lg border border-purple-500/30 bg-purple-500/5 px-4 py-3 text-xs text-purple-100">
          {installStatus}
        </div>
      )}

      {/* Software Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-3">
        {filteredSoftware.map((app) => (
          <div
            key={app.id}
            className="p-3.5 rounded-xl bg-[#0f121b] border border-slate-800 hover:border-slate-700 transition-all flex flex-col justify-between space-y-2.5"
          >
            <div className="space-y-1.5">
              <div className="flex items-center justify-between">
                <span className="text-[10px] font-bold text-purple-400 uppercase tracking-wider">
                  {app.category}
                </span>
                
              </div>

              <div className="flex items-center gap-3"><img src={officialBrandIcons[app.id] || ('https://www.google.com/s2/favicons?domain=' + new URL(app.officialSite).hostname + '&sz=64')} alt={'Ícone do ' + app.name} className="w-9 h-9 rounded-lg bg-[#171C2A] border border-slate-700/70 p-1.5 object-contain" loading="lazy" /><h3 className="font-bold text-sm text-white">{app.name}</h3></div>
              <p className="text-xs text-slate-400 leading-relaxed line-clamp-2">
                {app.description}
              </p>

              <div className="text-[11px] font-mono text-slate-500 pt-1">
                {t.common.versionLabel}: {app.version}
              </div>
            </div>

            <div className="flex items-center justify-between pt-2 border-t border-slate-800/60">
              <a
                href={app.officialSite}
                target="_blank"
                rel="noopener noreferrer"
                className="text-xs text-slate-400 hover:text-white flex items-center gap-1 transition-colors"
              >
                <span>{t.softwareHub.officialSite}</span>
                <ExternalLink className="w-3 h-3" />
              </a>

              {officialWingetIds[app.id] && agentConnected ? (
                <button
                  type="button"
                  onClick={() => void handleInstall(app)}
                  disabled={installingAppId !== null}
                  className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 disabled:opacity-50 text-white text-xs font-semibold cursor-pointer transition-all border border-purple-400/30"
                >
                  <Download className="w-3.5 h-3.5" />
                  <span>{installingAppId === app.id ? 'Instalando...' : 'Instalar'}</span>
                </button>
              ) : (
                <a
                  href={app.installerUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white text-xs font-semibold cursor-pointer transition-all border border-purple-400/30"
                >
                  <Download className="w-3.5 h-3.5" />
                  <span>{t.softwareHub.officialDownload}</span>
                </a>
              )}
            </div>
          </div>
        ))}
        {filteredSoftware.length === 0 && <div className="col-span-full py-8 text-center text-xs text-slate-500">Nenhum aplicativo corresponde à busca.</div>}
      </div>
    </div>
  );
};
