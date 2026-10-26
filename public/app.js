import { t, lang, setLang, server, rangeLabel, locale, applyStatic } from '/i18n.js';
const $ = selector => document.querySelector(selector);
const $$ = selector => [...document.querySelectorAll(selector)];
const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const compact = new URLSearchParams(location.search).has('compact');
if (compact) document.body.classList.add('compact');
// online: null until the first fetch settles, so the nav can say "connecting" rather than "disconnected".
const state = { data: null, range: '7d', provider: 'all', query: '', view: 'overview', includeSystem: false, sort: 'recent', rows: [], online: null, loading: false, detailId: null, group: 'model' };
try { const group = localStorage.getItem('agent-hud-group'); if (group === 'effort' || group === 'project') state.group = group; } catch {}
const number = n => new Intl.NumberFormat('en-US', { notation: n >= 10000 ? 'compact' : 'standard', maximumFractionDigits: 1 }).format(n);
const exact = n => new Intl.NumberFormat('en-US').format(Math.round(n || 0));
const percent = n => `${Number(n.toFixed(1))}%`;
const USAGE_PAGES = { codex: 'https://chatgpt.com/codex/settings/usage', claude: 'https://claude.ai/settings/usage' };
// Platform marks on a 24×24 grid, from LobeHub icons (MIT); the marks are trademarks of OpenAI and Anthropic.
// Codex uses its brand gradient (#codex-brand in index.html), Claude its clay colour. Same paths as desktop/Logos.cs.
const LOGOS = {
  codex: '<svg class="brand-mark" viewBox="0 0 24 24" aria-hidden="true"><path fill="url(#codex-brand)" fill-rule="evenodd" d="M8.086 0.457a6.105 6.105 0 0 1 3.046-0.415c1.333 0.153 2.521 0.72 3.564 1.7a0.117 0.117 0 0 0 0.107 0.029c1.408-0.346 2.762-0.224 4.061 0.366l0.063 0.03l0.154 0.076c1.357 0.703 2.33 1.77 2.918 3.198c0.278 0.679 0.418 1.388 0.421 2.126a5.655 5.655 0 0 1-0.18 1.631a0.167 0.167 0 0 0 0.04 0.155a5.982 5.982 0 0 1 1.578 2.891c0.385 1.901-0.01 3.615-1.183 5.14l-0.182 0.22a6.063 6.063 0 0 1-2.934 1.851a0.162 0.162 0 0 0-0.108 0.102c-0.255 0.736-0.511 1.364-0.987 1.992c-1.199 1.582-2.962 2.462-4.948 2.451c-1.583-0.008-2.986-0.587-4.21-1.736a0.145 0.145 0 0 0-0.14-0.032c-0.518 0.167-1.04 0.191-1.604 0.185a5.924 5.924 0 0 1-2.595-0.622a6.058 6.058 0 0 1-2.146-1.781c-0.203-0.269-0.404-0.522-0.551-0.821a7.74 7.74 0 0 1-0.495-1.283a6.11 6.11 0 0 1-0.017-3.064a0.166 0.166 0 0 0 0.008-0.074a0.115 0.115 0 0 0-0.037-0.064a5.958 5.958 0 0 1-1.38-2.202a5.196 5.196 0 0 1-0.333-1.589a6.915 6.915 0 0 1 0.188-2.132c0.45-1.484 1.309-2.648 2.577-3.493c0.282-0.188 0.55-0.334 0.802-0.438c0.286-0.12 0.573-0.22 0.861-0.304a0.129 0.129 0 0 0 0.087-0.087A6.016 6.016 0 0 1 5.635 2.31C6.315 1.464 7.132 0.846 8.086 0.457z m-0.804 7.85a0.848 0.848 0 0 0-1.473 0.842l1.694 2.965l-1.688 2.848a0.849 0.849 0 0 0 1.46 0.864l1.94-3.272a0.849 0.849 0 0 0 0.007-0.854l-1.94-3.393z m5.446 6.24a0.849 0.849 0 0 0 0 1.695h4.848a0.849 0.849 0 0 0 0-1.696h-4.848z"/></svg>',
  claude: '<svg class="brand-mark" viewBox="0 0 24 24" aria-hidden="true"><path fill="#d97757" d="M4.709 15.955l4.72-2.647l0.08-0.23l-0.08-0.128H9.2l-0.79-0.048l-2.698-0.073l-2.339-0.097l-2.266-0.122l-0.571-0.121L0 11.784l0.055-0.352l0.48-0.321l0.686 0.06l1.52 0.103l2.278 0.158l1.652 0.097l2.449 0.255h0.389l0.055-0.157l-0.134-0.098l-0.103-0.097l-2.358-1.596l-2.552-1.688l-1.336-0.972l-0.724-0.491l-0.364-0.462l-0.158-1.008l0.656-0.722l0.881 0.06l0.225 0.061l0.893 0.686l1.908 1.476l2.491 1.833l0.365 0.304l0.145-0.103l0.019-0.073l-0.164-0.274l-1.355-2.446l-1.446-2.49l-0.644-1.032l-0.17-0.619a2.97 2.97 0 0 1-0.104-0.729L6.283 0.134L6.696 0l0.996 0.134l0.42 0.364l0.62 1.414l1.002 2.229l1.555 3.03l0.456 0.898l0.243 0.832l0.091 0.255h0.158V9.01l0.128-1.706l0.237-2.095l0.23-2.695l0.08-0.76l0.376-0.91l0.747-0.492l0.584 0.28l0.48 0.685l-0.067 0.444l-0.286 1.851l-0.559 2.903l-0.364 1.942h0.212l0.243-0.242l0.985-1.306l1.652-2.064l0.73-0.82l0.85-0.904l0.547-0.431h1.033l0.76 1.129l-0.34 1.166l-1.064 1.347l-0.881 1.142l-1.264 1.7l-0.79 1.36l0.073 0.11l0.188-0.02l2.856-0.606l1.543-0.28l1.841-0.315l0.833 0.388l0.091 0.395l-0.328 0.807l-1.969 0.486l-2.309 0.462l-3.439 0.813l-0.042 0.03l0.049 0.061l1.549 0.146l0.662 0.036h1.622l3.02 0.225l0.79 0.522l0.474 0.638l-0.079 0.485l-1.215 0.62l-1.64-0.389l-3.829-0.91l-1.312-0.329h-0.182v0.11l1.093 1.068l2.006 1.81l2.509 2.33l0.127 0.578l-0.322 0.455l-0.34-0.049l-2.205-1.657l-0.851-0.747l-1.926-1.62h-0.128v0.17l0.444 0.649l2.345 3.521l0.122 1.08l-0.17 0.353l-0.608 0.213l-0.668-0.122l-1.374-1.925l-1.415-2.167l-1.143-1.943l-0.14 0.08l-0.674 7.254l-0.316 0.37l-0.729 0.28l-0.607-0.461l-0.322-0.747l0.322-1.476l0.389-1.924l0.315-1.53l0.286-1.9l0.17-0.632l-0.012-0.042l-0.14 0.018l-1.434 1.967l-2.18 2.945l-1.726 1.845l-0.414 0.164l-0.717-0.37l0.067-0.662l0.401-0.589l2.388-3.036l1.44-1.882l0.93-1.086l-0.006-0.158h-0.055L4.132 18.56l-1.13 0.146l-0.487-0.456l0.061-0.746l0.231-0.243l1.908-1.312l-0.006 0.006z"/></svg>',
};
const dayFormatter = new Intl.DateTimeFormat('en-US', { timeZone: 'America/New_York', year: 'numeric', month: '2-digit', day: '2-digit' });
const dayCache = new Map();
function calendarDay(time) { if(dayCache.has(time))return dayCache.get(time); const parts = dayFormatter.formatToParts(time); const day = ['year','month','day'].map(type => parts.find(p => p.type === type).value).join('-'); if(dayCache.size>100000)dayCache.clear(); dayCache.set(time,day); return day; }
function offsetDay(key, days) { const d = new Date(`${key}T12:00:00Z`); d.setUTCDate(d.getUTCDate() + days); return d.toISOString().slice(0,10); }
function sinceDay() { const today = calendarDay(Date.now()); return state.range === 'all' ? '0000-00-00' : offsetDay(today, -(state.range === 'today' ? 0 : state.range === '7d' ? 6 : 29)); }
function ago(time) { if (!time) return t('time.none'); const seconds = Math.max(0, Math.floor((Date.now() - time)/1000)); if (seconds < 60) return t('time.justNow'); if (seconds < 3600) return t('time.min', { n: Math.floor(seconds/60) }); if (seconds < 86400) return t('time.hour', { n: Math.floor(seconds/3600) }); return t('time.day', { n: Math.floor(seconds/86400) }); }
function fullTime(time) { return time ? new Intl.DateTimeFormat(locale(), { timeZone: state.data?.timeZone || 'America/New_York', dateStyle: 'short', timeStyle: 'short' }).format(time) : t('time.none'); }
function countdown(time) { if (!time) return t('reset.none'); const mins = Math.ceil((time - Date.now())/60000); if (mins <= 0) return t('reset.passed'); if (mins >= 1440) return t('reset.days', { d: Math.floor(mins/1440), h: Math.floor(mins%1440/60) }); if (mins < 60) return t('reset.minutes', { m: mins }); return t('reset.hours', { h: Math.floor(mins/60), m: mins%60 }); }
function usage(records) { return records.reduce((sum,r) => { for (const k of Object.keys(sum)) sum[k] += r[k] || 0; return sum; }, { input:0, output:0, cached:0, cacheWrite:0, reasoning:0, total:0 }); }
function selectedSessions() {
  if (!state.data) return [];
  const since = sinceDay();
  return state.data.sessions.filter(s => (state.includeSystem || !s.guardian) && (state.provider === 'all' || s.provider === state.provider)).map(s => {
    const records = s.records.filter(r => calendarDay(r.time) >= since);
    return { ...s, records, usage: usage(records) };
  }).filter(s => s.records.length || calendarDay(s.updatedAt || 0) >= since);
}

