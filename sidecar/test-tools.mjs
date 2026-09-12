// 测工具调用：给一个需要探查目录的分析场景，看 sidecar 是否把工具调用转发回「C#」。
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';

const SIDECAR_PORT = Number(process.argv[2] || 51999);
const settings = JSON.parse(fs.readFileSync(path.join(process.env.APPDATA, 'DashaoHuo', 'settings.json'), 'utf8'));
const prov = settings.AiProviders.find((p) => p.BaseUrl.includes('cf.api.fan'));

// 假 C# 端点：只响应 list_folder，其他一律拒（模拟「没扫过的路径不让看」）
const seen = [];
const cbServer = http.createServer((req, res) => {
  let body = '';
  req.on('data', (c) => (body += c));
  req.on('end', () => {
    let p = {};
    try { p = JSON.parse(body); } catch {}
    seen.push(p.name);
    console.log(`[C# 收到工具调用] ${p.name}  args=${JSON.stringify(p.args ?? {})}`);
    let result;
    if (p.name === 'list_folder') {
      result = '1.2G  C:\\Users\\me\\AppData\\Local\\Temp\n800M  C:\\Users\\me\\AppData\\Local\\pip\n300M  C:\\Users\\me\\AppData\\Local\\npm';
    } else if (p.name === 'suggest') {
      result = 'suggested 2 items';
    } else if (p.name === 'set_checked') {
      result = 'checked';
    } else if (p.name === 'search_clean') {
      result = 'no match';
    } else {
      res.writeHead(200, { 'content-type': 'application/json' });
      res.end(JSON.stringify({ error: `unknown tool ${p.name}` }));
      return;
    }
    res.writeHead(200, { 'content-type': 'application/json' });
    res.end(JSON.stringify({ result }));
  });
});
const cbPort = await new Promise((r) => cbServer.listen(0, '127.0.0.1', () => r(cbServer.address().port)));

const payload = {
  provider: { baseUrl: prov.BaseUrl, apiKey: prov.ApiKey, api: 'openai-completions', model: 'deepseek-v4-flash' },
  system:
    'You are a disk cleanup analyst. You have tools: list_folder, search_clean, set_checked, suggest. ' +
    'To answer, FIRST call list_folder on C:\\Users\\me\\AppData\\Local to see what is there, ' +
    'then call suggest on the cache items. Never invent paths. Never delete.',
  messages: [
    { role: 'user', content: 'Look at C:\\Users\\me\\AppData\\Local and tell me what can be cleaned.' },
  ],
  callbackPort: cbPort,
};

console.log(`\n=== 工具调用测试 ===`);
const started = Date.now();
const res = await fetch(`http://127.0.0.1:${SIDECAR_PORT}/chat`, {
  method: 'POST',
  headers: { 'content-type': 'application/json' },
  body: JSON.stringify(payload),
});

let text = '';
const events = [];
let toolNotices = 0;
const reader = res.body.getReader();
const decoder = new TextDecoder();
let buf = '';
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
      if (ev.type === 'delta') text += ev.text;
      else if (ev.type === 'tool') toolNotices++;
      else if (ev.type === 'error') console.log(`[ERROR] ${ev.message}`);
    } catch {}
  }
}

console.log(`\n=== 结果 ===`);
console.log(`耗时 ${Date.now() - started}ms`);
console.log(`事件序列: ${events.join(' -> ')}`);
console.log(`C# 端实际收到的工具调用: ${seen.length} 次 -> ${seen.join(', ') || '(无)'}`);
console.log(`\n--- 回复正文 ---`);
console.log(text || '(空)');
process.exit(0);
