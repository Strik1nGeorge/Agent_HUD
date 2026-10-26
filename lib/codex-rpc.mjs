import { spawn } from 'node:child_process';
import readline from 'node:readline';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { codexLimits } from './metrics.mjs';

function executable() {
  if (process.env.CODEX_BINARY) return process.env.CODEX_BINARY;
  if (process.platform === 'win32') {
    // Codex Desktop can bundle the executable without adding it to the system PATH.
    const base = path.join(process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local'), 'OpenAI', 'Codex', 'bin');
    try {
      const candidates = fs.readdirSync(base).map(n => path.join(base, n, 'codex.exe')).filter(p => fs.existsSync(p));
      candidates.sort((a,b) => fs.statSync(b).mtimeMs - fs.statSync(a).mtimeMs);
      if (candidates.length) return candidates[0];
    } catch {}
    return 'codex.exe';
  }
  return 'codex';
}

export class CodexQuota {
  constructor() { this.pending = new Map(); this.sequence = 0; this.snapshot = null; this.error = null; this.lastAttempt = 0; this.closed = false; }

  request(method, params = {}) {
    return new Promise((resolve,reject) => {
      const id = ++this.sequence;
      const timeout = setTimeout(() => { this.pending.delete(id); reject(new Error('读取 Codex 额度超时')); }, 12000);
      this.pending.set(id, { resolve, reject, timeout });
      this.proc.stdin.write(JSON.stringify({ id, method, params }) + '\n', error => {
        if (error) { clearTimeout(timeout); this.pending.delete(id); reject(error); }
      });
    });
  }

  async connect() {
    if (this.proc && this.ready) return;
    this.proc = spawn(executable(), ['app-server', '--listen', 'stdio://'], { windowsHide: true, stdio: ['pipe','pipe','pipe'] });
    this.proc.stderr.on('data', () => {});
    this.proc.stdin.on('error', () => {});
    const fail = () => {
      this.ready = false;
      for (const { reject, timeout } of this.pending.values()) { clearTimeout(timeout); reject(new Error('Codex 额度连接不可用')); }
      this.pending.clear();
    };
    this.proc.on('error', fail);
    this.proc.on('exit', fail);
    readline.createInterface({ input: this.proc.stdout }).on('line', line => {
      try {
        const msg = JSON.parse(line), req = this.pending.get(msg.id);
        if (!req) return;
        clearTimeout(req.timeout); this.pending.delete(msg.id);
        if (msg.error) req.reject(new Error('Codex 暂未提供额度，请检查登录状态'));
        else req.resolve(msg.result);
      } catch {}
    });
    await this.request('initialize', { clientInfo: { name: 'agent_meter', title: 'Agent Meter', version: '1.0.0' } });
    this.proc.stdin.write(JSON.stringify({ method: 'initialized', params: {} }) + '\n');
    this.ready = true;
  }

  async refresh() {
    if (this.closed || this.inFlight || Date.now() - this.lastAttempt < 60000) return;
    this.lastAttempt = Date.now();
    this.inFlight = true;
    try {
      await this.connect();
      const raw = await this.request('account/rateLimits/read');
      this.snapshot = codexLimits(raw, Date.now(), 'Codex 官方额度接口');
      this.error = null;
    } catch {
      this.error = '官方额度接口暂不可用，显示最近会话快照';
      this.proc?.kill(); this.proc = null; this.ready = false;
    } finally { this.inFlight = false; }
  }

  close() { this.closed = true; this.proc?.kill(); }
}
