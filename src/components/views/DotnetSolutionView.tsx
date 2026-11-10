import React, { useState } from 'react';
import {
  Code2,
  Download,
  Copy,
  Check,
  Folder,
  FileCode,
  Layers,
  Terminal,
  FileText,
} from 'lucide-react';
import { dotnetSolutionFiles } from '../../data/solutionFiles';
import { DotnetProjectFile } from '../../types';
import { useI18n } from '../../i18n';

interface DotnetSolutionViewProps {
  onDownloadSolution: () => void;
}

export const DotnetSolutionView: React.FC<DotnetSolutionViewProps> = ({
  onDownloadSolution,
}) => {
  const { t } = useI18n();
  const [selectedFile, setSelectedFile] = useState<DotnetProjectFile>(dotnetSolutionFiles[0]);
  const [copied, setCopied] = useState<boolean>(false);

  const handleCopy = () => {
    navigator.clipboard.writeText(selectedFile.content);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Banner */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <Code2 className="w-4 h-4" /> {t.dotnetSolution.subtitle}
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">
            {t.dotnetSolution.title}
          </h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl leading-relaxed">
            {t.dotnetSolution.description}
          </p>
        </div>

        <button
          onClick={onDownloadSolution}
          className="flex items-center gap-2 px-5 py-2.5 rounded-lg bg-gradient-to-r from-purple-600 via-indigo-600 to-violet-600 hover:from-purple-500 hover:to-violet-500 text-white font-bold text-xs uppercase tracking-wider shadow-lg shadow-purple-600/40 transition-all cursor-pointer border border-purple-400/40 active:scale-95"
        >
          <Download className="w-4 h-4" />
          <span>{t.dotnetSolution.downloadZip}</span>
        </button>
      </div>

      {/* Code Explorer Layout */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4 h-[650px]">
        {/* Solution Tree */}
        <div className="bg-[#0f121b] border border-slate-800 rounded-xl p-3 flex flex-col justify-between overflow-hidden">
          <div className="space-y-1 overflow-y-auto pr-1">
            <div className="px-2 py-1 text-[11px] font-bold text-purple-400 uppercase tracking-wider flex items-center gap-1.5">
              <Folder className="w-3.5 h-3.5" />
              <span>{t.dotnetSolution.projectsLabel}</span>
            </div>

            <div className="space-y-0.5 pt-1">
              {dotnetSolutionFiles.map((file) => {
                const isSelected = selectedFile.path === file.path;
                return (
                  <button
                    key={file.path}
                    onClick={() => setSelectedFile(file)}
                    className={`w-full text-left px-2.5 py-1.5 rounded-lg text-xs font-mono transition-all flex items-center justify-between cursor-pointer ${
                      isSelected
                        ? 'bg-purple-600/20 text-purple-200 border border-purple-500/40 font-bold'
                        : 'text-slate-400 hover:text-slate-200 hover:bg-[#151926]'
                    }`}
                  >
                    <div className="flex items-center gap-2 truncate">
                      <FileCode className={`w-3.5 h-3.5 shrink-0 ${isSelected ? 'text-purple-400' : 'text-slate-500'}`} />
                      <span className="truncate">{file.name}</span>
                    </div>
                    <span className="text-[10px] text-slate-500 uppercase shrink-0">
                      {file.language}
                    </span>
                  </button>
                );
              })}
            </div>
          </div>

          <div className="p-3 mt-2 bg-[#0a0c13] rounded-lg border border-slate-800/80 text-[11px] text-slate-400 space-y-1">
            <div className="font-bold text-white">{t.dotnetSolution.howToBuild}</div>
            <div className="font-mono text-[10px] text-purple-300">
              dotnet build PCControlUltimate.sln -c Release
            </div>
          </div>
        </div>

        {/* Code Content Viewer */}
        <div className="lg:col-span-2 bg-[#0a0c13] border border-slate-800 rounded-xl flex flex-col justify-between overflow-hidden">
          {/* File Header Bar */}
          <div className="p-3 bg-[#0d1017] border-b border-slate-800 flex items-center justify-between">
            <div className="space-y-0.5">
              <div className="flex items-center gap-2">
                <span className="text-xs font-mono font-bold text-white">
                  {selectedFile.path}
                </span>
                <span className="text-[10px] px-1.5 py-0.2 rounded bg-purple-500/20 text-purple-300 font-mono">
                  {selectedFile.language.toUpperCase()}
                </span>
              </div>
              <p className="text-[11px] text-slate-400">{selectedFile.description}</p>
            </div>

            <button
              onClick={handleCopy}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-[#141829] hover:bg-[#1b223d] border border-slate-700 text-slate-300 hover:text-white text-xs font-semibold cursor-pointer transition-colors"
            >
              {copied ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
              <span>{copied ? t.dotnetSolution.copied : t.dotnetSolution.copyCode}</span>
            </button>
          </div>

          {/* Syntax Code Display */}
          <div className="flex-1 p-4 overflow-auto font-mono text-xs text-slate-300 leading-relaxed bg-[#08090f]">
            <pre className="whitespace-pre">{selectedFile.content}</pre>
          </div>

          {/* File Footer */}
          <div className="p-2.5 bg-[#0d1017] border-t border-slate-800 text-[11px] text-slate-500 font-mono flex items-center justify-between">
            <span>{selectedFile.content.split('\n').length} {t.dotnetSolution.lines}</span>
            <span>{t.dotnetSolution.readyForVs}</span>
          </div>
        </div>
      </div>
    </div>
  );
};
