import { TOOL_CALLBACK_PATH } from './protocol.js';

// 工具一律在 C# 侧执行（勾选清理项、删到回收站都要动 C# 状态和 Windows Shell）。
// sidecar 只负责把模型想调的工具原样转发过去，拿回结果。
export function makeToolInvoker(callbackPort) {
  return async function invokeTool(name, args) {
    const res = await fetch(`http://127.0.0.1:${callbackPort}${TOOL_CALLBACK_PATH}`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ name, args }),
    });
    if (!res.ok) {
      throw new Error(`tool ${name} failed: HTTP ${res.status}`);
    }
    const data = await res.json();
    if (data.error) throw new Error(data.error);
    return data.result ?? '';
  };
}
