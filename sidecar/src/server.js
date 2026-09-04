import http from 'node:http';
import { runAgent } from './agent.js';
import { sseFrame, EVENT } from './protocol.js';

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    req.on('data', (c) => {
      size += c.length;
      if (size > 8 * 1024 * 1024) {
        reject(new Error('body too large'));
        req.destroy();
        return;
      }
      chunks.push(c);
    });
    req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    req.on('error', reject);
  });
}

export function startServer(port) {
  const server = http.createServer(async (req, res) => {
    // 健康检查：C# 用它判断 sidecar 是否活着
    if (req.method === 'GET' && req.url === '/health') {
      res.writeHead(200, { 'content-type': 'application/json' });
      res.end(JSON.stringify({ ok: true, pid: process.pid }));
      return;
    }

    if (req.method !== 'POST' || req.url !== '/chat') {
      res.writeHead(404, { 'content-type': 'application/json' });
      res.end(JSON.stringify({ error: 'not found' }));
      return;
    }

    let body;
    try {
      body = JSON.parse(await readBody(req));
    } catch (e) {
      res.writeHead(400, { 'content-type': 'application/json' });
      res.end(JSON.stringify({ error: `bad json: ${e.message}` }));
      return;
    }

    res.writeHead(200, {
      'content-type': 'text/event-stream; charset=utf-8',
      'cache-control': 'no-cache',
      connection: 'keep-alive',
      'x-accel-buffering': 'no',
    });

    const controller = new AbortController();
    res.on('close', () => controller.abort());

    try {
      await runAgent({
        cfg: body.provider,
        system: body.system ?? '',
        messages: body.messages ?? [],
        callbackPort: body.callbackPort,
        maxTurns: body.maxTurns ?? 4,
        signal: controller.signal,
        onEvent: (ev) => {
          if (!res.writableEnded) res.write(sseFrame(ev));
        },
      });
    } catch (err) {
      if (!res.writableEnded) {
        res.write(sseFrame({ type: EVENT.ERROR, message: String(err?.message ?? err) }));
      }
    } finally {
      if (!res.writableEnded) res.end();
    }
  });

  return new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(port, '127.0.0.1', () => resolve(server));
  });
}
