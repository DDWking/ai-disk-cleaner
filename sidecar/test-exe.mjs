// 验证打包后的 AiSidecar.exe 能脱离 Node 独立运行：
// 启动 exe → 等 READY → 健康检查 → 真实模型请求。
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import http from 'node:http';
import { fileURLToPath } from 'node:url';

const sidecarDir = path.dirname(fileURLToPath(import.meta.url));
const exe = path.join(sidecarDir, 'AiSidecar.exe');
if (!fs.existsSync(exe)) {
  console.error(`找不到 ${exe}`);
  process.exit(1);
}

const sizeMb = (fs.statSync(exe).size / 1024 / 1024).toFixed(1);
console.log(`exe: ${exe}  (${sizeMb} MB)`);

const port = 52100 + Math.floor(Math.random() * 500);
const proc = spawn(exe, [String(port)], { cwd: sidecarDir, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });

const readyPromise = new Promise((resolve) => {
  proc.stdout.on('data', (b) => {
    const s = b.toString();
    process.stdout.write(`[exe out] ${s}`);
    if (s.includes('READY')) resolve(s.trim());
  });
  proc.stderr.on('data', (b) => process.stdout.write(`[exe err] ${b.toString()}`));
  proc.on('exit', (c) => { console.log(`[exe exit] code=${c}`); resolve(null); });
  setTimeout(() => resolve(null), 30000);
});

const t0 = Date.now();
const ready = await readyPromise;
if (!ready) {
  console.log('❌ 30 秒内没等到 READY');
  try { proc.kill(true); } catch {}
  process.exit(1);
}
console.log(`✅ READY (${Date.now() - t0}ms): ${ready}`);

const health = await (await fetch(`http://127.0.0.1:${port}/health`)).json();
console.log(`✅ health: ${JSON.stringify(health)}`);

// 真实请求
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
    messages: [{ role: 'user', content: 'Say: standalone exe ok' }],
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

// 再验一次工具调用确实能回打到" C#"
const cb2 = http.createServer((rq, rs) => {
  let b = '';
  rq.on('data', (c) => (b += c));
  rq.on('end', () => {
    console.log(`[C# 收到] ${b.slice(0, 160)}`);
    rs.writeHead(200, { 'content-type': 'application/json' });
    rs.end(JSON.stringify({ result: '600M  C:\\mock\\cache' }));
  });
});
const cb2Port = await new Promise((r) => cb2.listen(0, '127.0.0.1', () => r(cb2.address().port)));

const res2 = await fetch(`http://127.0.0.1:${port}/chat`, {
  method: 'POST',
  headers: { 'content-type': 'application/json' },
  body: JSON.stringify({
    provider: { baseUrl: prov.BaseUrl, apiKey: prov.ApiKey, api: 'openai-completions', model: 'deepseek-v4-flash' },
    system: 'You are a disk analyst. You MUST call list_folder on C:\\mock first, then give the cleanup list.',
    messages: [{ role: 'user', content: 'What is in C:\\mock and what can I clean?' }],
    callbackPort: cb2Port,
    maxTurns: 3,
  }),
});
let text2 = '';
const r2 = res2.body.getReader(); let b2 = '';
while (true) {
  const { done, value } = await r2.read();
  if (done) break;
  b2 += dec.decode(value, { stream: true });
  let i;
  while ((i = b2.indexOf('\n\n')) >= 0) {
    const raw = b2.slice(0, i); b2 = b2.slice(i + 2);
    const line = raw.split('\n').find((l) => l.startsWith('data:'));
    if (!line) continue;
    try {
      const ev = JSON.parse(line.slice(5).trim());
      if (ev.type === 'delta') text2 += ev.text;
    } catch {}
  }
}
console.log(`\n--- 工具场景回复 ---\n${text2 || '(空)'}`);

try { proc.kill(true); } catch {}
console.log(`\n结论: ${text.trim() ? '✅ 自包含 exe 可用' : '❌ 失败'}`);
process.exit(0);