function renderProviders() {
  const providers = state.data.providers;
  $('#provider-cards').innerHTML = ['codex','claude'].map(id => {
    const p = providers.find(p => p.id === id) || { id, name: id === 'codex' ? 'Codex' : 'Claude Code', limits: null };
    const limits = p.limits;
    const windows = limits?.windows || [];
    const recent = limits && !limits.stale && Date.now() - limits.collectedAt < (limits.staleAfterMs || 120000);
    const plan = limits?.plan ? `<span class="plan">${escapeHtml(limits.plan)}</span>` : '';
    const label = windows.length ? (recent ? t('state.synced') : t('state.snapshot')) : (p.connected ? t('state.connected') : t('state.waiting'));
    const body = windows.length ? `<div class="quota-windows">${windows.map(win => {
      const expired = win.resetsAt && win.resetsAt <= Date.now();
      return `<div class="quota-window"><div class="quota-label"><span>${escapeHtml(server(win.name))}</span><small>${expired ? t('quota.updating') : t('quota.left')}</small></div><div class="quota-number">${expired ? '—' : Number(win.remainingPercent.toFixed(1))}${expired ? '' : '<small>%</small>'}</div><div class="quota-bar ${win.remainingPercent < 20 ? 'low' : ''}"><span style="width:${expired ? 0 : win.remainingPercent}%"></span></div><div class="quota-reset" title="${escapeHtml(fullTime(win.resetsAt))}">${countdown(win.resetsAt)}</div></div>`;
    }).join('')}</div>` : `<div class="unavailable"><strong>${t('quota.unavailable')}</strong><p>${escapeHtml(id === 'claude' ? server(p.quotaMessage) || t('quota.claudeMissing') : t('quota.codexMissing'))}</p>${id === 'claude' ? `<div class="button-row"><a class="connect-button" href="https://claude.ai/settings/usage" target="_blank" rel="noreferrer">${t('quota.claudeUsage')}</a><button class="connect-button secondary" data-setup>${t('quota.setupGuide')}</button></div>` : ''}</div>`;
    return `<article class="provider-card ${id}"><div class="provider-top"><span class="provider-logo ${id}" aria-hidden="true">${LOGOS[id]}</span><span class="provider-name">${p.name}</span>${plan}<span class="provider-state"><i class="${recent ? 'live' : ''}"></i>${label}</span></div>${body}<div class="provider-foot"><span title="${escapeHtml(server(p.quotaError) || '')}">${limits ? `${escapeHtml(server(limits.source))} · ${ago(limits.collectedAt)}` : t('quota.separate')}</span><div class="foot-links"><a class="source-link" href="${USAGE_PAGES[id]}" target="_blank" rel="noreferrer">${t('quota.usagePage')}</a><button class="source-link" data-connections>${t('quota.sources')}</button></div></div></article>`;
  }).join('');
  $$('[data-setup]').forEach(b => b.addEventListener('click', openSetup));
  $$('[data-connections]').forEach(b => b.addEventListener('click', () => switchView('connections')));
}

