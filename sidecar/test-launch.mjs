// 模拟 C# SidecarClient 的启动方式：
//   进程 = node <sidecar>/src/index.js <随机端口>，工作目录 = <sidecar>
// 验证：能拿到 READY，且能正常应答（覆盖 EnsureStarted 的核心路径）。
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import http from 'node:http';
import { fileURLToPath } from 'node:url';

// 脚本在 sidecar/ 下，所以 dirname 就是 sidecar 目录
const sidecarDir = path.dirname(fileURLToPath(import.meta.url));
const entry = path.join(sidecarDir, 'src', 'index.js');
const port = 52000 + Math.floor(Math.random() * 1000);

console.log(`工作目录: ${sidecarDir}`);
console.log(`入口: ${entry}`);
console.log(`端口: ${port}`);

const proc = spawn('node', [entry, String(port)], {
  cwd: sidecarDir,
  windowsHide: true,
  stdio: ['ignore', 'pipe', 'pipe'],
});

let readyLine = '';
const readyPromise = new Promise((resolve) => {
  proc.stdout.on('data', (b) => {
    const s = b.toString();
    process.stdout.write(`[sidecar out] ${s}`);
    if (!readyLine && s.includes('READY')) {
      readyLine = s.trim();
      resolve(readyLine);
    }
  });
  proc.stderr.on('data', (b) => process.stdout.write(`[sidecar err] ${b.toString()}`));
  proc.on('exit', (c) => { console.log(`[sidecar exit] code=${c}`); resolve(null); });
  setTimeout(() => resolve(null), 20000);
});

const t0 = Date.now();
const ready = await readyPromise;
if (!ready) {
  console.log('❌ 20 秒内没等到 READY');
  try { proc.kill(true); } catch {}
  process.exit(1);
}
console.log(`✅ READY (${Date.now() - t0}ms): ${ready}`);

// 健康检查
const health = await (await fetch(`http://127.0.0.1:${port}/health`)).json();
console.log(`✅ health: ${JSON.stringify(health)}`);

// 真实请求（用 settings 里的 deepseek）
const settings = JSON.parse(fs.readFileSync(path.join(process.env.APPDATA, 'DashaoHuo', 'settings.json'), 'utf8'));
const prov = settings.AiProviders.find((p) => p.BaseUrl.includes('cf.api.fan'));

const cb = http.createServer((rq, rs) => {
  rs.writeHead(200, { 'content-type': 'application/json' });
  rs.end(JSON.stringify({ result: 'ok' }));
});
const cbPort = await new Promise((r) => cb.listen(0, '127.0.0.1', () => r(cb.address().port)));

const t1 = Date.now();
const res = await fetch(`http://127.0.0.1:${port}/chat`, {
  method: 'POST',
  headers: { 'content-type': 'application/json' },
  body: JSON.stringify({
    provider: { baseUrl: prov.BaseUrl, apiKey: prov.ApiKey, api: 'openai-completions', model: 'deepseek-v4-flash' },
    system: 'Reply in one short sentence.',
    messages: [{ role: 'user', content: 'Say: end-to-end ok' }],
    callbackPort: cbPort,
    maxTurns: 2,
  }),
});

let text = '';
const reader = res.body.getReader();
const dec = new TextDecoder();
let buf = '';
while (true) {
  const { done, value } = await reader.read();
  if (done) break;
  buf += dec.decode(value, { stream: true });
  let i;
  while ((i = buf.indexOf('\n\n')) >= 0) {
    const raw = buf.slice(0, i); buf = buf.slice(i + 2);
    const line = raw.split('\n').find((l) => l.startsWith('data:'));
    if (!line) continue;
    try {
      const ev = JSON.parse(line.slice(5).trim());
      if (ev.type === 'delta') text += ev.text;
      if (ev.type === 'error') console.log(`[ERROR] ${ev.message}`);
    } catch {}
  }
}
console.log(`✅ 回复 (${Date.now() - t1}ms): ${text || '(空)'}`);

try { proc.kill(true); } catch {}
console.log(`\n结论: ${text.trim() ? '端到端启动路径 OK' : '❌ 空回复'}`);
process.exit(0);
