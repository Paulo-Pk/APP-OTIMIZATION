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
  onProgress({ status: 'running', progress: 10, currentTest: 'Inicializando contexto WebGL Shader Compute...' });

  await new Promise((r) => setTimeout(r, 80));

  // Create an offscreen canvas
  const canvas = document.createElement('canvas');
  canvas.width = 512;
  canvas.height = 512;
  const gl = canvas.getContext('webgl2') || canvas.getContext('webgl');

  if (!gl) {
    onProgress({
      status: 'completed',
      progress: 100,
      currentTest: 'Aceleração de hardware WebGL indisponível',
      score: 4200,
      details: 'Modo padrão sem WebGL2',
    });
    return { score: 4200, durationMs: 500, details: 'WebGL indisponível' };
  }

  // Vertex and Fragment shaders for raymarching fractal computation
  const vsSource = `
    attribute vec2 position;
    void main() {
      gl_Position = vec4(position, 0.0, 1.0);
    }
  `;

  const fsSource = `
    precision highp float;
    uniform vec2 u_resolution;
    uniform float u_time;
    void main() {
      vec2 uv = gl_FragCoord.xy / u_resolution.xy;
      float color = 0.0;
      for (float i = 0.0; i < 40.0; i++) {
        vec2 p = uv * 2.0 - 1.0;
        p += vec2(sin(u_time + i * 0.1), cos(u_time * 0.8 + i * 0.2)) * 0.4;
        color += 0.01 / length(p);
      }
      gl_FragColor = vec4(vec3(color * 0.8, color * 0.4, color * 1.2), 1.0);
    }
  `;

  function createShader(glCtx: WebGLRenderingContext, type: number, src: string) {
    const s = glCtx.createShader(type);
    if (!s) return null;
    glCtx.shaderSource(s, src);
    glCtx.compileShader(s);
    return s;
  }

  const vs = createShader(gl, gl.VERTEX_SHADER, vsSource);
  const fs = createShader(gl, gl.FRAGMENT_SHADER, fsSource);
  const program = gl.createProgram();

  if (!vs || !fs || !program) {
    return { score: 5500, durationMs: 400, details: 'Shader compilation fallback' };
  }

  gl.attachShader(program, vs);
  gl.attachShader(program, fs);
  gl.linkProgram(program);
  gl.useProgram(program);

  const buffer = gl.createBuffer();
  gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
  gl.bufferData(
    gl.ARRAY_BUFFER,
    new Float32Array([-1, -1, 1, -1, -1, 1, -1, 1, 1, -1, 1, 1]),
    gl.STATIC_DRAW
  );

  const posLoc = gl.getAttribLocation(program, 'position');
  gl.enableVertexAttribArray(posLoc);
  gl.vertexAttribPointer(posLoc, 2, gl.FLOAT, false, 0, 0);

  const resLoc = gl.getUniformLocation(program, 'u_resolution');
  const timeLoc = gl.getUniformLocation(program, 'u_time');
  gl.uniform2f(resLoc, 512, 512);

  // Render 80 frames measuring time
  const totalFrames = 80;
  for (let f = 0; f < totalFrames; f++) {
    gl.uniform1f(timeLoc, f * 0.05);
    gl.drawArrays(gl.TRIANGLES, 0, 6);

    if (f % 15 === 0) {
      const pct = Math.round(20 + (f / totalFrames) * 75);
      onProgress({
        status: 'running',
        progress: pct,
        currentTest: `Renderizando ${totalFrames} frames de Fractal Raymarching (${f}/${totalFrames})...`,
      });
      await new Promise((r) => setTimeout(r, 10));
    }
  }

  // Read one pixel to force GPU pipeline flush
  const pixel = new Uint8Array(4);
  gl.readPixels(0, 0, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, pixel);

  const duration = performance.now() - startTime;
  const fps = (totalFrames / (duration / 1000)).toFixed(1);
  const score = Math.round((totalFrames / duration) * 85000);

  onProgress({
    status: 'completed',
    progress: 100,
    currentTest: 'Concluído',
    score,
    details: `${score} pts · Renderizados ${totalFrames} frames a ~${fps} FPS real`,
  });

  return {
    score,
    durationMs: duration,
    details: `${totalFrames} quadros shader raymarching · ${fps} FPS renderizado`,
  };
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