function renderStats(sessions) {
  const totals = usage(sessions.flatMap(s => s.records));
  const cacheRate = totals.input ? totals.cached / totals.input * 100 : 0;
  const active = sessions.filter(s => s.activity === 'recent').length;
  const stats = [
    [t('stat.total'),number(totals.total),'tokens',t('stat.totalNote', { range: rangeLabel(state.range) })],
    [t('stat.io'),`${number(totals.input)} <small>/</small> ${number(totals.output)}`,'',t('stat.ioNote')],
    [t('stat.cache'),totals.input ? percent(cacheRate) : '—','',t('stat.cacheNote', { n: number(totals.cached) })],
    [t('stat.sessions'),sessions.length,'',t('stat.sessionsNote', { n: active })],
  ];
  $('#summary-stats').innerHTML = stats.map(([label,value,unit,note]) => `<div class="stat"><div class="stat-label">${label}</div><div class="stat-value">${value}${unit ? `<small>${unit}</small>` : ''}</div><div class="stat-note">${note}</div></div>`).join('');
  $('#nav-count').textContent = sessions.length;
}

// Monotone cubic (Fritsch–Carlson): smooth like iOS charts, but never overshoots below zero.
function smoothPath(points) {
  const f = n => Number(n.toFixed(1));
  if (points.length < 2) return points.length ? `M${f(points[0][0])},${f(points[0][1])}` : '';
  const n = points.length, dx = [], m = [], t = [];
  for (let i = 0; i < n-1; i++) { dx[i] = points[i+1][0]-points[i][0]; m[i] = (points[i+1][1]-points[i][1])/dx[i]; }
  t[0] = m[0]; t[n-1] = m[n-2];
  for (let i = 1; i < n-1; i++) t[i] = m[i-1]*m[i] <= 0 ? 0 : (m[i-1]+m[i])/2;
  for (let i = 0; i < n-1; i++) {
    if (m[i] === 0) { t[i] = 0; t[i+1] = 0; continue; }
    const a = t[i]/m[i], b = t[i+1]/m[i], s = a*a+b*b;
    if (s > 9) { const k = 3/Math.sqrt(s); t[i] = k*a*m[i]; t[i+1] = k*b*m[i]; }
  }
  let d = `M${f(points[0][0])},${f(points[0][1])}`;
  for (let i = 0; i < n-1; i++) { const h = dx[i]/3; d += ` C${f(points[i][0]+h)},${f(points[i][1]+t[i]*h)} ${f(points[i+1][0]-h)},${f(points[i+1][1]-t[i+1]*h)} ${f(points[i+1][0])},${f(points[i+1][1])}`; }
  return d;
}

