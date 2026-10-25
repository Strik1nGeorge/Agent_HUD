export const blankUsage = () => ({ input: 0, output: 0, cached: 0, cacheWrite: 0, reasoning: 0, total: 0 });
const count = value => typeof value === 'number' && Number.isFinite(value) ? Math.max(0, value) : 0;
// Thinking effort as the client recorded it (low / medium / high / xhigh / max / ultra …); null when not recorded.
const effortOf = value => typeof value === 'string' && /^[a-z][a-z0-9_-]{0,15}$/i.test(value.trim()) ? value.trim().toLowerCase() : null;

export function usageOf(raw = {}, provider = 'codex') {
  const cached = count(raw.cached_input_tokens ?? raw.cache_read_input_tokens);
  const cacheWrite = count(raw.cache_write_input_tokens ?? raw.cache_creation_input_tokens);
  // Codex includes cache reads/writes in input_tokens; Claude reports them separately.
  const input = count(raw.input_tokens) + (provider === 'claude' ? cached + cacheWrite : 0);
  const output = count(raw.output_tokens);
  return { input, output, cached, cacheWrite, reasoning: count(raw.reasoning_output_tokens ?? raw.output_tokens_details?.thinking_tokens), total: input + output };
}

export function addUsage(a, b) {
  for (const key of Object.keys(blankUsage())) a[key] += b[key] || 0;
  return a;
}

export function difference(a, b) {
  return Object.fromEntries(Object.keys(blankUsage()).map(key => [key, Math.max(0, a[key] - (b[key] || 0))]));
}

export function normalizeWindow(raw, name, defaultMinutes = null) {
  if (!raw) return null;
  const used = raw.usedPercent ?? raw.used_percent ?? raw.used_percentage;
  if (typeof used !== 'number' || !Number.isFinite(used)) return null;
  const resets = raw.resetsAt ?? raw.resets_at;
  return {
    name, usedPercent: Math.max(0, Math.min(100, used)),
    remainingPercent: Math.max(0, Math.min(100, 100 - used)),
    windowMinutes: raw.windowDurationMins ?? raw.window_minutes ?? defaultMinutes,
    resetsAt: typeof resets === 'number' && Number.isFinite(resets) ? resets * 1000 : null,
  };
}

// Window names by length: 5 小时, 7 天, and whatever else a plan reports (the free plan's single 30-day window).
export function windowLabel(minutes) {
  if (!minutes) return null;
  if (minutes % 1440 === 0) return `${minutes / 1440} 天`;
  if (minutes % 60 === 0) return `${minutes / 60} 小时`;
  return `${minutes} 分钟`;
}

export function codexLimits(raw, collectedAt, source) {
  if (!raw) return null;
  const buckets = raw.rateLimitsByLimitId ?? { codex: raw.rateLimits ?? raw };
  const windows = [];
  let plan = null;
  for (const [id, bucket] of Object.entries(buckets)) {
    if (!bucket) continue;
    plan ||= bucket.planType ?? bucket.plan_type;
    const prefix = id === 'codex' ? '' : `${bucket.limitName || bucket.limit_name || id} · `;
    for (const [key, fallback, minutes] of [['primary', '短周期', 300], ['secondary', '长周期', 10080]]) {
      const rawWindow = bucket[key];
      const duration = rawWindow?.windowDurationMins ?? rawWindow?.window_minutes;
      const label = windowLabel(duration) ?? fallback;
      const win = normalizeWindow(rawWindow, prefix + label, duration ?? minutes);
      if (win) windows.push(win);
    }
  }
  return { windows, plan, collectedAt, source };
}

export function claudeLimits(raw, collectedAt) {
  if (!raw) return null;
  return { windows: [normalizeWindow(raw.five_hour, '5 小时', 300), normalizeWindow(raw.seven_day, '7 天', 10080), normalizeWindow(raw.spend_limit, '支出额度')].filter(Boolean), plan: null, collectedAt, source: 'Claude Code 状态栏' };
}

export function newSession(provider, file) {
  return { provider, file, id: null, name: null, project: '', parentId: null, kind: 'session', model: null, effort: null, updatedAt: 0, startedAt: 0, state: 'unknown', context: null, records: new Map(), fallback: new Map(), baseline: blankUsage(), latestLimits: null, warnings: 0 };
}

