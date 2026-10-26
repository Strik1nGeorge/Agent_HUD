import http from 'node:http';
import tls from 'node:tls';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Collector } from './lib/collector.mjs';
import { CodexQuota } from './lib/codex-rpc.mjs';
import { ClaudeDesktopQuota } from './lib/claude-desktop.mjs';
import { providerActivity } from './lib/activity.mjs';

// Match desktop applications' Windows trust store without weakening HTTPS validation.
if (process.platform === 'win32' && typeof tls.getCACertificates === 'function' && typeof tls.setDefaultCACertificates === 'function') {
  tls.setDefaultCACertificates([...tls.getCACertificates('default'), ...tls.getCACertificates('system')]);
}

const root = path.dirname(fileURLToPath(import.meta.url));
const dataDir = process.env.AGENT_HUD_DATA_DIR || path.join(root, '.data');
await fs.mkdir(dataDir, { recursive: true });
const collector = new Collector({ dataDir });
const quota = new CodexQuota();
// Explicit local opt-in: importing the adapter never reads credentials or makes a request.
const claudeDesktopEnabled = await fs.readFile(path.join(dataDir, 'claude-desktop-connection.json'), 'utf8').then(s => JSON.parse(s).enabled === true).catch(() => false);
const claudeQuota = new ClaudeDesktopQuota();
const port = Number(process.env.PORT || 4318);
const assets = { '/': ['index.html', 'text/html; charset=utf-8'], '/app.js': ['app.js', 'text/javascript; charset=utf-8'], '/i18n.js': ['i18n.js', 'text/javascript; charset=utf-8'],'/styles.css': ['styles.css', 'text/css; charset=utf-8'], '/icon.svg': ['icon.svg','image/svg+xml'] };

function snapshot() {
  const data = structuredClone(collector.value);
  const codex = data.providers.find(p => p.id === 'codex');
  if (codex) {
    if (quota.snapshot && (!codex.limits || quota.snapshot.collectedAt > codex.limits.collectedAt)) codex.limits = quota.snapshot;
    codex.quotaError = quota.error;
  }
  const claude = data.providers.find(p => p.id === 'claude');
  if (claude) {
    claude.desktopConnection = { enabled: claudeDesktopEnabled, status: claudeDesktopEnabled ? claudeQuota.status : 'disabled', nextAttemptAt: claudeDesktopEnabled ? claudeQuota.nextAttemptAt : null };
    if (claudeDesktopEnabled) {
      if (claudeQuota.snapshot && (!claude.limits || claudeQuota.snapshot.collectedAt > claude.limits.collectedAt)) claude.limits = claudeQuota.snapshot;
      claude.quotaError = claudeQuota.error;
      claude.quotaMessage = claudeQuota.error;
      claude.quotaStatus = claudeQuota.snapshot ? 'available' : claudeQuota.status;
    } else if (claude.quotaStatus === 'desktop-not-connected') {
      claude.quotaStatus = 'desktop-connection-disabled';
      claude.quotaMessage = '桌面版账户额度读取通道已准备好，尚未启用连接。';
    }
  }
  data.timeZone = 'America/New_York';
  data.refreshSeconds = 5;
  return data;
}

const server = http.createServer(async (req, res) => {
  res.setHeader('Cache-Control', 'no-store');
  res.setHeader('X-Content-Type-Options', 'nosniff');
  res.setHeader('X-Frame-Options', 'DENY');
  res.setHeader('Referrer-Policy', 'no-referrer');
  res.setHeader('Content-Security-Policy', "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; object-src 'none'");
  const allowedHosts = new Set([`127.0.0.1:${port}`, `localhost:${port}`]);
  if (!allowedHosts.has(req.headers.host) || (req.headers.origin && ![`http://127.0.0.1:${port}`, `http://localhost:${port}`].includes(req.headers.origin))) {
    res.writeHead(403); res.end('Local access only'); return;
  }
  if (req.method !== 'GET') { res.writeHead(405, { Allow: 'GET' }); res.end(); return; }
  const url = new URL(req.url, `http://127.0.0.1:${port}`);
  try {
    if (url.pathname === '/api/snapshot') {
      res.setHeader('Content-Type', 'application/json; charset=utf-8');
      res.end(JSON.stringify(snapshot())); return;
    }
    if (url.pathname === '/api/quotas') {
      const data = snapshot();
      res.setHeader('Content-Type', 'application/json; charset=utf-8');
      // The widget gets each platform's live activity (for its pets and hover cards) instead of every session record.
      const now = Date.now();
      const providers = data.providers.map(p => ({ ...p, activity: providerActivity(data.sessions, p.id, now, data.timeZone) }));
      res.end(JSON.stringify({ providers, collectedAt: data.collectedAt, loading: data.loading, timeZone: data.timeZone })); return;
    }
    if (url.pathname === '/api/health') {
      res.setHeader('Content-Type', 'application/json');
      res.end(JSON.stringify({ ok: true, loading: collector.value.loading })); return;
    }
    if (url.pathname === '/api/setup') {
      res.setHeader('Content-Type', 'application/json; charset=utf-8');
      res.end(JSON.stringify({ claudeCommand: `& '${process.execPath.replaceAll("'", "''")}' '${path.join(root, 'scripts', 'connect-claude.mjs').replaceAll("'", "''")}'` })); return;
    }
    const asset = assets[url.pathname];
    if (!asset) { res.writeHead(404); res.end('Not found'); return; }
    res.setHeader('Content-Type', asset[1]);
    res.end(await fs.readFile(path.join(root, 'public', asset[0])));
  } catch { res.writeHead(500); res.end('Temporarily unavailable'); }
});

server.listen(port, '127.0.0.1', () => console.log(`Agent Meter · http://127.0.0.1:${port}`));
server.on('error', e => { console.error(e.code === 'EADDRINUSE' ? `端口 ${port} 已被使用，请设置 PORT 后重试。` : e.message); quota.close(); claudeQuota.close(); process.exit(1); });
const refresh = () => collector.refresh().catch(() => { collector.value.error = '本地数据扫描失败，稍后重试'; });
void refresh();
if (!process.env.AGENT_HUD_DISABLE_RPC) void quota.refresh();
if (claudeDesktopEnabled) void claudeQuota.refresh();
const timer = setInterval(() => { void refresh(); if (!process.env.AGENT_HUD_DISABLE_RPC) void quota.refresh(); if (claudeDesktopEnabled) void claudeQuota.refresh(); }, 5000);
function close() { clearInterval(timer); quota.close(); claudeQuota.close(); server.close(() => process.exit(0)); setTimeout(() => process.exit(0), 1500).unref(); }
process.on('SIGINT', close); process.on('SIGTERM', close);
