import React, { useState } from 'react';
import {
  DownloadCloud,
  ExternalLink,
  Download,
  ShieldCheck,
} from 'lucide-react';
import { curatedSoftware } from '../../data/softwareHub';
import { useI18n } from '../../i18n';

const officialBrandIcons: Record<string, string> = {
  'sw-discord': 'https://cdn.simpleicons.org/discord/5865F2',
  'sw-steam': 'https://cdn.simpleicons.org/steam/FFFFFF',
  'sw-epic': 'https://cdn.simpleicons.org/epicgames/FFFFFF',
  'sw-gog': 'https://cdn.simpleicons.org/gogdotcom/FFFFFF',
  'sw-xbox': 'https://cdn.simpleicons.org/xbox/107C10',
  'sw-opera-gx': 'https://cdn.simpleicons.org/operagx/FA1E4E',
};

export const SoftwareHubView: React.FC = () => {
  const { t } = useI18n();
  const [search, setSearch] = useState('');
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

              <a
                href={app.installerUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white text-xs font-semibold cursor-pointer transition-all border border-purple-400/30"
              >
                <Download className="w-3.5 h-3.5" />
                <span>{t.softwareHub.officialDownload}</span>
              </a>
            </div>
          </div>
        ))}
        {filteredSoftware.length === 0 && <div className="col-span-full py-8 text-center text-xs text-slate-500">Nenhum aplicativo corresponde à busca.</div>}
      </div>
    </div>
  );
};
