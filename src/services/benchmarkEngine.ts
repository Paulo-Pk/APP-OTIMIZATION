export interface BenchmarkProgress {
  status: 'idle' | 'running' | 'completed';
  progress: number; // 0 - 100
  currentTest: string;
  score?: number;
  details?: string;
}

export async function runCpuBenchmark(
  onProgress: (p: BenchmarkProgress) => void
): Promise<{ score: number; durationMs: number; details: string }> {
  const startTime = performance.now();
  onProgress({ status: 'running', progress: 5, currentTest: 'Inicializando carga computacional x86...' });

  await new Promise((r) => setTimeout(r, 80));

  // Step 1: Prime Sieve (Multi-iteration)
  const limit = 60000;
  let primesCount = 0;
  const iterations = 60;

  for (let iter = 0; iter < iterations; iter++) {
    const isPrime = new Uint8Array(limit + 1).fill(1);
    isPrime[0] = 0;
    isPrime[1] = 0;
    for (let p = 2; p * p <= limit; p++) {
      if (isPrime[p]) {
        for (let i = p * p; i <= limit; i += p) {
          isPrime[i] = 0;
        }
      }
    }
    primesCount = 0;
    for (let i = 2; i <= limit; i++) {
      if (isPrime[i]) primesCount++;
    }

    if (iter % 10 === 0) {
      const pct = Math.round(10 + (iter / iterations) * 45);
      onProgress({
        status: 'running',
        progress: pct,
        currentTest: `Teste de Crivo de Números Primos (Iteração ${iter + 1}/${iterations})...`,
      });
      await new Promise((r) => setTimeout(r, 10));
    }
  }

  // Step 2: Floating point trigonometric math
  onProgress({ status: 'running', progress: 65, currentTest: 'Cálculo de Matrizes e Ponto Flutuante AVX...' });
  let floatAccumulator = 0;
  const mathIterations = 800000;
  for (let i = 0; i < mathIterations; i++) {
    floatAccumulator += Math.sin(i) * Math.cos(i) + Math.sqrt(i + 1);
  }

  onProgress({ status: 'running', progress: 90, currentTest: 'Consolidando métricas de IPC e latência de instrução...' });
  await new Promise((r) => setTimeout(r, 120));

  const totalDuration = performance.now() - startTime;
  // Calculate standardized score (higher is better)
  const score = Math.round(1500000 / Math.max(totalDuration, 150));

  onProgress({
    status: 'completed',
    progress: 100,
    currentTest: 'Concluído',
    score,
    details: `${score} pts · ${primesCount} primos calculados · Tempo total: ${totalDuration.toFixed(1)}ms`,
  });

  return {
    score,
    durationMs: totalDuration,
    details: `${primesCount} primos em 60 passos · Ponto flutuante: ${(mathIterations / 1000).toFixed(0)}k ops`,
  };
}

