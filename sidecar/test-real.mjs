// 真实联调：起一个假 C# 回调端点，用 settings.json 里的真实提供方打一次请求。
// 目的：验证 pi-ai 能否拿下之前「空回复」的 deepseek 中转。
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';

const SIDECAR_PORT = Number(process.argv[2] || 51999);
const PROVIDER_NAME = process.argv[3] || 'packy';
const MODEL = process.argv[4] || 'deepseek-v4-flash';

const settingsPath = path.join(process.env.APPDATA, 'DashaoHuo', 'settings.json');
const settings = JSON.parse(fs.readFileSync(settingsPath, 'utf8'));
const prov = settings.AiProviders.find((p) => p.Name === PROVIDER_NAME || p.BaseUrl.includes(PROVIDER_NAME));
if (!prov) {
  console.error(`provider not found: ${PROVIDER_NAME}`);
  process.exit(1);
}

// 假 C# 端点：sidecar 调工具时打到这里
const toolCalls = [];
const cbServer = http.createServer((req, res) => {
  let body = '';
  req.on('data', (c) => (body += c));
  req.on('end', () => {
    let parsed = {};
    try { parsed = JSON.parse(body); } catch {}
    toolCalls.push(parsed);
    console.log(`[TOOL-CALL] ${parsed.name} ${JSON.stringify(parsed.args ?? {}).slice(0, 120)}`);
    res.writeHead(200, { 'content-type': 'application/json' });
    res.end(JSON.stringify({ result: 'ok (mock C#)' }));
  });
});

const cbPort = await new Promise((resolve) => {
  cbServer.listen(0, '127.0.0.1', () => resolve(cbServer.address().port));
});

const payload = {
  provider: {
    baseUrl: prov.BaseUrl,
    apiKey: prov.ApiKey,
    api: 'openai-completions',
    model: MODEL,
  },
  system: 'You are a file analyst. Reply with one short sentence.',
  messages: [{ role: 'user', content: 'Say: pi sidecar works. Then stop.' }],
  callbackPort: cbPort,
};

console.log(`\n=== 请求 ${prov.BaseUrl} / ${MODEL} ===`);
const started = Date.now();
const res = await fetch(`http://127.0.0.1:${SIDECAR_PORT}/chat`, {
  method: 'POST',
  headers: { 'content-type': 'application/json' },
  body: JSON.stringify(payload),
});

if (!res.ok) {
  console.error(`HTTP ${res.status}: ${await res.text()}`);
  process.exit(1);
}

let text = '';
const events = [];
const reader = res.body.getReader();
const decoder = new TextDecoder();
let buf = '';
let firstDeltaMs = null;

while (true) {
  const { done, value } = await reader.read();
  if (done) break;
  buf += decoder.decode(value, { stream: true });
  let idx;
  while ((idx = buf.indexOf('\n\n')) >= 0) {
    const raw = buf.slice(0, idx);
    buf = buf.slice(idx + 2);
    const line = raw.split('\n').find((l) => l.startsWith('data:'));
    if (!line) continue;
    try {
      const ev = JSON.parse(line.slice(5).trim());
      events.push(ev.type);
      if (ev.type === 'delta') {
        if (firstDeltaMs === null) firstDeltaMs = Date.now() - started;
        text += ev.text;
      } else if (ev.type === 'error') {
        console.log(`[ERROR] ${ev.message}`);
      } else if (ev.type === 'tool') {
        console.log(`[TOOL] ${ev.name}`);
      }
    } catch {}
  }
}

console.log(`\n=== 结果 ===`);
console.log(`耗时 ${Date.now() - started}ms, 首字 ${firstDeltaMs}ms`);
console.log(`事件序列: ${events.join(' -> ')}`);
console.log(`工具调用次数: ${toolCalls.length}`);
console.log(`\n--- 回复正文 (${text.length} 字符) ---`);
console.log(text || '(空！)');
process.exit(0);
