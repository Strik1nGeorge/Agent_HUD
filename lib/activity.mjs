// Per-platform activity for the widget: whether an agent is working right now, and today's totals.
// "Working" means a session wrote to its log in the last two minutes and hasn't finished its turn
// (see publicSession's activity); system review agents don't count.
const DAY = 86400000;
const formatters = new Map();
function dayKey(time, timeZone) {
  let format = formatters.get(timeZone);
  if (!format) formatters.set(timeZone, format = new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }));
  return format.format(time);
}

export function providerActivity(sessions, provider, now = Date.now(), timeZone = 'America/New_York') {
  const today = dayKey(now, timeZone);
  let activeSessions = 0, todayTokens = 0, todayRequests = 0, lastActiveAt = null, latest = null;
  for (const session of sessions || []) {
    if (session.provider !== provider || session.guardian) continue;
    if (session.activity === 'recent') activeSessions++;
    if (session.updatedAt && (!lastActiveAt || session.updatedAt > lastActiveAt)) lastActiveAt = session.updatedAt;
    for (const record of session.records || []) {
      if (!record.time) continue;
      if (!latest || record.time > latest.time) latest = record;
      // Only records from the last ~day can be today; skip the date formatting for the rest.
      if (now - record.time < DAY + 3600000 && record.time <= now + 60000 && dayKey(record.time, timeZone) === today) {
        todayTokens += record.total || 0; todayRequests++;
      }
    }
  }
  return { working: activeSessions > 0, activeSessions, todayTokens, todayRequests, lastActiveAt, model: latest?.model ?? null, effort: latest?.effort ?? null };
}
