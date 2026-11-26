import assert from "node:assert/strict";
import test from "node:test";
import { parseOpenRequest, sessionFromTab } from "./protocol";

test("only an exact chat tab identifies a focused Kiro session", () => {
  assert.equal(sessionFromTab("kiro.views.agentChat", "kiro-session:/1/sess_123-abc"), "sess_123-abc");
  assert.equal(sessionFromTab("other", "kiro-session:/1/sess_123-abc"), undefined);
  assert.equal(sessionFromTab("kiro.views.agentChat", "file:///workspace"), undefined);
});

test("open links require valid request and session IDs", () => {
  assert.deepEqual(parseOpenRequest("sessionId=sess_123-abc&requestId=0123456789abcdef0123456789abcdef&ownerPid=123&targetWindowId=1&agentPort=54321"), {
    sessionId: "sess_123-abc", requestId: "0123456789abcdef0123456789abcdef", ownerPid: 123, targetWindowId: 1, agentPort: 54321
  });
  assert.equal(parseOpenRequest("sessionId=../secrets&requestId=0123456789abcdef0123456789abcdef&ownerPid=123&targetWindowId=1&agentPort=54321"), undefined);
  assert.equal(parseOpenRequest("sessionId=sess_123-abc&requestId=0123456789abcdef0123456789abcdef&ownerPid=0&targetWindowId=1&agentPort=54321"), undefined);
  assert.equal(parseOpenRequest("sessionId=sess_123-abc&requestId=0123456789abcdef0123456789abcdef&ownerPid=123&targetWindowId=1&agentPort=65536"), undefined);
});
