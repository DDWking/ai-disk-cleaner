// C# <-> sidecar 进程间协议。
// 两边都只认这里定义的事件，改协议就改这个文件。

export const EVENT = {
  DELTA: 'delta',   // 一段文本
  TOOL: 'tool',     // 模型要调工具（sidecar 已转发到 C# 并拿到结果，仅通知）
  DONE: 'done',     // 正常结束
  ERROR: 'error',   // 出错，带 message
};

// SSE 编码：每行 data: <json>
export function sseFrame(obj) {
  return `data: ${JSON.stringify(obj)}\n\n`;
}

// 解析 SSE 字节流为事件对象（C# 侧也按同样格式解析）
export function parseSse(chunk, bufferRef) {
  bufferRef.value += chunk;
  const events = [];
  let idx;
  while ((idx = bufferRef.value.indexOf('\n\n')) >= 0) {
    const raw = bufferRef.value.slice(0, idx);
    bufferRef.value = bufferRef.value.slice(idx + 2);
    const line = raw.split('\n').find((l) => l.startsWith('data:'));
    if (!line) continue;
    const payload = line.slice(5).trim();
    if (!payload || payload === '[DONE]') continue;
    try {
      events.push(JSON.parse(payload));
    } catch {
      /* 忽略半包 */
    }
  }
  return events;
}

// C# 暴露的回调端点路径：sidecar 执行工具时打回来
export const TOOL_CALLBACK_PATH = '/tool';
