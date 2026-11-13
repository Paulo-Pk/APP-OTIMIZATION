import React, { useState, useEffect, useRef } from 'react';
import {
  Keyboard as KeyboardIcon,
  Mouse,
  Monitor,
  RotateCcw,
  Sparkles,
  Maximize2,
  CheckCircle2,
  AlertCircle,
  Info,
} from 'lucide-react';
import { useI18n } from '../../i18n';
import { nativeBridge } from '../../services/nativeBridge';

export const PeripheralTestView: React.FC = () => {
  const { t } = useI18n();
  const [activeTab, setActiveTab] = useState<'keyboard' | 'mouse' | 'monitor'>('keyboard');

  // KEYBOARD STATE
  const [pressedKeys, setPressedKeys] = useState<Set<string>>(new Set());
  const [historyKeys, setHistoryKeys] = useState<string[]>([]);

  const handleKeyboardKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    e.preventDefault();
    const code = e.code;
    setPressedKeys((prev) => new Set(prev).add(code));
    setHistoryKeys((prev) => [code, ...prev.slice(0, 19)]);
  };

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
  const [mousePollingRate, setMousePollingRate] = useState<number>(0);
  const [mouseMoveCount, setMouseMoveCount] = useState<number>(0);
  const [mouseWheelCount, setMouseWheelCount] = useState<number>(0);
  const [clickDurationMs, setClickDurationMs] = useState<number | null>(null);

  const lastMouseTimeRef = useRef<number>(performance.now());
  const mouseIntervalsRef = useRef<number[]>([]);
  const mouseDownTimestampRef = useRef<number | null>(null);

  const handleMouseMove = () => {
    const now = performance.now();
    setMouseMoveCount((count) => count + 1);
    const dt = now - lastMouseTimeRef.current;
    lastMouseTimeRef.current = now;
    if (dt > 0.5 && dt < 100) {
      mouseIntervalsRef.current.push(dt);
      if (mouseIntervalsRef.current.length > 25) {
        mouseIntervalsRef.current.shift();
        const avgDt = mouseIntervalsRef.current.reduce((a, b) => a + b, 0) / mouseIntervalsRef.current.length;
        const hz = Math.round(1000 / avgDt);
        setMousePollingRate(hz);
      }
    }
  };

  const handleMouseDown = (e: React.MouseEvent) => {
    e.preventDefault();
    mouseDownTimestampRef.current = performance.now();
    setMouseClicksCount((prev) => prev + 1);
    if (e.button === 0) setMouseButtons((b) => ({ ...b, left: true }));
    if (e.button === 1) setMouseButtons((b) => ({ ...b, middle: true }));
    if (e.button === 2) setMouseButtons((b) => ({ ...b, right: true }));
  };

  const handleMouseUp = (e: React.MouseEvent) => {
    e.preventDefault();
    if (mouseDownTimestampRef.current !== null) {
      const duration = performance.now() - mouseDownTimestampRef.current;
      setClickDurationMs(parseFloat(duration.toFixed(1)));
      mouseDownTimestampRef.current = null;
    }
    if (e.button === 0) setMouseButtons((b) => ({ ...b, left: false }));
    if (e.button === 1) setMouseButtons((b) => ({ ...b, middle: false }));
    if (e.button === 2) setMouseButtons((b) => ({ ...b, right: false }));
  };

  // MONITOR REFRESH RATE STATE
  const [hardwareRefreshRate, setHardwareRefreshRate] = useState<number | null>(null);
  const [browserRafFps, setBrowserRafFps] = useState<number>(60);
  const [fullscreenColor, setFullscreenColor] = useState<string | null>(null);

  // Query real display refresh rate from native agent when available
  useEffect(() => {
    const checkDisplay = async () => {
      const info = await nativeBridge.fetchDisplayInfo();
      if (info.available && info.nativeRefreshRateHz) {
        setHardwareRefreshRate(info.nativeRefreshRateHz);
      } else {
        setHardwareRefreshRate(null);
      }
    };
    checkDisplay();
  }, []);

  // Browser rAF Estimator (explicitly documented as browser rendering rate, NOT physical monitor hardware frequency)
  useEffect(() => {
    let frameTimes: number[] = [];
    let lastTime = performance.now();
    let animId: number;

    const countFrames = (now: number) => {
      const delta = now - lastTime;
      lastTime = now;
      if (delta > 0) frameTimes.push(delta);
      if (frameTimes.length > 50) {
        frameTimes.shift();
        const avg = frameTimes.reduce((a, b) => a + b, 0) / frameTimes.length;
        const fps = Math.round(1000 / avg);
        if (fps >= 30 && fps <= 540) {
          setBrowserRafFps(fps);
        }
      }
      animId = requestAnimationFrame(countFrames);
    };

    animId = requestAnimationFrame(countFrames);
    return () => cancelAnimationFrame(animId);
  }, []);

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
            <KeyboardIcon className="w-4 h-4" /> {t.peripherals.subtitle}
          </div>
          <h1 className="text-xl font-black text-white mt-0.5">{t.peripherals.title}</h1>
          <p className="text-xs text-slate-400 mt-1 max-w-xl leading-relaxed">
            {t.peripherals.description}
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
            <KeyboardIcon className="w-3.5 h-3.5" /> {t.peripherals.tabKeyboard}
          </button>
          <button
            onClick={() => setActiveTab('mouse')}
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded font-semibold cursor-pointer transition-colors ${
              activeTab === 'mouse' ? 'bg-purple-600 text-white' : 'text-slate-400 hover:text-white'
            }`}
          >
            <Mouse className="w-3.5 h-3.5" /> {t.peripherals.tabMouse}
          </button>
          <button
            onClick={() => setActiveTab('monitor')}
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded font-semibold cursor-pointer transition-colors ${
              activeTab === 'monitor' ? 'bg-purple-600 text-white' : 'text-slate-400 hover:text-white'
            }`}
          >
            <Monitor className="w-3.5 h-3.5" /> {t.peripherals.tabMonitor}
          </button>
        </div>
      </div>

      {/* 1. KEYBOARD TAB */}
      {activeTab === 'keyboard' && (
        <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-4" tabIndex={0} onKeyDown={handleKeyboardKeyDown}>
          <div className="flex items-center justify-between pb-2 border-b border-slate-800 text-xs">
            <div>
              <span className="font-bold text-white uppercase tracking-wider">
                {t.peripherals.keyboardTitle}
              </span>
              <p className="text-[11px] text-slate-400 mt-0.5">Clique nesta área e pressione as teclas para vê-las acender. O teste não captura teclas fora deste painel.</p>
            </div>
            <button
              onClick={resetKeyboard}
              className="px-3 py-1.5 rounded bg-slate-800 hover:bg-slate-700 text-slate-300 font-semibold cursor-pointer flex items-center gap-1.5 text-xs"
            >
              <RotateCcw className="w-3.5 h-3.5" /> {t.peripherals.resetKeyboard}
            </button>
          </div>

          <div className="space-y-2 p-3 bg-[#0a0c13] rounded-xl border border-slate-900 overflow-x-auto outline-none focus:border-purple-500/60">
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
            <span>{t.peripherals.keysTested} {pressedKeys.size} / ~104</span>
            <span>{t.peripherals.lastKeyPressed} {historyKeys[0] || t.common.none}</span>
          </div>
        </div>
      )}

      {/* 2. MOUSE TAB */}
      {activeTab === 'mouse' && (
        <div
          onMouseMove={handleMouseMove}
          onMouseDown={handleMouseDown}
          onMouseUp={handleMouseUp}
          onContextMenu={(e) => e.preventDefault()}
          className="p-6 rounded-xl bg-[#0f121b] border border-slate-800 space-y-5 select-none"
        >
          <div className="flex items-center justify-between pb-2 border-b border-slate-800 text-xs">
            <span className="font-bold text-white uppercase tracking-wider">
              {t.peripherals.mouseAreaTitle}
            </span>
            <span className="text-slate-400 font-mono">{t.peripherals.mouseClicks} {mouseClicksCount}</span>
          </div>

          <div className="min-h-[150px] rounded-xl border border-dashed border-purple-500/50 bg-gradient-to-br from-[#151329] to-[#0a0c13] flex flex-col items-center justify-center gap-2 text-center px-4">
            <Mouse className="w-8 h-8 text-purple-300" />
            <strong className="text-sm text-white">Área de teste do mouse</strong>
            <span className="text-xs text-slate-400">Mova o cursor, clique com os três botões e use a roda do mouse aqui</span>
            <span className="text-[11px] font-mono text-purple-300">{mouseMoveCount} movimentos · {mouseWheelCount} giros da roda</span>
          </div>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4 text-center">
            {/* Polling rate */}
            <div className="p-5 rounded-xl bg-[#0a0c13] border border-slate-800 space-y-2">
              <span className="text-xs text-slate-500 uppercase font-bold">{t.peripherals.pollingRateTitle}</span>
              <div className="text-3xl font-black font-mono text-purple-400">
                {mousePollingRate > 0 ? `~${mousePollingRate} Hz` : 'Aguardando'}
              </div>
              <span className="text-[11px] text-slate-400 block">
                {mousePollingRate > 0 ? 'Estimativa da frequência dos eventos recebidos pelo navegador; não é a taxa USB real.' : 'Mova o mouse na área de teste.'}
              </span>
            </div>

            {/* Buttons */}
            <div className="p-5 rounded-xl bg-[#0a0c13] border border-slate-800 space-y-2">
              <span className="text-xs text-slate-500 uppercase font-bold">{t.peripherals.mouseButtonsTitle}</span>
              <div className="flex justify-center gap-2 pt-1">
                <span className={`px-3 py-1 rounded text-xs font-bold ${mouseButtons.left ? 'bg-emerald-500 text-white' : 'bg-slate-800 text-slate-500'}`}>
                  LMB
                </span>
                <span className={`px-3 py-1 rounded text-xs font-bold ${mouseButtons.middle ? 'bg-emerald-500 text-white' : 'bg-slate-800 text-slate-500'}`}>
                  Wheel
                </span>
                <span className={`px-3 py-1 rounded text-xs font-bold ${mouseButtons.right ? 'bg-emerald-500 text-white' : 'bg-slate-800 text-slate-500'}`}>
                  RMB
                </span>
              </div>
              <span className="text-[11px] text-slate-400 block">{t.peripherals.clickToTest}</span>
            </div>

            {/* Real Press Hold Duration (Measured, no fake 1.2ms) */}
            <div className="p-5 rounded-xl bg-[#0a0c13] border border-slate-800 space-y-2">
              <span className="text-xs text-slate-500 uppercase font-bold">{t.peripherals.clickLatencyTitle}</span>
              <div className="text-3xl font-black font-mono text-emerald-400">
                {clickDurationMs !== null ? `${clickDurationMs} ms` : '—'}
              </div>
              <span className="text-[11px] text-slate-400 block">
                {clickDurationMs !== null
                  ? t.peripherals.opticalSwitch
                  : t.peripherals.clickDurationInstruction}
              </span>
            </div>
          </div>
        </div>
      )}

      {/* 3. MONITOR TAB */}
      {activeTab === 'monitor' && (
        <div className="p-5 rounded-xl bg-[#0f121b] border border-slate-800 space-y-4">
          <div className="flex items-center justify-between pb-2 border-b border-slate-800 text-xs">
            <span className="font-bold text-white uppercase tracking-wider">
              {t.peripherals.monitorTitle}
            </span>
            <div className="flex items-center gap-2">
              <span className="text-slate-400">{t.peripherals.hardwareRefreshRate}:</span>
              {hardwareRefreshRate !== null ? (
                <span className="text-emerald-400 font-mono font-bold">{hardwareRefreshRate} Hz</span>
              ) : (
                <span className="text-amber-400 font-mono font-bold bg-amber-500/10 px-2 py-0.5 rounded border border-amber-500/30">
                  {t.peripherals.hardwareNotDetected}
                </span>
              )}
            </div>
          </div>

          {/* Browser rAF notice banner */}
          <div className="p-3.5 rounded-lg bg-[#141829] border border-purple-500/30 text-xs flex items-start gap-2.5 text-purple-300">
            <Info className="w-4 h-4 text-purple-400 shrink-0 mt-0.5" />
            <span>{t.peripherals.browserRafNotice}</span>
          </div>

          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 text-xs font-mono">
            <div className="p-3 bg-[#0a0c13] rounded-lg border border-slate-800">
              <span className="text-slate-500 block text-[10px] uppercase">{t.peripherals.activeResolution}</span>
              <span className="text-white font-bold">{window.screen.width} x {window.screen.height}</span>
            </div>
            <div className="p-3 bg-[#0a0c13] rounded-lg border border-slate-800">
              <span className="text-slate-500 block text-[10px] uppercase">{t.peripherals.browserRafEstimate}</span>
              <span className="text-cyan-400 font-bold">~{browserRafFps} FPS</span>
            </div>
            <div className="p-3 bg-[#0a0c13] rounded-lg border border-slate-800">
              <span className="text-slate-500 block text-[10px] uppercase">{t.peripherals.colorDepth}</span>
              <span className="text-white font-bold">{window.screen.colorDepth}-bit RGB</span>
            </div>
            <div className="p-3 bg-[#0a0c13] rounded-lg border border-slate-800">
              <span className="text-slate-500 block text-[10px] uppercase">{t.peripherals.dpiScale}</span>
              <span className="text-white font-bold">{(window.devicePixelRatio * 100).toFixed(0)}%</span>
            </div>
          </div>

          <div className="space-y-2 pt-2">
            <span className="text-xs font-bold text-slate-300 uppercase block">
              {t.peripherals.deadPixelTitle}
            </span>
            <div className="flex gap-2">
              {[
                { name: t.peripherals.patternBlack, color: '#000000' },
                { name: t.peripherals.patternWhite, color: '#ffffff' },
                { name: t.peripherals.patternRed, color: '#ff0000' },
                { name: t.peripherals.patternGreen, color: '#00ff00' },
                { name: t.peripherals.patternBlue, color: '#0000ff' },
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

      {/* Fullscreen color modal */}
      {fullscreenColor && (
        <div
          onClick={() => setFullscreenColor(null)}
          className="fixed inset-0 z-50 flex items-center justify-center cursor-pointer"
          style={{ backgroundColor: fullscreenColor }}
        >
          <div className="bg-black/80 px-4 py-2 rounded text-white text-xs font-bold backdrop-blur-md opacity-70 hover:opacity-100 transition-opacity">
            {t.peripherals.clickToExit}
          </div>
        </div>
      )}
    </div>
  );
};
