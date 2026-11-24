import path from "node:path";
import os from "node:os";

export type SessionId = string & { readonly __brand: "SessionId" };
export type RequestId = string & { readonly __brand: "RequestId" };
export type AgentPid = number & { readonly __brand: "AgentPid" };
export type WindowId = number & { readonly __brand: "WindowId" };
export type AgentPort = number & { readonly __brand: "AgentPort" };

export type OpenRequest = {
  sessionId: SessionId;
  requestId: RequestId;
  ownerPid: AgentPid;
  targetWindowId: WindowId;
  agentPort: AgentPort;
};

export function parseSessionId(value: unknown): SessionId | undefined {
  return typeof value === "string" && /^sess_[a-zA-Z0-9-]{1,80}$/.test(value)
    ? value as SessionId
    : undefined;
}

export function parseRequestId(value: unknown): RequestId | undefined {
  return typeof value === "string" && /^[a-f0-9]{32}$/.test(value)
    ? value as RequestId
    : undefined;
}

export function parseOpenRequest(query: string): OpenRequest | undefined {
  const values = new URLSearchParams(query);
  const sessionId = parseSessionId(values.get("sessionId"));
  const requestId = parseRequestId(values.get("requestId"));
  const pid = positiveInteger(values.get("ownerPid"), 2147483647);
  const window = positiveInteger(values.get("targetWindowId"), 2147483647);
  const port = positiveInteger(values.get("agentPort"), 65535);
  if (!sessionId || !requestId || !pid || !window || !port) return undefined;
  return { sessionId, requestId, ownerPid: pid as AgentPid, targetWindowId: window as WindowId, agentPort: port as AgentPort };
}

function positiveInteger(value: string | null, maximum: number): number | undefined {
  if (!value || !/^[0-9]{1,10}$/.test(value)) return undefined;
  const number = Number(value);
  return Number.isSafeInteger(number) && number > 0 && number <= maximum ? number : undefined;
}

export function sessionFromTab(viewType: string, uri: string): SessionId | undefined {
  if (viewType !== "kiro.views.agentChat") return undefined;
  const match = /^kiro-session:\/(?:-?\d+|remote)\/([^/?#]+)$/.exec(uri);
  return match ? parseSessionId(match[1]) : undefined;
}

export function dataRoot(platform: NodeJS.Platform, environment: NodeJS.ProcessEnv, home = os.homedir()): string {
  if (environment.KIRO_PET_HOME) return path.resolve(environment.KIRO_PET_HOME);
  if (platform === "win32") return path.join(environment.LOCALAPPDATA ?? path.join(home, "AppData", "Local"), "KiroPet");
  if (platform === "darwin") return path.join(home, "Library", "Application Support", "KiroPet");
  return path.join(home, ".local", "share", "KiroPet");
}
