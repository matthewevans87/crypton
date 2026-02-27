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
    urlParams.showAgentPanel ?? process.env.REACT_APP_SHOW_AGENT_PANEL,
    true
  ),
  showToolCalls: parseEnvBoolean(
    urlParams.showToolCalls ?? process.env.REACT_APP_SHOW_TOOL_CALLS,
    true
  ),
  showReasoning: parseEnvBoolean(
    urlParams.showReasoning ?? process.env.REACT_APP_SHOW_REASONING,
    true
  ),
  showMailbox: parseEnvBoolean(
    urlParams.showMailbox ?? process.env.REACT_APP_SHOW_MAILBOX,
    false
  ),
  enableWebSocket: parseEnvBoolean(
    urlParams.enableWebSocket ?? process.env.REACT_APP_ENABLE_WEBSOCKET,
    true
  ),
  enableCache: parseEnvBoolean(
    urlParams.enableCache ?? process.env.REACT_APP_ENABLE_CACHE,
    true
  ),
};

export function isFeatureEnabled(flag: keyof FeatureFlags): boolean {
  return featureFlags[flag];
}