function renderChart(sessions) {
  $('#chart-range').textContent = rangeLabel(state.range);
  const records = sessions.flatMap(s => s.records.map(r => ({ ...r, provider: s.provider })));
  const today = calendarDay(Date.now());
  let start = sinceDay();
  if (state.range === 'all') start = records.length ? records.reduce((min,r) => { const d = calendarDay(r.time); return d < min ? d : min; }, today) : offsetDay(today,-6);
  const totalDays = Math.max(1, Math.round((new Date(`${today}T12:00:00Z`) - new Date(`${start}T12:00:00Z`))/86400000) + 1);
  const step = Math.max(1, Math.ceil(totalDays / 32));
  const buckets = [];
  for (let i=0; i<totalDays; i+=step) buckets.push({ day:offsetDay(start,i), codex:0, claude:0 });
  for (const r of records) {
    const index = Math.floor((new Date(`${calendarDay(r.time)}T12:00:00Z`) - new Date(`${start}T12:00:00Z`))/86400000/step);
    if (buckets[index]) buckets[index][r.provider] += r.total;
  }
  // Draw at the container's real pixel size so dots stay round and labels are not stretched.
  const box = $('#usage-chart');
  const width = Math.max(280, box.clientWidth-40), height = Math.max(150, box.clientHeight-20), left=48, right=12, top=14, bottom=28;
  state.chartWidth = box.clientWidth;
  const max = Math.max(1,...buckets.flatMap(b => [b.codex,b.claude])) * 1.15;
  const x = i => buckets.length===1 ? (width+left)/2 : left+i*(width-left-right)/(buckets.length-1);
  const y = value => height-bottom-value/max*(height-top-bottom);
  const grid = [0,.5,1].map(r => `<line class="chart-grid${r===0?' base':''}" x1="${left}" y1="${y(max*r)}" x2="${width-right}" y2="${y(max*r)}"/><text x="${left-10}" y="${y(max*r)+4}" text-anchor="end">${number(max*r)}</text>`).join('');
  const labelInterval = Math.max(1, Math.ceil(buckets.length/Math.max(3, Math.floor(width/110))));
  const labels = buckets.map((b,i) => i%labelInterval===0 || i===buckets.length-1 ? `<text x="${x(i)}" y="${height-6}" text-anchor="middle">${b.day.slice(5).replace('-','/')}</text>` : '').join('');
  const lines = ['codex','claude'].map(id => {
    const points = buckets.map((b,i) => [x(i), y(b[id])]);
    const path = smoothPath(points);
    const area = points.length>1 ? `<path d="${path} L${x(buckets.length-1)},${height-bottom} L${x(0)},${height-bottom} Z" fill="url(#${id}-fill)"/>` : '';
    return `${area}<path class="chart-line ${id}" d="${path}"/>${buckets.map((b,i) => `<circle class="chart-dot ${id}" cx="${x(i)}" cy="${y(b[id])}" r="${buckets.length>15 ? 2.5 : 3.5}"><title>${id==='codex'?'Codex':'Claude Code'} · ${b.day}${step>1?t('chart.bucket', { n: step }):''} ·${exact(b[id])} tokens</title></circle>`).join('')}`;
  }).join('');
  const fill = id => `<linearGradient id="${id}-fill" x1="0" y1="0" x2="0" y2="1"><stop class="stop ${id}" stop-opacity=".08"/><stop class="stop ${id}" offset="1" stop-opacity="0"/></linearGradient>`;
  box.innerHTML = `<svg viewBox="0 0 ${width} ${height}" role="img" aria-label="${escapeHtml(t('chart.aria', { range: rangeLabel(state.range) }))}"><defs>${fill('codex')}${fill('claude')}</defs>${grid}${lines}${labels}</svg>`;
}

