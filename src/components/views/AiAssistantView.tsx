import React, { useState } from 'react';
import {
  Brain,
  Send,
  Sparkles,
  Bot,
  User,
  ShieldCheck,
  Zap,
  Flame,
  HelpCircle,
  AlertCircle,
} from 'lucide-react';
import { HardwareTelemetry } from '../../types';
import { useI18n } from '../../i18n';

interface Message {
  role: 'assistant' | 'user';
  text: string;
  timestamp: string;
}

interface AiAssistantViewProps {
  telemetry: HardwareTelemetry;
}

export const AiAssistantView: React.FC<AiAssistantViewProps> = ({
  telemetry,
}) => {
  const { t } = useI18n();

  const [messages, setMessages] = useState<Message[]>([
    {
      role: 'assistant',
      text: t.aiAssistant.initialGreeting,
      timestamp: 'Agora',
    },
  ]);

  const [input, setInput] = useState<string>('');
  const [isLoading, setIsLoading] = useState<boolean>(false);

  const quickPrompts = [
    t.aiAssistant.quickPrompt1,
    t.aiAssistant.quickPrompt2,
    t.aiAssistant.quickPrompt3,
    t.aiAssistant.quickPrompt4,
  ];

  const handleSendMessage = async (customText?: string) => {
    const textToSend = customText || input;
    if (!textToSend.trim() || isLoading) return;

    const userMsg: Message = {
      role: 'user',
      text: textToSend,
      timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
    };

    setMessages((prev) => [...prev, userMsg]);
    if (!customText) setInput('');
    setIsLoading(true);

    try {
      const response = await fetch('/api/ai/diagnose', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          hardwareData: telemetry,
          userQuestion: textToSend,
          diagnosticLogs: [
            { component: 'GPU', deltaT: telemetry.gpu.hotspot - telemetry.gpu.temperature },
            { component: 'CPU', load: telemetry.cpu.load, temp: telemetry.cpu.temperature },
          ],
        }),
      });

      const data = await response.json();
      const replyText =
        data.response || 'Não foi possível gerar uma resposta do assistente no momento.';

      const assistantMsg: Message = {
        role: 'assistant',
        text: replyText,
        timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
      };

      setMessages((prev) => [...prev, assistantMsg]);
    } catch (err) {
      const errorMsg: Message = {
        role: 'assistant',
        text: 'Erro de comunicação local com o serviço de IA. Verifique sua conexão ou utilize o modo offline.',
        timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
      };
      setMessages((prev) => [...prev, errorMsg]);
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="space-y-4 animate-fade-in flex flex-col h-[calc(100vh-140px)]">
      {/* Top Banner */}
      <div className="p-4 rounded-xl bg-gradient-to-r from-[#171228] via-[#1a1430] to-[#0f111a] border border-purple-900/40 flex items-center justify-between shrink-0">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <Brain className="w-4 h-4" /> {t.aiAssistant.subtitle}
          </div>
          <h1 className="text-lg font-black text-white mt-0.5">
            {t.aiAssistant.title}
          </h1>
        </div>

        <div className="flex items-center gap-2 text-xs text-purple-300 font-mono bg-[#0c0e17] px-3 py-1.5 rounded-lg border border-purple-900/50">
          <Sparkles className="w-3.5 h-3.5 text-purple-400" />
          <span>{t.aiAssistant.engineNotice}</span>
        </div>
      </div>

      {/* Chat Messages Log */}
      <div className="flex-1 bg-[#0a0c13] rounded-xl border border-slate-800/80 p-4 overflow-y-auto space-y-4">
        {messages.map((msg, idx) => {
          const isAssistant = msg.role === 'assistant';
          return (
            <div
              key={idx}
              className={`flex gap-3 max-w-3xl ${
                isAssistant ? '' : 'ml-auto flex-row-reverse'
              }`}
            >
              <div
                className={`w-7 h-7 rounded-lg flex items-center justify-center shrink-0 text-xs font-bold ${
                  isAssistant
                    ? 'bg-gradient-to-br from-purple-600 to-indigo-700 text-white shadow-sm'
                    : 'bg-slate-700 text-slate-200'
                }`}
              >
                {isAssistant ? <Bot className="w-4 h-4" /> : <User className="w-4 h-4" />}
              </div>

              <div className="space-y-1">
                <div
                  className={`p-3.5 rounded-xl text-xs leading-relaxed ${
                    isAssistant
                      ? 'bg-[#121624] text-slate-200 border border-purple-900/30'
                      : 'bg-purple-600 text-white'
                  }`}
                >
                  <div className="whitespace-pre-wrap">{msg.text}</div>
                </div>
                <div
                  className={`text-[10px] text-slate-500 font-mono px-1 ${
                    isAssistant ? '' : 'text-right'
                  }`}
                >
                  {msg.timestamp}
                </div>
              </div>
            </div>
          );
        })}

        {isLoading && (
          <div className="flex gap-3 max-w-md">
            <div className="w-7 h-7 rounded-lg bg-purple-600/30 flex items-center justify-center text-purple-400 shrink-0">
              <Sparkles className="w-4 h-4 animate-spin" />
            </div>
            <div className="p-3 bg-[#121624] rounded-xl text-xs text-purple-300 font-mono flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-purple-400 animate-ping" />
              <span>{t.aiAssistant.analyzingNotice}</span>
            </div>
          </div>
        )}
      </div>

      {/* Suggested Quick Prompts */}
      <div className="flex items-center gap-2 overflow-x-auto pb-1 shrink-0">
        {quickPrompts.map((qp, i) => (
          <button
            key={i}
            onClick={() => handleSendMessage(qp)}
            disabled={isLoading}
            className="px-3 py-1.5 rounded-lg bg-[#101320] hover:bg-[#181d30] border border-slate-800 text-[11px] text-slate-300 hover:text-white whitespace-nowrap cursor-pointer transition-colors"
          >
            {qp}
          </button>
        ))}
      </div>

      {/* Message Input Box */}
      <div className="flex gap-2 shrink-0">
        <input
          type="text"
          placeholder={t.aiAssistant.inputPlaceholder}
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') handleSendMessage();
          }}
          disabled={isLoading}
          className="flex-1 bg-[#0a0c13] border border-slate-800 rounded-xl px-4 py-2.5 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-purple-500"
        />

        <button
          onClick={() => handleSendMessage()}
          disabled={isLoading || !input.trim()}
          className="px-5 py-2.5 rounded-xl bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white font-bold text-xs uppercase tracking-wider shadow-lg shadow-purple-600/30 transition-all cursor-pointer border border-purple-400/40 disabled:opacity-50 flex items-center gap-2"
        >
          <Send className="w-3.5 h-3.5" />
          <span>{t.aiAssistant.sendButton}</span>
        </button>
      </div>
    </div>
  );
};
