import { afterEach, describe, expect, it, vi } from "vitest";
import {
  ApiError,
  deleteCapability,
  getDevices,
  restartAgent,
  revokeApiKeyOperator,
} from "./api";

// Drives the status mapping through the real exported functions with a
// stubbed fetch - requestVoid/throwUnlessOk stay private, the way the
// eleven-copies refactor left them. Each case is a behaviour that was
// wrong once: three of the old copies lost their 403 branch, and none
// of them surfaced the backend's crafted error body ("still referenced
// by ...") - it rendered as "Request failed (409)".

function stubFetch(status: number, body = "", contentType = "text/plain") {
  const response = new Response(status === 204 ? null : body, {
    status,
    headers: { "Content-Type": contentType },
  });
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response));
}

async function errorFrom(promise: Promise<unknown>): Promise<ApiError> {
  try {
    await promise;
  } catch (err) {
    expect(err).toBeInstanceOf(ApiError);
    return err as ApiError;
  }
  throw new Error("expected the call to reject");
}

afterEach(() => vi.unstubAllGlobals());

describe("no-body requests (requestVoid)", () => {
  it("resolves on 202 with no body to parse", async () => {
    stubFetch(202);
    await expect(restartAgent("key", "agent-1")).resolves.toBeUndefined();
  });

  it("maps 401 to the invalid-key message", async () => {
    stubFetch(401);
    const err = await errorFrom(restartAgent("key", "agent-1"));
    expect(err.status).toBe(401);
    expect(err.message).toBe("Invalid API key.");
  });

  it("maps 403 to Not permitted - the branch three old copies lost", async () => {
    stubFetch(403);
    const err = await errorFrom(deleteCapability("key", "cap-1"));
    expect(err.status).toBe(403);
    expect(err.message).toBe("Not permitted.");
  });

  it("surfaces the backend's real reason from a 409 body", async () => {
    stubFetch(409, "Capability is still referenced by 2 assignments.");
    const err = await errorFrom(deleteCapability("key", "cap-1"));
    expect(err.message).toBe("Capability is still referenced by 2 assignments.");
  });

  it("unwraps a JSON-string error body", async () => {
    stubFetch(400, '"Settings may not contain credential keys."', "application/json");
    const err = await errorFrom(restartAgent("key", "agent-1"));
    expect(err.message).toBe("Settings may not contain credential keys.");
  });

  it("falls back to a generic message when the body is empty", async () => {
    stubFetch(500);
    const err = await errorFrom(restartAgent("key", "agent-1"));
    expect(err.message).toBe("Request failed (500).");
  });
});

describe("operator tier", () => {
  it("gets its own 401 message", async () => {
    stubFetch(401);
    const err = await errorFrom(revokeApiKeyOperator("host-key", "key-1"));
    expect(err.message).toBe("Invalid operator key.");
  });
});

describe("json requests (request<T>)", () => {
  it("parses a successful body", async () => {
    stubFetch(200, "[]", "application/json");
    await expect(getDevices("key")).resolves.toEqual([]);
  });

  it("maps 403 to Not permitted - request<T> lacked this branch too", async () => {
    stubFetch(403);
    const err = await errorFrom(getDevices("key"));
    expect(err.message).toBe("Not permitted.");
  });
});