// Usage by model, by thinking effort or by project. Totals come from the request records themselves, so
// a session that switched model or effort mid-way is split correctly. Effort rows are per model (and so
// per platform): the same level on two models, or Codex's and Claude Code's "xhigh", are kept apart.
// Projects are the folder name each session ran in; one project can hold both platforms.
const GROUP_TEXT = { model: ['models.title', 'models.sub'], effort: ['effort.title', 'effort.sub'], project: ['project.title', 'project.sub'] };
const platformName = id => id === 'codex' ? 'Codex' : 'Claude Code';
// Longer lists show the biggest rows and fold the tail into one row that still states its total.
const FOLD_OVER = 10, FOLD_SHOW = 8;
function breakdownHeading() {
  const [title, sub] = GROUP_TEXT[state.group];
  $('#breakdown-title').dataset.i18n = title;
  $('#breakdown-sub').dataset.i18n = sub;
  $('#model-list').dataset.i18nAttr = `aria-label:${title}`;
  $$('[data-group]').forEach(b => b.classList.toggle('selected', b.dataset.group === state.group));
  applyStatic($('.model-panel'));
}
function renderBreakdown(sessions) {
  const mode = state.group;
  const groups = new Map();
  for (const s of sessions) for (const r of s.records) {
    const value = (mode === 'project' ? s.project : mode === 'effort' ? r.effort : r.model) || null;
    const key = mode === 'project' ? value || '' : `${s.provider}\u0000${mode === 'effort' ? r.model || '' : ''}\u0000${value || ''}`;
    let g = groups.get(key);
    if (!g) groups.set(key, g = { model: r.model || null, value, providers: new Set(), parts: { codex: 0, claude: 0 }, input: 0, output: 0, cached: 0, reasoning: 0, total: 0, requests: 0, sessions: new Set() });
    g.providers.add(s.provider); g.parts[s.provider] += r.total || 0;
    g.input += r.input || 0; g.output += r.output || 0; g.cached += r.cached || 0; g.reasoning += r.reasoning || 0; g.total += r.total || 0; g.requests++; g.sessions.add(`${s.provider}:${s.id}`);
  }
  const rows = [...groups.values()].sort((a, b) => b.total - a.total);
  const sum = rows.reduce((n, g) => n + g.total, 0);
  const shareText = share => share > 0 && share < .1 ? '&lt;0.1%' : percent(share);
  const folded = rows.length > FOLD_OVER && !state.breakdownOpen;
  const shown = folded ? rows.slice(0, FOLD_SHOW) : rows;
  $('#model-count').textContent = rows.length;
  const cell = (content, className = '') => `<div role="cell" class="${className}">${content}</div>`;
  const firstHeader = mode === 'project' ? t('project.label') : mode === 'effort' ? t('effort.level') : t('models.model');
  const head = `<div class="model-row head" role="row">${[firstHeader, t('models.share'), 'Tokens', mode === 'effort' ? t('effort.thinking') : t('models.io'), t('models.cache'), t('models.requests')].map(label => `<div role="columnheader">${label}</div>`).join('')}</div>`;
  const body = shown.map(g => {
    const share = sum ? g.total / sum * 100 : 0;
    const provider = g.providers.size > 1 ? 'mixed' : [...g.providers][0];
    const label = mode === 'project' ? server(g.value) || t('time.none') : g.value || t(mode === 'effort' ? 'effort.none' : 'time.none');
    const sessionCount = t(g.sessions.size === 1 ? 'models.session' : 'models.sessions', { n: g.sessions.size });
    const modelName = g.model || t('time.none');
    const title = mode === 'effort' ? t('effort.title.row', { effort: label, model: modelName, total: exact(g.total), thinking: exact(g.reasoning) })
      : t(mode === 'project' ? 'project.title.row' : 'models.title.row', { model: label, project: label, total: exact(g.total), input: exact(g.input), output: exact(g.output) });
    // The logo names the platform; under an effort level the second line names the model instead.
    const sub = mode === 'effort' ? `<span class="model-sub-id">${escapeHtml(modelName)}</span><span class="sub-count"> · ${sessionCount}</span>`
      : `${['codex', 'claude'].filter(id => g.providers.has(id)).map(platformName).join(' + ')} · ${sessionCount}`;
    const icon = provider === 'mixed' ? LOGOS.codex + LOGOS.claude : LOGOS[provider] || '';
    // A project used from both platforms splits its bar by platform colour.
    const bar = provider === 'mixed'
      ? `<span class="stack" style="width:${share.toFixed(2)}%">${['codex', 'claude'].map(id => `<i class="${id}" style="width:${(g.total ? g.parts[id] / g.total * 100 : 0).toFixed(2)}%"></i>`).join('')}</span>`
      : `<span style="width:${share.toFixed(2)}%"></span>`;
    return `<div class="model-row ${provider}" role="row" title="${escapeHtml(title)}">`
      + cell(`<span class="agent-symbol ${provider}" aria-hidden="true">${icon}</span><div class="model-text"><div class="model-id${g.value ? '' : ' unknown'}${mode === 'project' ? ' project-name' : ''}">${escapeHtml(label)}</div><div class="model-sub">${sub}</div></div>`, 'model-name')
      + cell(`<span class="share-track">${bar}</span><span class="share-value">${shareText(share)}</span>`, 'model-share')
      + cell(number(g.total), 'model-tokens')
      + cell(mode === 'effort' ? number(g.reasoning) : `${number(g.input)} / ${number(g.output)}`, 'model-muted')
      + cell(g.input ? percent(g.cached / g.input * 100) : '—', 'model-muted')
      + cell(exact(g.requests), 'model-muted')
      + '</div>';
  }).join('');
  const rest = rows.slice(FOLD_SHOW), restTotal = rest.reduce((n, g) => n + g.total, 0);
  const toggle = rows.length <= FOLD_OVER ? '' : `<button class="model-more" data-breakdown-more aria-expanded="${!folded}">${folded ? t('breakdown.more', { n: rest.length, tokens: number(restTotal), share: shareText(sum ? restTotal / sum * 100 : 0) }) : t('breakdown.less')}</button>`;
  $('#model-list').innerHTML = rows.length ? head + body + toggle : `<div class="model-empty">${state.data.loading ? t('table.scanning') : t('models.empty')}</div>`;
  $('[data-breakdown-more]')?.addEventListener('click', () => { state.breakdownOpen = !state.breakdownOpen; renderBreakdown(selectedSessions()); });
}

