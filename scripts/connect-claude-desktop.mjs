import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const flag = process.argv[2];
if (!['--enable', '--disable'].includes(flag)) {
  console.log('Enable: node scripts/connect-claude-desktop.mjs --enable');
  console.log('Disable: node scripts/connect-claude-desktop.mjs --disable');
  console.log('Enabling permits read-only use of Claude Desktop encrypted sign-in on this Windows account.');
  console.log('The access token is sent only to https://api.anthropic.com/api/oauth/usage.');
  console.log('No token is stored by HUD, no refresh is performed, and no model request is sent.');
  process.exit(0);
}
const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const data = process.env.AGENT_HUD_DATA_DIR || path.join(root, '.data');
await fs.mkdir(data, { recursive: true });
await fs.writeFile(path.join(data, 'claude-desktop-connection.json'), JSON.stringify({ enabled: flag === '--enable' }) + '\n');
console.log(`Claude Desktop quota connection ${flag === '--enable' ? 'enabled' : 'disabled'}. Restart the Agent Meter server to apply.`);
