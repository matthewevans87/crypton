#!/usr/bin/env node
// Crypton Bird Server — thin HTTP wrapper around the `bird` CLI.
// Listens on BIRD_HOST:BIRD_PORT (default 127.0.0.1:11435).
// POST /execute  { args: "search --json -n 10 bitcoin" }  → { stdout, stderr, exitCode }
// GET  /health   → 200 OK

const { spawn } = require("node:child_process");
const http = require("node:http");

const HOST = process.env.BIRD_HOST ?? "0.0.0.0";
const PORT = parseInt(process.env.BIRD_PORT ?? "11435", 10);
const TIMEOUT_MS = parseInt(process.env.BIRD_TIMEOUT_MS ?? "30000", 10);

function execBird(args, timeoutMs) {
  return new Promise((resolve) => {
    const parts = args.match(/"[^"]*"|\S+/g) ?? [];
    const child = spawn("bird", parts, {
      stdio: ["ignore", "pipe", "pipe"],
      timeout: timeoutMs,
    });

    const stdout = [];
    const stderr = [];
    child.stdout.on("data", (d) => stdout.push(d));
    child.stderr.on("data", (d) => stderr.push(d));

    child.on("error", (err) => {
      resolve({ stdout: "", stderr: err.message, exitCode: 1 });
    });

    child.on("close", (code) => {
      resolve({
        stdout: Buffer.concat(stdout).toString("utf8"),
        stderr: Buffer.concat(stderr).toString("utf8"),
        exitCode: code ?? 1,
      });
    });
  });
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    req.on("data", (c) => chunks.push(c));
    req.on("end", () => {
      try {
        resolve(JSON.parse(Buffer.concat(chunks).toString("utf8")));
      } catch {
        reject(new Error("Invalid JSON"));
      }
    });
    req.on("error", reject);
  });
}

const server = http.createServer(async (req, res) => {
  if (req.method === "GET" && req.url === "/health") {
    const authToken = process.env.BIRD_AUTH_TOKEN;
    const ct0 = process.env.BIRD_CT0;
    if (!authToken || !ct0) {
      res.writeHead(503, { "Content-Type": "application/json" });
      res.end(JSON.stringify({
        status: "unavailable",
        reason: "BIRD_AUTH_TOKEN and/or BIRD_CT0 environment variables are not set. Run 'make extract-tokens' in Crypton.Api.Bird/ to provision credentials."
      }));
      return;
    }
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end('{"status":"ok"}');
    return;
  }

  if (req.method === "POST" && req.url === "/execute") {
    let body;
    try {
      body = await readBody(req);
    } catch {
      res.writeHead(400, { "Content-Type": "application/json" });
      res.end('{"error":"Invalid JSON body"}');
      return;
    }

    const args = body.args;
    if (typeof args !== "string" || args.length === 0) {
      res.writeHead(400, { "Content-Type": "application/json" });
      res.end('{"error":"Missing or empty \\"args\\" field"}');
      return;
    }

    const timeoutMs = typeof body.timeoutMs === "number" ? body.timeoutMs : TIMEOUT_MS;
    const result = await execBird(args, timeoutMs);

    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify(result));
    return;
  }

  res.writeHead(404, { "Content-Type": "application/json" });
  res.end('{"error":"Not found"}');
});

server.listen(PORT, HOST, () => {
  console.log(`bird-server listening on ${HOST}:${PORT}`);
});
