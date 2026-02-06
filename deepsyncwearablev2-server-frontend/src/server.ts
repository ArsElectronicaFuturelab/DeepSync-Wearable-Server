import { Hono } from "hono";
import { logger } from 'hono/logger'
import { cors } from "hono/cors";
import { serve } from "@hono/node-server";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import process from "node:process";

type Color = { r: number; g: number; b: number };
type Wearable = { id: number; heartRate: number; color: Color; timestamp?: number };
type SimulatedWearable = {
    id: number;
    ip?: string;
    baseHeartRate: number;
    amplitude: number;
    speedHz: number;
    intervalMs: number;
    color: Color;
};
type ControlPayload = Record<string, unknown> & { action: string };
type ControlResult = {
    ok: boolean;
    status: number;
    bodyText: string;
    parsedBody?: unknown;
};
type RouteConfig = {
    wearables: string;
    state: string;
    simulatedSnapshot: string;
    simulatedCreate: string;
    simulatedUpdate: string;
    simulatedDelete: string;
};
type UrlConfig = {
    routes: RouteConfig;
    server: {
        backendControlUrl: string;
    };
};

let wearables: Wearable[] = [];
let simulatedWearables: SimulatedWearable[] = [];
const connectionTimeoutMs = Number(process.env.WEARABLE_TIMEOUT_MS || 1000);
const connectionMonitorIntervalMs = 200;
let lastWearableUpdate = 0;
let serverConnected = false;

const currentDir = dirname(fileURLToPath(import.meta.url));
const rootDir = join(currentDir, "..");
const publicDir = join(rootDir, "public");
const configDir = join(rootDir, "config");
const indexHtml = readFileSync(join(publicDir, "index.html"), "utf-8");
const urlConfig: UrlConfig = JSON.parse(readFileSync(join(configDir, "url-config.json"), "utf-8"));
const routes = urlConfig.routes;
const backendControlUrl = process.env.BACKEND_CONTROL_URL || urlConfig.server.backendControlUrl;
const controlTimeoutMs = Number(process.env.CONTROL_TIMEOUT_MS || 5000);

const app = new Hono();

app.use("/*", cors());
app.use(logger());

setInterval(() => {
    if (!lastWearableUpdate) {
        return;
    }
    const elapsed = Date.now() - lastWearableUpdate;
    if (elapsed > connectionTimeoutMs && serverConnected) {
        console.warn(`Wearable feed timed out after ${elapsed}ms without updates, 
            clearing ${wearables.length} entries.`);
        serverConnected = false;
        wearables = [];
        simulatedWearables = [];
    }
}, connectionMonitorIntervalMs);

app.get("/", (c) => c.html(indexHtml));
app.get("/app.js", (c) => {
  const js = readFileSync(join(publicDir, "app.js"), "utf-8");
  return c.text(js, 200, {
    "Content-Type": "application/javascript",
    "Cache-Control": "no-store, no-cache, must-revalidate, max-age=0",
  });
});
// app.get("/styles.css", (c) => c.text(stylesCss, 200, { "Content-Type": "text/css" }));
app.get("/styles.css", (c) => {
  const css = readFileSync(join(publicDir, "styles.css"), "utf-8");
  return c.text(css, 200, {
    "Content-Type": "text/css",
    "Cache-Control": "no-store, no-cache, must-revalidate, max-age=0",
  });
});
app.get("/config/url-config.json", (c) => c.json(urlConfig));

app.post(routes.wearables, async (c) => {
    const payload = await c.req.json();
    const updates: Wearable[] = Array.isArray(payload) ? payload : [payload];

    const now = Date.now();
    wearables = updates.map((w) => ({ ...w, timestamp: w.timestamp ?? now }));

    lastWearableUpdate = now;
    serverConnected = true;
    console.log(`Received wearable snapshot with ${updates.length} entries.`);
    return c.json({ ok: true });
});

app.post(routes.simulatedSnapshot, async (c) => {
    const payload = await c.req.json();
    const updates = Array.isArray(payload) ? payload : [payload];
    const snapshot = refreshSimulatedWearables(updates);
    console.log(`Received simulated snapshot with ${snapshot.length} entries.`);
    return c.json({ ok: true, simulatedWearables: snapshot });
});

app.get(routes.state, (c) =>
    c.json({
        wearables,
        simulatedWearables,
        serverConnected
    })
);

app.post(routes.simulatedCreate, async (c) => {
    const result = await forwardControlCommand({ action: "create" });
    const snapshot = applySimulatedSnapshot(result);
    return c.json({ ok: result.ok, backendStatus: result.status, simulatedWearables: snapshot }, result.ok ? 200 : 502);
});

