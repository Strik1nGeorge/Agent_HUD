import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import { fileURLToPath } from 'node:url';

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const bridge = path.join(root, 'scripts', 'claude-statusline.mjs').replaceAll('\\', '/');
const command = `"${process.execPath.replaceAll('\\', '/')}" "${bridge}"`;
const settingsPath = path.join(process.env.CLAUDE_CONFIG_DIR || path.join(os.homedir(), '.claude'), 'settings.json');
let settings = {};
try { settings = JSON.parse(await fs.readFile(settingsPath, 'utf8')); }
catch (e) { if (e.code !== 'ENOENT') throw new Error('Claude settings.json 无法解析；未修改文件。'); }
const remove = process.argv.includes('--remove');
if (remove) {
  if (settings.statusLine?.command === command) {
    delete settings.statusLine;
  } else {
    console.log('当前状态栏不属于 Agent Meter，未修改。');
    process.exit(0);
  }
} else {
  if (settings.statusLine?.command === command) { console.log('Claude Code 已接入 Agent Meter。'); process.exit(0); }
  if (settings.statusLine) {
    console.error('已有自定义状态栏，未覆盖。请把 scripts/claude-statusline.mjs 加到现有状态栏脚本中，向它传递同一份 stdin JSON。');
    process.exit(1);
  }
  settings.statusLine = { type: 'command', command };
}
await fs.mkdir(path.dirname(settingsPath), { recursive: true });
const backup = `${settingsPath}.agent-hud-${Date.now()}.bak`;
try { await fs.copyFile(settingsPath, backup); } catch (e) { if (e.code !== 'ENOENT') throw e; }
const temp = `${settingsPath}.agent-hud.tmp`;
await fs.writeFile(temp, JSON.stringify(settings, null, 2) + '\n', { mode: 0o600 });
await fs.rename(temp, settingsPath);
console.log(remove ? '已移除 Agent Meter 状态栏，其他设置保留。' : 'Claude Code 已接入。下次收到模型响应后，HUD 将显示可用的账户额度。');
console.log(`设置备份：${backup}`);
