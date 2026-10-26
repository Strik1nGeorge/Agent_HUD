import test from 'node:test';
import assert from 'node:assert/strict';
import { providerActivity } from '../lib/activity.mjs';

const now = Date.parse('2026-10-26T18:00:00Z'); // 14:00 in New York
const record = (minutesAgo, total, extra = {}) => ({ time: now - minutesAgo * 60000, total, model: 'm', ...extra });

test('Activity counts working sessions and today\'s usage per platform, ignoring system review agents', () => {
  const sessions = [
    { provider: 'claude', activity: 'recent', updatedAt: now - 30000, records: [record(1, 100, { model: 'opus', effort: 'max' }), record(60 * 20, 900)] },
    { provider: 'claude', activity: 'idle', updatedAt: now - 3600000, records: [record(60, 50)] },
    { provider: 'claude', guardian: true, activity: 'recent', updatedAt: now, records: [record(0, 7000)] },
    { provider: 'codex', activity: 'recent', updatedAt: now, records: [record(2, 5)] },
  ];
  const claude = providerActivity(sessions, 'claude', now, 'America/New_York');
  assert.equal(claude.working, true);
  assert.equal(claude.activeSessions, 1);
  // 20 hours ago is yesterday in New York (before midnight), so only the 100 and 50 count.
  assert.equal(claude.todayTokens, 150);
  assert.equal(claude.todayRequests, 2);
  assert.equal(claude.lastActiveAt, now - 30000);
  assert.deepEqual([claude.model, claude.effort], ['opus', 'max']);
  const idle = providerActivity(sessions.slice(1, 2), 'claude', now);
  assert.equal(idle.working, false);
  assert.deepEqual(providerActivity([], 'codex', now), { working: false, activeSessions: 0, todayTokens: 0, todayRequests: 0, lastActiveAt: null, model: null, effort: null });
});