function renderSessions(sessions) {
  const q = state.query.toLowerCase().trim();
  const rows = sessions.filter(s => !q || [s.name,s.project,s.model,s.id].some(v => String(v || '').toLowerCase().includes(q)));
  rows.sort(state.sort === 'tokens' ? (a,b) => b.usage.total-a.usage.total : (a,b) => b.updatedAt-a.updatedAt);
  state.rows=rows;
  $('#session-count').textContent=rows.length;
  $('#table-note').textContent=t('table.note', { range: rangeLabel(state.range), n: rows.length });
  $('#sessions-body').innerHTML = rows.length ? rows.map(s => {
    const ctx = s.context;
    const ctxPercent = ctx && Number.isFinite(ctx.percent) ? ctx.percent : null;
    const cache = s.usage.input ? percent(s.usage.cached/s.usage.input*100) : '—';
    return `<tr><td><div class="agent-cell"><span class="agent-symbol ${s.provider}" aria-hidden="true">${LOGOS[s.provider]||''}</span><div class="agent-title"><button data-detail="${escapeHtml(s.provider+':'+s.id)}" title="${escapeHtml(s.name)}">${escapeHtml(s.name)}</button><div class="agent-subtitle"><span>${escapeHtml(server(s.project))}</span>${s.kind==='subagent'?`<span class="subagent-label">${t('row.subagent')}</span>`:''}${s.guardian?`<span class="subagent-label">${t('row.guardian')}</span>`:''}</div></div></div></td><td><span class="model-label">${escapeHtml(s.model||t('time.none'))}</span></td><td class="token-cell" title="${exact(s.usage.total)} tokens">${s.records.length?number(s.usage.total):'—'}</td><td class="cache-cell">${cache}</td><td><div class="context-cell" title="${t('row.contextTitle')}">${ctxPercent==null?t('time.none'):`<span class="context-track"><span style="width:${ctxPercent}%"></span></span>${Math.round(ctxPercent)}%`}</div></td><td class="activity-cell ${s.activity}" title="${escapeHtml(fullTime(s.updatedAt))}">${s.activity==='recent'?`<span class="live-dot"></span>${t('row.active')}`:ago(s.updatedAt)}</td><td><button class="row-open" aria-label="${escapeHtml(t('row.open', { name: s.name }))}" data-detail="${escapeHtml(s.provider+':'+s.id)}"><svg viewBox="0 0 24 24"><path d="m9.5 6 6 6-6 6"/></svg></button></td></tr>`;
  }).join('') : `<tr class="empty-row"><td colspan="7">${state.data.loading?t('table.scanning'):t('table.empty')}</td></tr>`;
  $$('[data-detail]').forEach(b=>b.addEventListener('click',()=>openDetail(b.dataset.detail)));
}

function openDetail(id) {
  const s=selectedSessions().find(s=>`${s.provider}:${s.id}`===id);
  if(!s)return;
  state.detailId=id;
  const fields=[[t('detail.input'),s.usage.input],[t('detail.output'),s.usage.output],[t('detail.cached'),s.usage.cached],[t('detail.cacheWrite'),s.usage.cacheWrite],[t('detail.reasoning'),s.usage.reasoning],[t('detail.total'),s.usage.total]];
  $('#detail-content').innerHTML=`<h2>${escapeHtml(s.name)}</h2><p>${s.provider==='codex'?'Codex':'Claude Code'} · ${escapeHtml(server(s.project))} · ${rangeLabel(state.range)}</p><div class="detail-grid">${fields.map(([label,n])=>`<div class="detail-stat"><label>${label}</label><strong>${exact(n)}</strong></div>`).join('')}</div><div class="detail-meta"><span>${t('detail.id')}</span><code>${escapeHtml(s.id)}</code><span>${t('detail.model')}</span><span>${escapeHtml(s.model||t('time.none'))}</span><span>${t('detail.parent')}</span><code>${escapeHtml(s.parentId||t('detail.parentNone'))}</code><span>${t('detail.last')}</span><span>${fullTime(s.updatedAt)}</span><span>${t('detail.requests')}</span><span>${t('detail.requestCount', { n: s.records.length })}</span></div><p>${t('detail.note')}${s.guardian?(lang==='en'?' ':'')+t('detail.guardianNote'):''}</p>`;
  if(!$('#detail-dialog').classList.contains('open')) openDialog($('#detail-dialog'));
}
// Glass sheets materialize (blur + scale + opacity together) and leave along the same path.
// Transitions rather than keyframes, so re-opening mid-close reverses from where it is.
function openDialog(dialog){
  clearTimeout(dialog.closeTimer);
  dialog.classList.remove('closing');
  if(!dialog.open){dialog.showModal();dialog.getBoundingClientRect();}
  dialog.classList.add('open');
}
function closeDialog(dialog){
  if(!dialog.open)return;
  dialog.classList.add('closing');dialog.classList.remove('open');
  clearTimeout(dialog.closeTimer);
  dialog.closeTimer=setTimeout(()=>{dialog.classList.remove('closing');dialog.close();},270);
}

