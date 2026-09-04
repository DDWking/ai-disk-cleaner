import { createModels, createProvider } from '@earendil-works/pi-ai';
import { openAICompletionsApi } from '@earendil-works/pi-ai/api/openai-completions.lazy';
import { anthropicMessagesApi } from '@earendil-works/pi-ai/api/anthropic-messages.lazy';
import { openAIResponsesApi } from '@earendil-works/pi-ai/api/openai-responses.lazy';

// 中转/野网关各种不规范的兜底开关。
// 这些都是我们之前手写 AiClient 时一个个踩出来的坑，pi-ai 有现成开关。
const DEFAULT_COMPLETIONS_COMPAT = {
  supportsStore: false,            // 多数中转不支持 store 字段
  supportsStrictMode: false,       // 中转对 strict JSON schema 支持差
  maxTokensField: 'max_tokens',    // 很多网关只认 max_tokens
  supportsUsageInStreaming: false, // 省得网关在这上面报错
  requiresToolResultName: false,
};

// DeepSeek 系：思考内容放在 reasoning_content，不加这个就是「回复为空」。
function deepseekCompat() {
  return {
    ...DEFAULT_COMPLETIONS_COMPAT,
    thinkingFormat: 'deepseek',
    requiresReasoningContentOnAssistantMessages: true,
  };
}

function inferCompat(baseUrl, api, userCompat) {
  const url = (baseUrl || '').toLowerCase();
  if (userCompat && Object.keys(userCompat).length > 0) return userCompat;
  if (url.includes('deepseek') || url.includes('api.fan')) return deepseekCompat();
  if (api === 'openai-completions') return DEFAULT_COMPLETIONS_COMPAT;
  return {};
}

const API_IMPLS = {
  'openai-completions': openAICompletionsApi,
  'openai-responses': openAIResponsesApi,
  'anthropic-messages': anthropicMessagesApi,
};

/**
 * 按 C# 传来的提供方配置建一个临时 provider + model。
 * cfg: { baseUrl, apiKey, api, model, compat?, contextWindow?, maxTokens?, reasoning? }
 */
export function buildModelAndStream(cfg) {
  const api = cfg.api || 'openai-completions';
  const makeApi = API_IMPLS[api];
  if (!makeApi) {
    throw new Error(`unsupported api: ${api}`);
  }

  const providerId = `custom-${Math.random().toString(36).slice(2, 10)}`;
  const model = {
    id: cfg.model,
    name: cfg.model,
    api,
    provider: providerId,
    baseUrl: cfg.baseUrl,
    reasoning: cfg.reasoning ?? false,
    input: ['text'],
    cost: { input: 0, output: 0, cacheRead: 0, cacheWrite: 0 },
    contextWindow: cfg.contextWindow ?? 128000,
    maxTokens: cfg.maxTokens ?? 8192,
    compat: inferCompat(cfg.baseUrl, api, cfg.compat),
    // 凭据直接给，不走 pi-ai 的环境变量/登录那一套
    headers: cfg.apiKey ? { Authorization: `Bearer ${cfg.apiKey}` } : {},
  };

  const provider = createProvider({
    id: providerId,
    name: providerId,
    baseUrl: cfg.baseUrl,
    auth: { apiKey: { name: providerId, resolve: async () => ({ auth: {} }) } },
    models: [model],
    api: makeApi(),
  });

  const models = createModels();
  models.setProvider(provider);
  return { models, model };
}
