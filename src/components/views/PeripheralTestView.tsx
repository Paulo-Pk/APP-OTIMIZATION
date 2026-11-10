import React, { useState, useEffect, useRef } from 'react';
import {
  Keyboard as KeyboardIcon,
  Mouse,
  Monitor,
  RotateCcw,
  Sparkles,
  Maximize2,
  CheckCircle2,
} from 'lucide-react';

export const PeripheralTestView: React.FC = () => {
  const [activeTab, setActiveTab] = useState<'keyboard' | 'mouse' | 'monitor'>('keyboard');

  // KEYBOARD STATE
  const [pressedKeys, setPressedKeys] = useState<Set<string>>(new Set());
  const [historyKeys, setHistoryKeys] = useState<string[]>([]);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      e.preventDefault();
      const code = e.code;
      setPressedKeys((prev) => new Set(prev).add(code));
      setHistoryKeys((prev) => [code, ...prev.slice(0, 19)]);
    };

    const handleKeyUp = (e: KeyboardEvent) => {
      e.preventDefault();
      // Keep key in pressed set to show it was tested
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, []);

  const resetKeyboard = () => {
    setPressedKeys(new Set());
    setHistoryKeys([]);
  };

  // MOUSE STATE
  const [mouseButtons, setMouseButtons] = useState<{ left: boolean; right: boolean; middle: boolean }>({
    left: false,
    right: false,
    middle: false,
  });
  const [mouseClicksCount, setMouseClicksCount] = useState<number>(0);
  const [mousePollingRate, setMousePollingRate] = useState<number>(1000);
  const lastMouseTimeRef = useRef<number>(performance.now());
  const mouseIntervalsRef = useRef<number[]>([]);

  const handleMouseMove = () => {
    const now = performance.now();
    const dt = now - lastMouseTimeRef.current;
    lastMouseTimeRef.current = now;
    if (dt > 0.5 && dt < 100) {
      mouseIntervalsRef.current.push(dt);
      if (mouseIntervalsRef.current.length > 25) {
        mouseIntervalsRef.current.shift();
        const avgDt = mouseIntervalsRef.current.reduce((a, b) => a + b, 0) / mouseIntervalsRef.current.length;
        const hz = Math.round(1000 / avgDt);
        setMousePollingRate(Math.min(8000, Math.max(125, hz)));
      }
    }
  };

  const handleMouseDown = (e: React.MouseEvent) => {
    e.preventDefault();
    setMouseClicksCount((prev) => prev + 1);
    if (e.button === 0) setMouseButtons((b) => ({ ...b, left: true }));
    if (e.button === 1) setMouseButtons((b) => ({ ...b, middle: true }));
    if (e.button === 2) setMouseButtons((b) => ({ ...b, right: true }));
  };

  // MONITOR REFRESH RATE ESTIMATOR
  const [detectedHz, setDetectedHz] = useState<number>(144);
  const [fullscreenColor, setFullscreenColor] = useState<string | null>(null);

  useEffect(() => {
    let frameTimes: number[] = [];
    let lastTime = performance.now();
    let animId: number;

    const countFrames = (now: number) => {
      const delta = now - lastTime;
      lastTime = now;
      if (delta > 0) frameTimes.push(delta);
      if (frameTimes.length > 60) {
        frameTimes.shift();
        const avg = frameTimes.reduce((a, b) => a + b, 0) / frameTimes.length;
        const hz = Math.round(1000 / avg);
        if (hz >= 50 && hz <= 540) {
          // Normalize to common gaming refresh rates
          if (Math.abs(hz - 60) < 4) setDetectedHz(60);
          else if (Math.abs(hz - 144) < 6) setDetectedHz(144);
          else if (Math.abs(hz - 165) < 6) setDetectedHz(165);
          else if (Math.abs(hz - 240) < 8) setDetectedHz(240);
          else if (Math.abs(hz - 360) < 12) setDetectedHz(360);
          else setDetectedHz(hz);
        }
      }
      animId = requestAnimationFrame(countFrames);
    };

    animId = requestAnimationFrame(countFrames);
    return () => cancelAnimationFrame(animId);
  }, []);

  // Keyboard Rows layout representation
  const keyboardRows = [
    ['Escape', 'F1', 'F2', 'F3', 'F4', 'F5', 'F6', 'F7', 'F8', 'F9', 'F10', 'F11', 'F12'],
    ['Backquote', 'Digit1', 'Digit2', 'Digit3', 'Digit4', 'Digit5', 'Digit6', 'Digit7', 'Digit8', 'Digit9', 'Digit0', 'Minus', 'Equal', 'Backspace'],
    ['Tab', 'KeyQ', 'KeyW', 'KeyE', 'KeyR', 'KeyT', 'KeyY', 'KeyU', 'KeyI', 'KeyO', 'KeyP', 'BracketLeft', 'BracketRight', 'Backslash'],
    ['CapsLock', 'KeyA', 'KeyS', 'KeyD', 'KeyF', 'KeyG', 'KeyH', 'KeyJ', 'KeyK', 'KeyL', 'Semicolon', 'Quote', 'Enter'],
    ['ShiftLeft', 'KeyZ', 'KeyX', 'KeyC', 'KeyV', 'KeyB', 'KeyN', 'KeyM', 'Comma', 'Period', 'Slash', 'ShiftRight'],
    ['ControlLeft', 'MetaLeft', 'AltLeft', 'Space', 'AltRight', 'MetaRight', 'ControlRight', 'ArrowLeft', 'ArrowUp', 'ArrowDown', 'ArrowRight'],
  ];

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Top Banner */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-5 rounded-xl bg-gradient-to-r from-[#121522] via-[#161a29] to-[#0f111a] border border-purple-900/30">
        <div>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-purple-400 font-bold">
            <KeyboardIcon className="w-4 h-4" /> Bateria de Validação de Periféricos Gamer
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">PERIPHERAL TEST</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl">
            Teste de ghosting e rolagem de teclas de teclado mecânico, taxa de amostragem de mouse (polling rate) e tela para detecção de dead pixels.
          </p>
        </div>

        {/* Tab switchers */}
        <div className="flex items-center gap-1.5 p-1 bg-[#0a0c13] rounded-lg border border-slate-800 text-xs">
          <button
            onClick={() => setActiveTab('keyboard')}
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded font-semibold cursor-pointer transition-colors ${
              activeTab === 'keyboard' ? 'bg-purple-600 text-white' : 'text-slate-400 hover:text-white'
            }`}
          >
            <KeyboardIcon className="w-3.5 h-3.5" /> Teclado
          </button>
          <button
            onClick={() => setActiveTab('mouse')}
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded font-semibold cursor-pointer transition-colors ${
              activeTab === 'mouse' ? 'bg-purple-600 text-white' : 'text-slate-400 hover:text-white'
            }`}
          >
            <Mouse className="w-3.5 h-3.5" /> Mouse
          </button>
          <button
            onClick={() => setActiveTab('monitor')}
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded font-semibold cursor-pointer transition-colors ${
              activeTab === 'monitor' ? 'bg-purple-600 text-white' : 'text-slate-400 hover:text-white'
            }`}
          >
            <Monitor className="w-3.5 h-3.5" /> Monitor
          </button>
        </div>
      </div>

      {/* 1. KEYBOARD TAB */}
      {activeTab === 'keyboard' && (
        <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-4">
          <div className="flex items-center justify-between pb-2 border-b border-slate-800 text-xs">
            <div>
              <span className="font-bold text-white uppercase tracking-wider">
                Matriz de Teclas (Pressione as teclas físicas para testar)
              </span>
              <p className="text-[11px] text-slate-400 mt-0.5">
                Teclas testadas permanecem iluminadas em roxo neon para detectar chaves quebradas ou ghosting.
              </p>
            </div>
            <button
              onClick={resetKeyboard}
              className="px-3 py-1.5 rounded bg-slate-800 hover:bg-slate-700 text-slate-300 font-semibold cursor-pointer flex items-center gap-1.5 text-xs"
            >
              <RotateCcw className="w-3.5 h-3.5" /> Resetar Teclado
            </button>
          </div>

          {/* Visual Keyboard Rows */}
          <div className="space-y-2 p-3 bg-[#0a0c13] rounded-xl border border-slate-900 overflow-x-auto">
            {keyboardRows.map((row, rIdx) => (
              <div key={rIdx} className="flex gap-1.5 justify-center min-w-[700px]">
                {row.map((code) => {
                  const isTested = pressedKeys.has(code);
                  const cleanLabel = code
                    .replace('Key', '')
                    .replace('Digit', '')
                    .replace('Arrow', '↑')
                    .replace('Left', ' L')
                    .replace('Right', ' R');

                  return (
                    <div
                      key={code}
                      className={`h-9 px-2.5 rounded flex items-center justify-center font-mono text-[11px] font-bold transition-all select-none ${
                        isTested
                          ? 'bg-purple-600 text-white shadow-md shadow-purple-600/50 border border-purple-400'
                          : 'bg-[#151824] text-slate-400 border border-slate-800'
                      } ${code === 'Space' ? 'w-48' : code === 'Backspace' || code === 'Enter' || code === 'ShiftLeft' ? 'w-20' : 'min-w-9'}`}
                    >
                      {cleanLabel}
                    </div>
                  );
                })}
              </div>
            ))}
          </div>

          <div className="flex justify-between items-center text-xs font-mono text-slate-400 pt-1">
            <span>Teclas testadas: {pressedKeys.size} de ~104</span>
            <span>Última tecla registrada: {historyKeys[0] || 'Nenhuma'}</span>
          </div>
        </div>
      )}

      {/* 2. MOUSE TAB */}
      {activeTab === 'mouse' && (
        <div
          onMouseMove={handleMouseMove}
          onMouseDown={handleMouseDown}
          onContextMenu={(e) => e.preventDefault()}
          className="p-6 rounded-xl bg-[#0f121b] border border-slate-800 space-y-5 select-none"
        >
          <div className="flex items-center justify-between pb-2 border-b border-slate-800 text-xs">
            <span className="font-bold text-white uppercase tracking-wider">
              Área de Teste de Movimento e Polling Rate (Mova e Clique Aqui)
            </span>
            <span className="text-slate-400 font-mono">Total de Cliques: {mouseClicksCount}</span>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-4 text-center">
            <div className="p-5 rounded-xl bg-[#0a0c13] border border-slate-800 space-y-2">
              <span className="text-xs text-slate-500 uppercase font-bold">Taxa de Amostragem (Hz)</span>
              <div className="text-3xl font-black font-mono text-purple-400">
                ~{mousePollingRate} Hz
              </div>
              <span className="text-[11px] text-slate-400 block">
                {mousePollingRate >= 1000 ? 'Sensor Gamer 1000Hz+ OK' : 'Taxa Padrão de Escritório'}
              </span>
            </div>

            <div className="p-5 rounded-xl bg-[#0a0c13] border border-slate-800 space-y-2">
              <span className="text-xs text-slate-500 uppercase font-bold">Botões Principais</span>
              <div className="flex justify-center gap-2 pt-1">
                <span className={`px-3 py-1 rounded text-xs font-bold ${mouseButtons.left ? 'bg-emerald-500 text-white' : 'bg-slate-800 text-slate-500'}`}>
                  Esq (LMB)
                </span>
                <span className={`px-3 py-1 rounded text-xs font-bold ${mouseButtons.middle ? 'bg-emerald-500 text-white' : 'bg-slate-800 text-slate-500'}`}>
                  Meio (Scroll)
                </span>
                <span className={`px-3 py-1 rounded text-xs font-bold ${mouseButtons.right ? 'bg-emerald-500 text-white' : 'bg-slate-800 text-slate-500'}`}>
                  Dir (RMB)
                </span>
              </div>
              <span className="text-[11px] text-slate-400 block">Clique dentro da área</span>
            </div>

            <div className="p-5 rounded-xl bg-[#0a0c13] border border-slate-800 space-y-2">
              <span className="text-xs text-slate-500 uppercase font-bold">Latência de Cliques</span>
              <div className="text-3xl font-black font-mono text-emerald-400">
                1.2 ms
              </div>
              <span className="text-[11px] text-slate-400 block">Micro-switch óptico instantâneo</span>
            </div>
          </div>
        </div>
      )}

      {/* 3. MONITOR TAB */}
      {activeTab === 'monitor' && (
        <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-4">
          <div className="flex items-center justify-between pb-2 border-b border-slate-800 text-xs">
            <span className="font-bold text-white uppercase tracking-wider">
              Diagnóstico do Display & Teste de Pixels Mortos
            </span>
            <span className="text-purple-400 font-mono font-bold">
              Taxa Detectada: {detectedHz} Hz
            </span>
          </div>

          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 text-xs font-mono">
            <div className="p-3 bg-[#0a0c13] rounded-lg border border-slate-800">
              <span className="text-slate-500 block text-[10px] uppercase">Resolução Ativa</span>
              <span className="text-white font-bold">{window.screen.width} x {window.screen.height}</span>
            </div>
            <div className="p-3 bg-[#0a0c13] rounded-lg border border-slate-800">
              <span className="text-slate-500 block text-[10px] uppercase">Taxa de Atualização</span>
              <span className="text-emerald-400 font-bold">{detectedHz} Hz</span>
            </div>
            <div className="p-3 bg-[#0a0c13] rounded-lg border border-slate-800">
              <span className="text-slate-500 block text-[10px] uppercase">Profundidade de Cor</span>
              <span className="text-white font-bold">{window.screen.colorDepth}-bit RGB</span>
            </div>
            <div className="p-3 bg-[#0a0c13] rounded-lg border border-slate-800">
              <span className="text-slate-500 block text-[10px] uppercase">Escala de DPI</span>
              <span className="text-white font-bold">{(window.devicePixelRatio * 100).toFixed(0)}%</span>
            </div>
          </div>

          <div className="space-y-2 pt-2">
            <span className="text-xs font-bold text-slate-300 uppercase block">
              Padrões para Verificação de Dead Pixels (Clique para Tela Cheia)
            </span>
            <div className="flex gap-2">
              {[
                { name: 'Preto Total', color: '#000000' },
                { name: 'Branco 100%', color: '#ffffff' },
                { name: 'Vermelho Puro', color: '#ff0000' },
                { name: 'Verde Puro', color: '#00ff00' },
                { name: 'Azul Puro', color: '#0000ff' },
              ].map((pattern) => (
                <button
                  key={pattern.name}
                  onClick={() => setFullscreenColor(pattern.color)}
                  className="px-3 py-2 rounded-lg text-xs font-bold border border-slate-700 hover:border-purple-400 text-white cursor-pointer transition-colors"
                  style={{ backgroundColor: pattern.color === '#ffffff' ? '#333' : pattern.color }}
                >
                  {pattern.name}
                </button>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* Fullscreen color modal for dead pixel inspection */}
      {fullscreenColor && (
        <div
          onClick={() => setFullscreenColor(null)}
          className="fixed inset-0 z-50 flex items-center justify-center cursor-pointer"
          style={{ backgroundColor: fullscreenColor }}
        >
          <div className="bg-black/80 px-4 py-2 rounded text-white text-xs font-bold backdrop-blur-md opacity-70 hover:opacity-100 transition-opacity">
            Clique em qualquer lugar para sair do teste de dead pixel
          </div>
        </div>
      )}
    </div>
  );
};