function renderConnections() {
  $('#connections-section').innerHTML = `<div class="provider-grid">${state.data.providers.map(p=>`<article class="panel connection-panel"><div class="row"><h2>${escapeHtml(p.name)}</h2><span class="connection-status">${p.connected?t('conn.found', { n: p.fileCount }):t('conn.none')}</span></div><p>${p.id==='codex'?t('conn.codexDesc'):t('conn.claudeDesc')}</p><p>${t('conn.quota')}${p.limits?.windows.length?`${escapeHtml(server(p.limits.source))} · ${ago(p.limits.collectedAt)}`:t('conn.waiting')}</p>${p.id==='claude'?`<button class="quiet-button" data-setup>${t('conn.setup')}</button>`:`<a href="https://learn.chatgpt.com/docs/app-server" target="_blank" rel="noreferrer">${t('conn.appServer')}</a>`}${p.error?`<p>${escapeHtml(server(p.error))}</p>`:''}</article>`).join('')}</div><div class="panel connection-panel source-description">${t('conn.explainer')}</div>`;
  $$('#connections-section [data-setup]').forEach(b=>b.addEventListener('click',openSetup));
}

function viewText() {
  const view=state.view;
  $('#breadcrumb-current').textContent=t(`view.${view}`);
  $('#page-title').textContent=view==='overview'&&compact?'Agent Meter':t(`title.${view}`);
  $('#page-subtitle').textContent=t(`sub.${view}`);
}
function switchView(view) {
  state.view=view;
  $$('.nav-item').forEach(b=>{b.classList.toggle('active',b.dataset.view===view);b.setAttribute('aria-current',b.dataset.view===view?'page':'false');});
  viewText();
  $('#overview-section').hidden=view!=='overview';
  $('#sessions-section').hidden=view==='connections';
  $('#connections-section').hidden=view!=='connections';
  $('.range-control').hidden=view==='connections';
  render();
  placeThumbs();
}
// The single white pill that slides under the selected segment.
function placeThumb(group) {
  const thumb=group.querySelector('.seg-thumb'), active=group.querySelector('.selected');
  if(!thumb||!active||!active.offsetWidth)return;
  thumb.style.setProperty('--x',`${active.offsetLeft}px`);thumb.style.setProperty('--y',`${active.offsetTop}px`);
  thumb.style.setProperty('--w',`${active.offsetWidth}px`);thumb.style.setProperty('--h',`${active.offsetHeight}px`);
  if(!thumb.classList.contains('ready'))requestAnimationFrame(()=>requestAnimationFrame(()=>thumb.classList.add('ready')));
}
function placeThumbs(){ $$('.seg').forEach(placeThumb); }
const themeIcons={
  auto:'<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="8"/><path class="filled" d="M12 4a8 8 0 0 1 0 16z"/></svg>',
  light:'<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="4"/><path d="M12 2.5v2M12 19.5v2M4.6 4.6 6 6M18 18l1.4 1.4M2.5 12h2M19.5 12h2M4.6 19.4 6 18M18 6l1.4-1.4"/></svg>',
  dark:'<svg viewBox="0 0 24 24"><path d="M19.5 14.5A8 8 0 0 1 9.5 4.5a8 8 0 1 0 10 10z"/></svg>',
};
let theme='auto';
try{const stored=localStorage.getItem('agent-hud-theme');if(themeIcons[stored])theme=stored;}catch{}
function applyTheme(){
  if(theme==='auto')delete document.documentElement.dataset.theme;else document.documentElement.dataset.theme=theme;
  const button=$('#theme-button');button.innerHTML=themeIcons[theme];button.title=t('theme.title', { name: t(`theme.${theme}`) });button.setAttribute('aria-label',button.title);
}
$('#theme-button').addEventListener('click',()=>{theme={auto:'light',light:'dark',dark:'auto'}[theme];try{localStorage.setItem('agent-hud-theme',theme);}catch{}applyTheme();toast(t('theme.toast', { name: t(`theme.${theme}`) }));});
// Everything with text, re-run after a language switch. Rendered parts redraw from the last snapshot.
function applyLanguage(){
  applyStatic();
  applyTheme();
  viewText();
  renderStatus();
  if(setupFailed)$('#setup-command').textContent=t('setup.fallback');
  else if(!setupCommand)$('#setup-command').textContent=t('loading');
  render();
  placeThumbs();
}
$('#lang-button').addEventListener('click',()=>{setLang(lang==='en'?'zh':'en');applyLanguage();toast(t('lang.toast'));});
function render() {
  if(!state.data)return;
  const sessions=selectedSessions();
  renderProviders();renderStats(sessions);renderChart(sessions);renderBreakdown(sessions);renderSessions(sessions);renderConnections();
  $('#last-sync').textContent=state.data.collectedAt?t('footer.lastScan', { time: fullTime(state.data.collectedAt) }):t('footer.scanning');
  if(state.detailId&&$('#detail-dialog').classList.contains('open'))openDetail(state.detailId);
}
function renderStatus(){
  const status=$('#connection-state');
  status.classList.toggle('offline',state.online===false);
  status.innerHTML=`<span class="live-dot"></span>${state.online===null?t('sync.connecting'):state.online?(state.data.loading?t('sync.scanning'):t('sync.live')):t('sync.offline')}`;
  const error=state.online===false?t('error.offline'):server(state.data?.error)||'';
  $('#error-banner').hidden=!error;
  $('#error-banner').textContent=error;
}