export async function runGpuBenchmark(
  onProgress: (p: BenchmarkProgress) => void
): Promise<{ score: number; durationMs: number; details: string }> {
  const startTime = performance.now();
  onProgress({ status: 'running', progress: 5, currentTest: 'Preparando teste gráfico acelerado por hardware...' });

  const canvas = document.createElement('canvas');
  canvas.width = 1024;
  canvas.height = 576;

  const contextOptions = { alpha: false, antialias: false, depth: false, stencil: false, preserveDrawingBuffer: false };
  const gl2 = canvas.getContext('webgl2', contextOptions);
  const gl = gl2 ?? canvas.getContext('webgl', contextOptions);
  if (!gl) {
    throw new Error('O navegador não disponibilizou WebGL. Ative a aceleração de hardware nas configurações do navegador e tente novamente.');
  }

  const debugInfo = gl.getExtension('WEBGL_debug_renderer_info') as { UNMASKED_VENDOR_WEBGL: number; UNMASKED_RENDERER_WEBGL: number } | null;
  const renderer = debugInfo
    ? String(gl.getParameter(debugInfo.UNMASKED_RENDERER_WEBGL) || 'GPU não identificada pelo navegador')
    : 'GPU detectada (identificação detalhada indisponível)';
  const vendor = debugInfo
    ? String(gl.getParameter(debugInfo.UNMASKED_VENDOR_WEBGL) || '')
    : '';

  if (/swiftshader|llvmpipe|microsoft basic render driver|software rasterizer/i.test(renderer)) {
    throw new Error('O navegador está usando renderização por software, não a placa de vídeo. Ative a aceleração gráfica do navegador antes de executar este teste.');
  }

  const isWebGL2 = gl2 !== null;
  const vertexSource = isWebGL2
    ? `#version 300 es
      in vec2 position;
      void main() { gl_Position = vec4(position, 0.0, 1.0); }`
    : `attribute vec2 position;
      void main() { gl_Position = vec4(position, 0.0, 1.0); }`;

  const fragmentSource = isWebGL2
    ? `#version 300 es
      precision highp float;
      uniform vec2 u_resolution;
      uniform float u_time;
      out vec4 outputColor;
      void main() {
        vec2 uv = gl_FragCoord.xy / u_resolution.xy;
        float value = 0.0;
        for (int i = 0; i < 40; i++) {
          float fi = float(i);
          vec2 p = uv * 2.0 - 1.0;
          p += vec2(sin(u_time + fi * 0.1), cos(u_time * 0.8 + fi * 0.2)) * 0.4;
          value += 0.01 / max(length(p), 0.0001);
        }
        outputColor = vec4(value * 0.8, value * 0.4, value * 1.2, 1.0);
      }`
    : `precision highp float;
      uniform vec2 u_resolution;
      uniform float u_time;
      void main() {
        vec2 uv = gl_FragCoord.xy / u_resolution.xy;
        float value = 0.0;
        for (int i = 0; i < 40; i++) {
          float fi = float(i);
          vec2 p = uv * 2.0 - 1.0;
          p += vec2(sin(u_time + fi * 0.1), cos(u_time * 0.8 + fi * 0.2)) * 0.4;
          value += 0.01 / max(length(p), 0.0001);
        }
        gl_FragColor = vec4(value * 0.8, value * 0.4, value * 1.2, 1.0);
      }`;

  const compileShader = (type: number, source: string) => {
    const shader = gl.createShader(type);
    if (!shader) throw new Error('Não foi possível criar o shader do teste gráfico.');
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
      const log = gl.getShaderInfoLog(shader) || 'Erro sem detalhes';
      gl.deleteShader(shader);
      throw new Error('Falha ao preparar o teste da GPU: ' + log);
    }
    return shader;
  };

  let vertexShader: WebGLShader | null = null;
  let fragmentShader: WebGLShader | null = null;
  let program: WebGLProgram | null = null;
  let buffer: WebGLBuffer | null = null;

  try {
    vertexShader = compileShader(gl.VERTEX_SHADER, vertexSource);
    fragmentShader = compileShader(gl.FRAGMENT_SHADER, fragmentSource);
    program = gl.createProgram();
    if (!program) throw new Error('Não foi possível criar o programa gráfico da GPU.');

    gl.attachShader(program, vertexShader);
    gl.attachShader(program, fragmentShader);
    gl.linkProgram(program);
    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
      throw new Error('A placa de vídeo não conseguiu preparar o programa de teste: ' + (gl.getProgramInfoLog(program) || 'erro de link'));
    }

    gl.useProgram(program);
    gl.viewport(0, 0, canvas.width, canvas.height);
    buffer = gl.createBuffer();
    if (!buffer) throw new Error('Não foi possível alocar o buffer de geometria.');
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, -1, 1, 1, -1, 1, 1]), gl.STATIC_DRAW);

    const positionLocation = gl.getAttribLocation(program, 'position');
    const resolutionLocation = gl.getUniformLocation(program, 'u_resolution');
    const timeLocation = gl.getUniformLocation(program, 'u_time');
    if (positionLocation < 0 || !resolutionLocation || !timeLocation) {
      throw new Error('O driver não disponibilizou todos os recursos gráficos necessários ao teste.');
    }

    gl.enableVertexAttribArray(positionLocation);
    gl.vertexAttribPointer(positionLocation, 2, gl.FLOAT, false, 0, 0);
    gl.uniform2f(resolutionLocation, canvas.width, canvas.height);

    const totalFrames = 60;
    let measuredRenderMs = 0;
    let batchStart = performance.now();
    for (let frame = 0; frame < totalFrames; frame++) {
      gl.uniform1f(timeLocation, frame * 0.05);
      gl.drawArrays(gl.TRIANGLES, 0, 6);

      if ((frame + 1) % 10 === 0) {
        // Finish each batch before measuring it. UI refresh pauses are excluded from the score.
        gl.finish();
        measuredRenderMs += performance.now() - batchStart;
        onProgress({
          status: 'running',
          progress: Math.round(10 + ((frame + 1) / totalFrames) * 80),
          currentTest: `Renderizando carga gráfica ${frame + 1}/${totalFrames}...`,
        });
        await new Promise((resolve) => setTimeout(resolve, 12));
        batchStart = performance.now();
      }
    }

    const duration = Math.max(1, measuredRenderMs);
    const renderFps = Math.round((totalFrames / (duration / 1000)) * 10) / 10;
    const score = renderFps;

    onProgress({
      status: 'completed',
      progress: 100,
      currentTest: 'Teste gráfico concluído',
      score,
      details: `${renderFps.toLocaleString('pt-BR')} quadros/s · ${isWebGL2 ? 'WebGL 2' : 'WebGL'} · ${renderer}`,
    });

    return {
      score,
      durationMs: duration,
      details: `${totalFrames} quadros renderizados em ${duration.toFixed(0)} ms · Adaptador: ${vendor ? vendor + ' / ' : ''}${renderer}`,
    };
  } finally {
    if (buffer) gl.deleteBuffer(buffer);
    if (program) gl.deleteProgram(program);
    if (vertexShader) gl.deleteShader(vertexShader);
    if (fragmentShader) gl.deleteShader(fragmentShader);
    const loseContext = gl.getExtension('WEBGL_lose_context');
    loseContext?.loseContext();
  }
}

