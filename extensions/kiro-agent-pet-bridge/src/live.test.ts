import * as vscode from "vscode";
import { promises as fs } from "node:fs";
import path from "node:path";
import { randomUUID } from "node:crypto";
import { dataRoot, parseSessionId } from "./protocol";

function record(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

async function waitJson(file: string, timeout = 60000): Promise<unknown> {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    try { return JSON.parse(await fs.readFile(file, "utf8")); } catch { }
    await new Promise<void>((resolve) => setTimeout(resolve, 200));
  }
  throw new Error(`Timed out waiting for ${path.basename(file)}.`);
}

async function bounded<T>(operation: Thenable<T>, label: string): Promise<T> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  try {
    return await Promise.race([Promise.resolve(operation), new Promise<never>((_, reject) => {
      timer = setTimeout(() => reject(new Error(`${label} did not finish within 30 seconds.`)), 30000);
    })]);
  } finally { if (timer) clearTimeout(timer); }
}

export async function run(): Promise<void> {
  const folder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
  if (!folder || !path.basename(folder).startsWith("kiro-agent-pet-ide-probe-")) throw new Error("Live test requires its own temporary workspace.");
  const resultFile = path.join(folder, "live-result.json");
  const root = dataRoot(process.platform, process.env);
  let sessionId: string | undefined;
  try {
    const agent = vscode.extensions.getExtension("kiro.kiroAgent");
    const bridge = vscode.extensions.getExtension("jaeskang0.kiro-agent-pet-bridge");
    if (!agent || !bridge) throw new Error("Kiro or the development bridge is unavailable.");
    await bounded(agent.activate(), "Kiro activation");
    await bounded(bridge.activate(), "Bridge activation");
    const result: unknown = await bounded(vscode.commands.executeCommand("kiroAgent.sessions.create"), "Verification chat creation");
    sessionId = record(result) ? parseSessionId(result.sessionId) : undefined;
    if (!sessionId) throw new Error("Kiro did not create the empty verification chat.");
    const endpoint = await waitJson(path.join(root, "ide-endpoints", `${process.pid}-${bridge.id}.json`));
    if (!record(endpoint) || typeof endpoint.uri !== "string" || endpoint.pid !== process.pid ||
      typeof endpoint.windowId !== "number" || typeof endpoint.agentPort !== "number") throw new Error("IDE endpoint registration failed.");
    const lease: unknown = await waitJson(path.join(process.env.KIRO_NATIVE_HOME ?? path.join(process.env.USERPROFILE ?? "", ".kiro"), "session-leases", `${sessionId}.lease`));
    if (!record(lease) || typeof lease.pid !== "number") throw new Error("Kiro did not record the verification chat's owner.");
    const agentPid = lease.pid;
    const requestId = randomUUID().replaceAll("-", "");
    const link = new URL(endpoint.uri);
    link.searchParams.set("sessionId", sessionId);
    link.searchParams.set("requestId", requestId);
    link.searchParams.set("ownerPid", String(agentPid));
    link.searchParams.set("targetWindowId", String(endpoint.windowId));
    link.searchParams.set("agentPort", String(endpoint.agentPort));
    await fs.writeFile(path.join(folder, "uri-request.json"), JSON.stringify({ link: link.toString(), requestId, sessionId, ownerPid: agentPid }));
    const reply = await waitJson(path.join(root, "ide-replies", `${requestId}.json`), 90000);
    if (!record(reply) || reply.opened !== true || reply.requestId !== requestId ||
      reply.sessionId !== sessionId || reply.bridgePid !== process.pid) {
      throw new Error(record(reply) && typeof reply.message === "string" ? reply.message : "Exact chat confirmation failed.");
    }
    const completionId = `verification:${requestId}`;
    await fs.mkdir(root, { recursive: true });
    await fs.writeFile(path.join(root, "ide-status.json"), JSON.stringify([{ sessionId, status: "done", completionId, windowId: endpoint.windowId, agentPid }]));
    const deadline = Date.now() + 10000;
    let focused = false;
    while (Date.now() < deadline && !focused) {
      try {
        for (const file of await fs.readdir(path.join(root, "ide-viewed"))) {
          const event: unknown = JSON.parse(await fs.readFile(path.join(root, "ide-viewed", file), "utf8"));
          if (record(event) && event.sessionId === sessionId && event.completionId === completionId) focused = true;
        }
      } catch { }
      if (!focused) await new Promise<void>((resolve) => setTimeout(resolve, 200));
    }
    if (!focused) throw new Error("The focused editor tab did not report its completion identity.");
    await fs.writeFile(resultFile, JSON.stringify({ passed: true, sessionId, ownerPid: agentPid, bridgePid: process.pid, focused, workspace: folder }));
  } catch (error) {
    await fs.writeFile(resultFile, JSON.stringify({ passed: false, sessionId, message: error instanceof Error ? error.message : String(error) }));
    throw error;
  }
}