async function refresh() {
  if(state.loading)return;
  state.loading=true;
  $('#refresh-button').disabled=true;
  try {
    const response=await fetch('/api/snapshot',{signal:AbortSignal.timeout(10000)});
    if(!response.ok)throw new Error('http');
    state.data=await response.json();state.online=true;
    renderStatus();
    render();
  }catch{
    state.online=false;
    renderStatus();
  }finally{state.loading=false;$('#refresh-button').disabled=false;}
}
let setupCommand='',setupFailed=false;
async function openSetup(){
  openDialog($('#setup-dialog'));
  try{const res=await fetch('/api/setup');if(!res.ok)throw new Error();setupCommand=(await res.json()).claudeCommand;setupFailed=false;$('#setup-command').textContent=setupCommand;}catch{setupFailed=!setupCommand;if(setupFailed)$('#setup-command').textContent=t('setup.fallback');}
}
let toastTimer;
function toast(message){$('#toast').textContent=message;$('#toast').hidden=false;clearTimeout(toastTimer);toastTimer=setTimeout(()=>$('#toast').hidden=true,3000);}
function csvCell(value){let str=String(value??'');if(/^[=+@\-\t\r]/.test(str))str="'"+str;return '"'+str.replaceAll('"','""')+'"';}
$('#export-button').addEventListener('click',()=>{
  if(!state.rows.length){toast(t('toast.nothing'));return;}
  const rows=[t('csv.headers').split('|'),...state.rows.map(s=>[s.provider,s.name,server(s.project),s.model,rangeLabel(state.range),s.usage.input,s.usage.output,s.usage.cached,s.usage.cacheWrite,s.usage.total,fullTime(s.updatedAt)])];
  const url=URL.createObjectURL(new Blob(['\ufeff'+rows.map(r=>r.map(csvCell).join(',')).join('\r\n')],{type:'text/csv;charset=utf-8'}));
  const a=document.createElement('a');a.href=url;a.download=`agent-meter-${calendarDay(Date.now())}.csv`;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);toast(t('toast.exported'));
});
$('#copy-setup').addEventListener('click',async()=>{try{await navigator.clipboard.writeText(setupCommand||'node scripts/connect-claude.mjs');toast(t('toast.copied'));}catch{toast(t('toast.clipboard'));}});
$$('[data-range]').forEach(b=>b.addEventListener('click',()=>{state.range=b.dataset.range;$$('[data-range]').forEach(x=>x.classList.toggle('selected',x===b));render();placeThumbs();}));
$('#search-input').addEventListener('input',e=>{state.query=e.target.value;if(state.data)renderSessions(selectedSessions());});
$('#include-system').addEventListener('change',e=>{state.includeSystem=e.target.checked;render();});
$$('[data-provider]').forEach(b=>b.addEventListener('click',()=>{state.provider=b.dataset.provider;$$('[data-provider]').forEach(x=>x.classList.toggle('selected',x===b));render();placeThumbs();}));
$$('[data-view]').forEach(b=>b.addEventListener('click',()=>switchView(b.dataset.view)));
$$('[data-group]').forEach(b=>b.addEventListener('click',()=>{state.group=b.dataset.group;state.breakdownOpen=false;try{localStorage.setItem('agent-hud-group',state.group);}catch{}breakdownHeading();if(state.data)renderBreakdown(selectedSessions());placeThumbs();}));
$('#sort-tokens').addEventListener('click',()=>{state.sort=state.sort==='tokens'?'recent':'tokens';$('#sort-tokens span').textContent=state.sort==='tokens'?'↓':'↕';render();});
$('#refresh-button').addEventListener('click',refresh);
$('#compact-button').addEventListener('click',()=>{if(compact){location.href='/';return;}const win=window.open('/?compact=1','agent-hud-compact','width=450,height=780,resizable=yes,scrollbars=yes');if(!win)toast(t('compact.blocked'));});
$('#close-detail').addEventListener('click',()=>closeDialog($('#detail-dialog')));
$('#detail-dialog').addEventListener('close',()=>state.detailId=null);
$('#close-setup').addEventListener('click',()=>closeDialog($('#setup-dialog')));
$$('dialog').forEach(dialog=>{
  dialog.addEventListener('cancel',e=>{e.preventDefault();closeDialog(dialog);});
  dialog.addEventListener('click',e=>{if(e.target===dialog){const r=dialog.getBoundingClientRect();if(e.clientX<r.left||e.clientX>r.right||e.clientY<r.top||e.clientY>r.bottom)closeDialog(dialog);}});
});
breakdownHeading();
applyLanguage();
// Scroll edge: the nav hairline appears only once content passes beneath it.
const syncNavEdge=()=>$('#nav').classList.toggle('scrolled',scrollY>8);
addEventListener('scroll',syncNavEdge,{passive:true});syncNavEdge();
placeThumbs();document.fonts?.ready.then(placeThumbs);
let resizeFrame;
window.addEventListener('resize',()=>{cancelAnimationFrame(resizeFrame);resizeFrame=requestAnimationFrame(()=>{placeThumbs();if(state.data&&$('#usage-chart').clientWidth!==state.chartWidth)renderChart(selectedSessions());});});
void refresh();setInterval(()=>{if(!document.hidden)void refresh();},5000);
document.addEventListener('visibilitychange',()=>{if(!document.hidden)void refresh();});
