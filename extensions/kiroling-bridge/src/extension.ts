import * as vscode from "vscode";
import { promises as fs } from "node:fs";
import path from "node:path";
import { randomUUID } from "node:crypto";
import os from "node:os";
import { dataRoot, parseOpenRequest, parseSessionId, sessionFromTab, SessionId, AgentPid } from "./protocol";

function record(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function focusedSession(): SessionId | undefined {
  if (!vscode.window.state.focused) return undefined;
  const input = vscode.window.tabGroups.activeTabGroup.activeTab?.input;
  return input instanceof vscode.TabInputCustom ? sessionFromTab(input.viewType, input.uri.toString()) : undefined;
}

async function writeJson(root: string, folder: string, id: string, value: unknown): Promise<void> {
  const directory = path.join(root, folder);
  await fs.mkdir(directory, { recursive: true });
  const destination = path.join(directory, `${id}.json`);
  const temporary = `${destination}.${randomUUID()}.tmp`;
  try {
    await fs.writeFile(temporary, JSON.stringify(value), { encoding: "utf8", flag: "wx" });
    await fs.rename(temporary, destination);
  } finally {
    await fs.unlink(temporary).catch(() => undefined);
  }
}

async function ownsSession(sessionId: SessionId, agentPid: number): Promise<boolean> {
  try {
    const nativeRoot = process.env.KIRO_NATIVE_HOME ?? path.join(os.homedir(), ".kiro");
    const lease: unknown = JSON.parse(await fs.readFile(path.join(nativeRoot, "session-leases", `${sessionId}.lease`), "utf8"));
    return record(lease) && lease.pid === agentPid && lease.clientName === "kiro-ide";
  } catch { return false; }
}

async function waitForChat(sessionId: SessionId, agentPid: AgentPid): Promise<boolean> {
  const deadline = Date.now() + 8000;
  while (Date.now() < deadline) {
    if (focusedSession() === sessionId && await ownsSession(sessionId, agentPid)) return true;
    await new Promise<void>((resolve) => setTimeout(resolve, 100));
  }
  return false;
}

export function activate(context: vscode.ExtensionContext): void {
  const root = dataRoot(process.platform, process.env);
  const output = vscode.window.createOutputChannel("Kiroling");
  let lastViewed = "";
  let reading = false;
  let publishing = false;
  let stopped = false;
  let endpointUri: vscode.Uri | undefined;
  let windowId = 0;
  let agentPort = 0;
  const startedAt = Math.round(Date.now() - process.uptime() * 1000);
  const endpointFile = path.join(root, "ide-endpoints", `${process.pid}.json`);

  async function publishEndpoint(): Promise<void> {
    if (publishing || stopped || !endpointUri || windowId <= 0) return;
    publishing = true;
    try {
      const registry: unknown = await vscode.commands.executeCommand("kiro.agentRegistry.getAgentEndpoints");
      if (!Array.isArray(registry)) return;
      const agent: unknown = registry.find(entry => record(entry) && entry.windowId === windowId);
      if (!record(agent) || typeof agent.port !== "number" || !Number.isInteger(agent.port) || agent.port <= 0 || agent.port > 65535) return;
      agentPort = agent.port;
      const sessionIds = vscode.window.tabGroups.all.flatMap(group => group.tabs.flatMap(tab => {
        if (!(tab.input instanceof vscode.TabInputCustom)) return [];
        const id = sessionFromTab(tab.input.viewType, tab.input.uri.toString());
        return id ? [id] : [];
      }));
      await writeJson(root, "ide-endpoints", String(process.pid), {
        pid: process.pid, startedAt, updatedAt: Date.now(), uri: endpointUri.toString(true), windowId, agentPort,
        workspacePaths: vscode.workspace.workspaceFolders?.map(folder => folder.uri.fsPath) ?? [], sessionIds
      });
    } catch (error) {
      output.appendLine(`Could not register IDE window: ${error instanceof Error ? error.message : "unknown error"}`);
    } finally { publishing = false; }
  }

  void vscode.env.asExternalUri(vscode.Uri.parse(`${vscode.env.uriScheme}://${context.extension.id}/open`)).then(uri => {
    if (uri.scheme !== "kiro") { output.appendLine("Kiro did not provide a local chat-opening link."); return; }
    endpointUri = uri;
    windowId = Number(new URLSearchParams(uri.query).get("windowId"));
    if (!Number.isSafeInteger(windowId) || windowId <= 0) { output.appendLine("Kiro did not identify this IDE window."); return; }
    void publishEndpoint();
  }, error => output.appendLine(`Could not resolve this window's link: ${String(error)}`));

  async function reportFocusedChat(): Promise<void> {
    if (reading || stopped) return;
    const sessionId = focusedSession();
    if (!sessionId) { lastViewed = ""; return; }
    reading = true;
    try {
      const raw: unknown = JSON.parse(await fs.readFile(path.join(root, "ide-status.json"), "utf8"));
      if (!Array.isArray(raw)) return;
      for (const item of raw) {
        if (!record(item)) continue;
        const id = parseSessionId(item.SessionId ?? item.sessionId);
        const state = item.Status ?? item.status;
        const completion = item.CompletionId ?? item.completionId;
        const ownerWindow = item.WindowId ?? item.windowId;
        const ownerPid = item.AgentPid ?? item.agentPid;
        if (id !== sessionId || (state !== "done" && state !== "error") ||
          ownerWindow !== windowId || typeof ownerPid !== "number" || !Number.isSafeInteger(ownerPid) || ownerPid <= 0 ||
          typeof completion !== "string" || completion.length === 0 || completion.length > 512) continue;
        if (!await ownsSession(sessionId, ownerPid)) return;
        const identity = `${sessionId}:${completion}`;
        if (identity === lastViewed || focusedSession() !== sessionId) return;
        await writeJson(root, "ide-viewed", randomUUID().replaceAll("-", ""), { sessionId, completionId: completion });
        lastViewed = identity;
        return;
      }
    } catch (error) {
      if (error instanceof Error && "code" in error && error.code === "ENOENT") return;
      output.appendLine(`Could not report focused chat: ${error instanceof Error ? error.message : "unknown error"}`);
    } finally { reading = false; }
  }

  context.subscriptions.push(output, vscode.window.registerUriHandler({
    async handleUri(uri): Promise<void> {
      if (uri.path !== "/open") return;
      const request = parseOpenRequest(uri.query);
      if (!request) return;
      let opened = false;
      let message = "";
      try {
        const result: unknown = request.targetWindowId === windowId && request.agentPort === agentPort
          ? await vscode.commands.executeCommand("kiroAgent.sessions.openInEditor", request.sessionId)
          : { accepted: false, reason: "wrongWindow" };
        if (!record(result) || result.accepted !== true || result.sessionId !== request.sessionId) {
          const reason = record(result) && typeof result.reason === "string" ? ` (${result.reason})` : "";
          message = "Kiro could not open this chat. Reload the target IDE window and try again.";
          output.appendLine(`${message}${reason}`);
        } else {
          opened = await waitForChat(request.sessionId, request.ownerPid);
          if (!opened) message = "Kiro did not bring the requested chat into view.";
        }
      } catch (error) {
        message = error instanceof Error ? error.message : "Kiro chat navigation failed.";
      }
      await writeJson(root, "ide-replies", request.requestId, {
        requestId: request.requestId, sessionId: request.sessionId, bridgePid: process.pid, opened, message
      });
      if (opened) await reportFocusedChat();
    }
  }));
  context.subscriptions.push(
    vscode.window.onDidChangeWindowState(() => { void reportFocusedChat(); }),
    vscode.window.tabGroups.onDidChangeTabGroups(() => { void reportFocusedChat(); }),
    vscode.window.tabGroups.onDidChangeTabs(() => { void reportFocusedChat(); })
  );
  const timer = setInterval(() => { void publishEndpoint(); void reportFocusedChat(); }, 1000);
  context.subscriptions.push({ dispose: () => { stopped = true; clearInterval(timer); void fs.unlink(endpointFile).catch(() => undefined); } });
  void reportFocusedChat();
}
