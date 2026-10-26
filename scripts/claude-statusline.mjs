import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { normalizeWindow } from '../lib/metrics.mjs';

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
let input = '';
for await (const chunk of process.stdin) {
  input += chunk;
  if (input.length > 1024 * 1024) process.exit(0);
}
try {
  const data = JSON.parse(input);
  const sessionId = String(data.session_id || 'current').replace(/[^a-zA-Z0-9_-]/g, '').slice(0, 100);
  const limits = {};
  for (const key of ['five_hour', 'seven_day', 'spend_limit']) {
    const raw = data.rate_limits?.[key];
    const valid = normalizeWindow(raw, key);
    if (valid) limits[key] = { used_percentage: valid.usedPercent, resets_at: valid.resetsAt ? valid.resetsAt / 1000 : null };
  }
  const cw = data.context_window;
  const percent = cw?.used_percentage;
  const context = typeof percent === 'number' && Number.isFinite(percent) ? {
    percent: Math.max(0, Math.min(100, percent)), size: Number(cw.context_window_size) || null,
    used: Number(cw.total_input_tokens) || null, source: 'Claude Code 状态栏',
  } : null;
  const dir = process.env.AGENT_HUD_DATA_DIR || path.join(root, '.data');
  await fs.mkdir(dir, { recursive: true });
  const target = path.join(dir, `claude-status-${sessionId}.json`);
  const tmp = `${target}.${process.pid}.tmp`;
  // Only the allowlisted counters are persisted, never the full status-line payload.
  await fs.writeFile(tmp, JSON.stringify({ session_id: sessionId, collectedAt: Date.now(), rate_limits: limits, context }), { mode: 0o600 });
  await fs.rename(tmp, target);
  const parts = ['Agent Meter'];
  if (limits.five_hour) parts.push(`5h left ${Math.round(100 - limits.five_hour.used_percentage)}%`);
  if (limits.seven_day) parts.push(`7d left ${Math.round(100 - limits.seven_day.used_percentage)}%`);
  if (context) parts.push(`ctx ${Math.round(context.percent)}%`);
  process.stdout.write(parts.join(' | '));
} catch {
  // A status-line failure must never interrupt Claude Code.
  process.stdout.write('Agent Meter | waiting for usage');
}