export function consumeCodex(session, row) {
  const p = row.payload || {};
  const time = Date.parse(row.timestamp) || 0;
  if (row.type === 'session_meta') {
    session.id = p.id;
    session.startedAt = Date.parse(p.timestamp) || time;
    session.project = p.cwd || '';
    session.parentId = p.parent_thread_id || p.source?.subagent?.thread_spawn?.parent_thread_id || null;
    session.kind = p.source?.subagent || session.parentId ? 'subagent' : 'session';
    session.name = p.agent_nickname || p.agent_name || p.agent_path || null;
    session.historyStart = p.subagent_history_start_ordinal;
    session.guardian = p.thread_source === 'guardian_review';
  }
  // Each turn states its model and reasoning effort; the requests that follow inherit both.
  if (row.type === 'turn_context') {
    session.model = p.model || session.model;
    session.effort = effortOf(p.effort ?? p.collaboration_mode?.settings?.reasoning_effort) ?? session.effort;
  }
  if (row.type === 'token_usage_record') {
    if (p.thread_id && p.thread_id !== session.id) return;
    const id = p.response_id || `${p.turn_id}:${row.ordinal ?? time}`;
    session.records.set(id, { id, time, model: session.model, effort: session.effort, ...usageOf(p.usage) });
    session.updatedAt = Math.max(session.updatedAt, time);
  }
  if (row.type !== 'event_msg') return;
  if (p.type === 'task_started') session.state = 'working';
  if (['task_complete', 'task_completed', 'turn_aborted'].includes(p.type)) session.state = 'idle';
  if (p.type === 'token_count') {
    if (p.info?.total_token_usage) {
      const total = usageOf(p.info.total_token_usage);
      const delta = difference(total, session.baseline);
      session.baseline = total;
      const inherited = (session.historyStart != null && row.ordinal < session.historyStart) || time < session.startedAt;
      if (!inherited && delta.total > 0) session.fallback.set(String(row.ordinal ?? time), { id: `${session.id}:${row.ordinal ?? time}`, time, model: session.model, effort: session.effort, ...delta });
      const size = p.info.model_context_window;
      const used = usageOf(p.info.last_token_usage).input;
      if (size && p.info.last_token_usage) session.context = { used, size, percent: Math.min(100, used / size * 100), source: '最近一次模型请求' };
    }
    if (p.rate_limits) session.latestLimits = codexLimits(p.rate_limits, time, 'Codex 会话快照');
  }
  session.updatedAt = Math.max(session.updatedAt, time);
}

export function consumeClaude(session, row) {
  const time = Date.parse(row.timestamp) || 0;
  session.id ||= row.agentId || row.sessionId;
  session.project = row.cwd || session.project;
  if (row.entrypoint) session.entrypoint = row.entrypoint;
  if (row.type === 'custom-title') session.name = row.customTitle;
  if (row.type === 'agent-name' && !session.name) session.name = row.agentName;
  session.updatedAt = Math.max(session.updatedAt, time);
  if (time && (!session.startedAt || time < session.startedAt)) session.startedAt = time;
  if (row.type === 'user') session.state = 'working';
  if (row.type !== 'assistant' || !row.message?.usage) return;
  const msg = row.message;
  session.model = msg.model || session.model;
  session.state = ['end_turn', 'stop_sequence'].includes(msg.stop_reason) ? 'idle' : 'working';
  // A streamed message may be written more than once, with growing usage counters.
  const id = msg.id || row.requestId || row.uuid;
  if (!id || msg.model === '<synthetic>') return;
  const usage = usageOf(msg.usage, 'claude');
  const previous = session.records.get(id);
  if (previous) for (const key of Object.keys(blankUsage())) usage[key] = Math.max(usage[key], previous[key]);
  usage.total = usage.input + usage.output;
  // Claude Code writes the effort of the turn on each assistant row.
  const effort = effortOf(row.effort ?? row.perTurnEffort) ?? previous?.effort ?? null;
  session.records.set(id, { id, time: previous?.time || time, model: msg.model, effort, ...usage });
}

export function sessionRecords(session) {
  if (session.provider !== 'codex' || !session.records.size) return [...(session.records.size ? session.records : session.fallback).values()];
  // Prefer request-level telemetry. Only legacy deltas before it starts are additive.
  const records = [...session.records.values()];
  const first = Math.min(...records.map(r => r.time));
  return [...session.fallback.values()].filter(r => r.time < first).concat(records);
}

export function publicSession(session, names) {
  const { provider, id, parentId, kind, model, updatedAt, startedAt, context, state, guardian } = session;
  const project = session.project.split(/[\\/]/).filter(Boolean).at(-1) || '未命名项目';
  return { provider, id, parentId, kind, model, updatedAt, startedAt, context, guardian: !!guardian,
    name: names?.get(id) || session.name || `${project} · ${(id || '').slice(0, 8)}`, project,
    activity: Date.now() - updatedAt < 120000 ? (state === 'idle' ? 'idle' : 'recent') : 'idle',
    records: sessionRecords(session),
  };
}
