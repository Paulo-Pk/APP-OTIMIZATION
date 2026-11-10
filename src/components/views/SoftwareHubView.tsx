import React from 'react';
import {
  DownloadCloud,
  CheckCircle2,
  ExternalLink,
  Download,
  ShieldCheck,
} from 'lucide-react';
import { curatedSoftware } from '../../data/softwareHub';
import { useI18n } from '../../i18n';

export const SoftwareHubView: React.FC = () => {
  const { t } = useI18n();

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

      {/* Software Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {curatedSoftware.map((app) => (
          <div
            key={app.id}
            className="p-4 rounded-xl bg-[#0f121b] border border-slate-800 hover:border-slate-700 transition-all flex flex-col justify-between space-y-3"
          >
            <div className="space-y-1.5">
              <div className="flex items-center justify-between">
                <span className="text-[10px] font-bold text-purple-400 uppercase tracking-wider">
                  {app.category}
                </span>
                {app.isInstalled ? (
                  <span className="text-[10px] font-mono font-bold px-2 py-0.5 rounded bg-emerald-500/10 text-emerald-400 border border-emerald-500/30 flex items-center gap-1">
                    <CheckCircle2 className="w-3 h-3" /> {t.softwareHub.installed}
                  </span>
                ) : (
                  <span className="text-[10px] font-mono font-bold px-2 py-0.5 rounded bg-slate-800 text-slate-400">
                    {t.softwareHub.available}
                  </span>
                )}
              </div>

              <h3 className="font-bold text-sm text-white">{app.name}</h3>
              <p className="text-xs text-slate-400 leading-relaxed">
                {app.description}
              </p>

              <div className="text-[11px] font-mono text-slate-500 pt-1">
                Versão: {app.version}
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
      </div>
    </div>
  );
};
