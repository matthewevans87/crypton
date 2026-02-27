export interface FeatureFlags {
  showAgentPanel: boolean;
  showToolCalls: boolean;
  showReasoning: boolean;
  showMailbox: boolean;
  enableWebSocket: boolean;
  enableCache: boolean;
}

function getUrlParams(): Record<string, string> {
  const params = new URLSearchParams(window.location.search);
  const result: Record<string, string> = {};
  params.forEach((value, key) => {
    if (key.startsWith('ff_')) {
      result[key.slice(3)] = value;
    }
  });
  return result;
}

function parseEnvBoolean(value: string | undefined, defaultValue: boolean): boolean {
  if (value === undefined) return defaultValue;
  if (value === 'true') return true;
  if (value === 'false') return false;
  return defaultValue;
}

const urlParams = getUrlParams();

export const featureFlags: FeatureFlags = {
  showAgentPanel: parseEnvBoolean(
    urlParams.showAgentPanel ?? (import.meta.env.VITE_SHOW_AGENT_PANEL as string | undefined),
    true
  ),
  showToolCalls: parseEnvBoolean(
    urlParams.showToolCalls ?? (import.meta.env.VITE_SHOW_TOOL_CALLS as string | undefined),
    true
  ),
  showReasoning: parseEnvBoolean(
    urlParams.showReasoning ?? (import.meta.env.VITE_SHOW_REASONING as string | undefined),
    true
  ),
  showMailbox: parseEnvBoolean(
    urlParams.showMailbox ?? (import.meta.env.VITE_SHOW_MAILBOX as string | undefined),
    false
  ),
  enableWebSocket: parseEnvBoolean(
    urlParams.enableWebSocket ?? (import.meta.env.VITE_ENABLE_WEBSOCKET as string | undefined),
    true
  ),
  enableCache: parseEnvBoolean(
    urlParams.enableCache ?? (import.meta.env.VITE_ENABLE_CACHE as string | undefined),
    true
  ),
};

export function isFeatureEnabled(flag: keyof FeatureFlags): boolean {
  return featureFlags[flag];
}