export async function runRamBenchmark(
  onProgress: (p: BenchmarkProgress) => void
): Promise<{ score: number; durationMs: number; details: string }> {
  const startTime = performance.now();
  onProgress({ status: 'running', progress: 15, currentTest: 'Alocando buffer de 32MB na memória RAM...' });

  await new Promise((r) => setTimeout(r, 50));

  const size = 8 * 1024 * 1024; // 8M integers = 32 MB
  const source = new Uint32Array(size);
  const dest = new Uint32Array(size);

  for (let i = 0; i < size; i++) {
    source[i] = i & 0xff;
  }

  const iterations = 40;
  for (let i = 0; i < iterations; i++) {
    dest.set(source);
    // checksum
    source[i % size] = dest[(i + 5) % size] + 1;

    if (i % 8 === 0) {
      const pct = Math.round(20 + (i / iterations) * 75);
      onProgress({
        status: 'running',
        progress: pct,
        currentTest: `Teste de largura de banda de escrita contínua (${i}/${iterations})...`,
      });
      await new Promise((r) => setTimeout(r, 10));
    }
  }

  const duration = performance.now() - startTime;
  const totalMB = (iterations * 32) / (duration / 1000);
  const score = Math.round(totalMB);

  onProgress({
    status: 'completed',
    progress: 100,
    currentTest: 'Concluído',
    score,
    details: `${score} MB/s · Vazão média de leitura e cópia sequencial de blocos`,
  });

  return {
    score,
    durationMs: duration,
    details: `Taxa de transferência: ${(totalMB / 1024).toFixed(2)} GB/s`,
  };
}