app.post(routes.simulatedUpdate, async (c) => {
    const ip = (c.req.param("ip") ?? c.req.param("id") ?? "").trim();
    if (!ip) {
        console.warn("[control:update] Missing simulated wearable IP");
        return c.json({ ok: false, error: "Missing simulated wearable IP" }, 400);
    }

    const body = await c.req.json();

    const result = await forwardControlCommand({
        action: "update",
        ip,
        id: body.id,
        baseHeartRate: body.baseHeartRate,
        amplitude: body.amplitude,
        speedHz: body.speedHz,
        intervalMs: body.intervalMs,
        color: body.color
    });

    const snapshot = applySimulatedSnapshot(result);
    return c.json({ ok: result.ok, backendStatus: result.status, simulatedWearables: snapshot }, result.ok ? 200 : 502);
});

app.delete(routes.simulatedDelete, async (c) => {
    const ip = (c.req.param("ip") ?? c.req.param("id") ?? "").trim();
    if (!ip) {
        console.warn("[control:delete] Missing simulated wearable IP");
        return c.json({ ok: false, error: "Missing simulated wearable IP" }, 400);
    }

    const result = await forwardControlCommand({ action: "delete", ip });
    const snapshot = applySimulatedSnapshot(result);

    return c.json({ ok: result.ok, backendStatus: result.status, simulatedWearables: snapshot }, result.ok ? 200 : 502);
});

async function forwardControlCommand(payload: ControlPayload): Promise<ControlResult> {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), controlTimeoutMs);

    try {
        const response = await fetch(backendControlUrl, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload),
            signal: controller.signal
        });
        const bodyText = await response.text();
        const parsedBody = tryParseJson(bodyText);

        if (response.ok) {
            console.log(`[control:${payload.action}] backend responded ${response.status}`);
        } else {
            console.error(`[control:${payload.action}] backend error ${response.status}: ${bodyText}`);
        }

        return { ok: response.ok, status: response.status, bodyText, parsedBody };
    } catch (error) {
        if (error instanceof Error && error.name === "AbortError") {
            console.error(`[control:${payload.action}] backend timeout after ${controlTimeoutMs}ms`);
            return { ok: false, status: 408, bodyText: "Request timeout" };
        }
        console.error(`[control:${payload.action}] backend call failed:`, error);
        return { ok: false, status: 500, bodyText: String(error) };
    } finally {
        clearTimeout(timeout);
    }
}

function applySimulatedSnapshot(result: ControlResult): SimulatedWearable[] | undefined {
    if (!result.ok || !Array.isArray(result.parsedBody)) {
        return undefined;
    }
    return refreshSimulatedWearables(result.parsedBody);
}

function refreshSimulatedWearables(rawItems: unknown[]): SimulatedWearable[] {
    simulatedWearables = rawItems
        .map((raw) => normalizeSimulatedWearable(raw))
        .filter((entry): entry is SimulatedWearable => Boolean(entry));
    return simulatedWearables;
}

function normalizeSimulatedWearable(raw: unknown): SimulatedWearable | undefined {
    if (!raw || typeof raw !== "object") {
        return undefined;
    }
    const entry = raw as Record<string, unknown>;
    const id = Number(entry.id);
    if (!Number.isFinite(id)) {
        return undefined;
    }

    return {
        id,
        ip: typeof entry.ip === "string" ? entry.ip : typeof entry.Ip === "string" ? entry.Ip : undefined,
        baseHeartRate: Number(entry.baseHeartRate ?? entry.BaseHeartRate ?? 0),
        amplitude: Number(entry.amplitude ?? entry.Amplitude ?? 0),
        speedHz: Number(entry.speedHz ?? entry.SpeedHz ?? 0),
        intervalMs: Number(entry.intervalMs ?? entry.IntervalMs ?? 0),
        color: normalizeColor(entry.color ?? entry.Color)
    };
}

function normalizeColor(raw: unknown): Color {
    if (!raw || typeof raw !== "object") {
        return { r: 0, g: 0, b: 0 };
    }
    const color = raw as Record<string, unknown>;
    return {
        r: Number(color.r ?? color.R ?? 0),
        g: Number(color.g ?? color.G ?? 0),
        b: Number(color.b ?? color.B ?? 0)
    };
}

function tryParseJson(text: string): unknown {
    if (!text || !text.trim()) {
        return undefined;
    }
    try {
        return JSON.parse(text);
    } catch {
        return undefined;
    }
}

const port = Number(process.env.PORT || 8788);
serve({
    port,
    fetch: app.fetch
}, (info) => {
    console.log(`Server is running on http://localhost:${info.port}`)
})