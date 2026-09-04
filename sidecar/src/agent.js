import { Agent, convertToLlm } from '@earendil-works/pi-agent-core';
import { Type, contentText } from '@earendil-works/pi-ai';
import { buildModelAndStream } from './providers.js';
import { makeToolInvoker } from './toolClient.js';
import { EVENT } from './protocol.js';

// 工具 schema 与 C# 侧 DiskAnalyst.Tools 保持一致。
// 真正的执行全部转发回 C# —— 勾选项、删文件都要动 C# 状态和 Windows Shell。
function buildTools(invokeTool) {
  const listFolder = {
    name: 'list_folder',
    label: 'List folder',
    description: "List the largest direct children of a folder from the scan tree. Max 40.",
    parameters: Type.Object({
      path: Type.String({ description: 'Folder path, e.g. C:\\Users' }),
    }),
    executionMode: 'sequential',
    execute: async (_id, params) => ({
      content: [{ type: 'text', text: await invokeTool('list_folder', params) }],
      details: {},
    }),
  };

  const searchClean = {
    name: 'search_clean',
    label: 'Search cleanable',
    description: 'Search the cleanable and largest-file lists by name, path, reason, or group.',
    parameters: Type.Object({
      query: Type.String({ description: 'Text to search' }),
    }),
    executionMode: 'sequential',
    execute: async (_id, params) => ({
      content: [{ type: 'text', text: await invokeTool('search_clean', params) }],
      details: {},
    }),
  };

  const setChecked = {
    name: 'set_checked',
    label: 'Check items',
    description:
      'Check or uncheck items on the clean list. Cannot delete. Only safe cleanable items (temp/cache, dumps, recycle) and large files outside Windows/Program Files/system.',
    parameters: Type.Object({
      paths: Type.Array(Type.String(), { description: 'Full paths' }),
      checked: Type.Boolean({ description: 'true to check, false to uncheck' }),
    }),
    executionMode: 'sequential',
    execute: async (_id, params) => ({
      content: [{ type: 'text', text: await invokeTool('set_checked', params) }],
      details: {},
    }),
  };

  const suggest = {
    name: 'suggest',
    label: 'Suggest cleanup',
    description:
      'Mark files the user might delete. Checks them on the right clean list. Does not delete. note = specific reason in the user language.',
    parameters: Type.Object({
      items: Type.Array(
        Type.Object({
          path: Type.String(),
          note: Type.String({ description: 'Short reason, one line' }),
        }),
      ),
    }),
    executionMode: 'sequential',
    execute: async (_id, params) => ({
      content: [{ type: 'text', text: await invokeTool('suggest', params) }],
      details: {},
    }),
  };

  return [listFolder, searchClean, setChecked, suggest];
}

/**
 * 跑一次分析。onEvent 收到的对象直接 SSE 推给 C#。
 */
export async function runAgent({ cfg, system, messages, callbackPort, onEvent, signal, maxTurns = 4 }) {
  const { models, model } = buildModelAndStream(cfg);
  const invokeTool = makeToolInvoker(callbackPort);

  // 实测：模型会反复换关键词 search_clean，十几二十轮下去中转直接 520。
  // 这里硬性封顶，到点就停，用已经拿到的结果收尾。
  let turns = 0;

  const agent = new Agent({
    initialState: {
      systemPrompt: system,
      model,
      tools: buildTools(invokeTool),
      messages: [],
    },
    convertToLlm,
    streamFn: models.streamSimple.bind(models),
    toolExecution: 'sequential',
    getApiKey: async () => cfg.apiKey || '',
    shouldStopAfterTurn: async () => {
      turns += 1;
      // 单纯防跑飞。总结不靠循环内部（follow-up / 撤工具都试过，受
      // agent-loop.js 里「有 tool call 就不检查 follow-up」的约束，很难凑对），
      // 改成循环结束后由 completeSimple 兜底产出结论。
      return turns >= maxTurns;
    },
  });

  let sawTool = false;
  agent.subscribe((event) => {
    if (event.type === 'message_update' && event.assistantMessageEvent?.type === 'text_delta') {
      onEvent({ type: EVENT.DELTA, text: event.assistantMessageEvent.delta });
    } else if (event.type === 'tool_execution_end') {
      sawTool = true;
      onEvent({ type: EVENT.TOOL, name: event.toolName, id: event.toolCallId });
    } else if (event.type === 'agent_end') {
      onEvent({ type: EVENT.DONE });
    }
  });

  // messages: [{role, content}] —— 多轮对话时带历史
  const parts = [];
  for (const m of messages) {
    if (typeof m.content === 'string' && m.content.trim()) parts.push(m.content);
  }
  const prompt = parts.length > 0 ? parts.join('\n\n') : '';

  await agent.prompt(prompt);
  await agent.waitForIdle();

  // 兜底总结：只要动过工具，循环停下的位置大概率停在「我再查查」这类过程语，
  // 用户看到的是半句话。这里用一次独立的、无工具的请求把结论问出来。
  // 走 completeSimple 而不是 agent 循环，是因为循环内部的 follow-up / 撤工具
  // 都受「本轮有 tool call 就不检查 follow-up」约束，凑不对。
  if (sawTool) {
    try {
      const history = convertToLlm(agent.state.messages);
      history.push({
        role: 'user',
        content:
          'Stop investigating. Using only what you already found, write the final cleanup list now. ' +
          'Plain text, no tool calls.',
        timestamp: Date.now(),
      });
      const final = await models.completeSimple(model, { messages: history });
      const text = contentText(final?.content ?? '');
      if (text.trim()) {
        onEvent({ type: EVENT.DELTA, text });
      }
    } catch (e) {
      // 总结失败不拖累主流程：至少过程文本已经流出去了
      onEvent({ type: EVENT.DELTA, text: '' });
    }
  }

  // 兜底：某些事件序列下 agent_end 可能没触发
  onEvent({ type: EVENT.DONE });
  return agent;
}
