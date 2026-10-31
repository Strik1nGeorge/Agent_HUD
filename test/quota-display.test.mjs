import test from 'node:test';
import assert from 'node:assert/strict';
import { quotaDisplay } from '../public/quota-display.js';

const now = Date.parse('2026-10-31T12:00:00Z');
const short = () => ({ windowMinutes: 300, remainingPercent: 80, resetsAt: now + 3600000 });
const week = extra => ({ windowMinutes: 10080, remainingPercent: 0, resetsAt: now + 86400000, ...extra });

test('an exhausted account week zeroes 5-hour availability and uses its reset without mutating the source', () => {
  const window = short();
  const weekly = week();
  assert.deepEqual(quotaDisplay(window, [window, weekly], now), { weeklyBlocked: true, remainingPercent: 0, resetsAt: weekly.resetsAt, expired: false });
  assert.equal(window.remainingPercent, 80);
  assert.equal(window.resetsAt, now + 3600000);
  assert.equal(quotaDisplay(weekly, [window, weekly], now).remainingPercent, 0);
});

test('positive, missing, invalid, and expired weekly balances do not block the short window', () => {
  for (const weekly of [week({ remainingPercent: .1 }), week({ remainingPercent: null }), week({ remainingPercent: NaN }), week({ remainingPercent: -Infinity }), week({ resetsAt: now }), week({ resetsAt: now - 1 })]) {
    assert.equal(quotaDisplay(short(), [weekly], now).remainingPercent, 80);
  }
  assert.equal(quotaDisplay(short(), [], now).remainingPercent, 80);
});

test('model-specific weekly limits cannot zero the entire account', () => {
  for (const weekly of [week({ id: 'seven_day_opus', name: 'Opus · 7 天' }), week({ name: 'Sonnet · 7 天' }), week({ name: 'Other models · 7 days' })]) {
    assert.equal(quotaDisplay(short(), [weekly], now).remainingPercent, 80);
  }
  assert.equal(quotaDisplay(short(), [week({ id: 'seven_day', name: '7 天' })], now).remainingPercent, 0);
});

test('an active weekly block remains known when the short-window reset already passed', () => {
  assert.equal(quotaDisplay({ ...short(), resetsAt: now - 1 }, [week()], now).remainingPercent, 0);
  assert.equal(quotaDisplay({ ...short(), resetsAt: now - 1 }, [week()], now).expired, false);
  assert.equal(quotaDisplay({ ...short(), resetsAt: now - 1 }, [week({ resetsAt: now - 1 })], now).expired, true);
});

test('availability waits for both exhausted windows and never invents missing reset times', () => {
  const emptyShort = { ...short(), remainingPercent: 0, resetsAt: now + 2 * 86400000 };
  assert.equal(quotaDisplay(emptyShort, [week()], now).resetsAt, emptyShort.resetsAt);
  assert.equal(quotaDisplay(emptyShort, [week({ resetsAt: null })], now).resetsAt, null);
  assert.equal(quotaDisplay({ ...emptyShort, resetsAt: null }, [week()], now).resetsAt, null);
});

test('monthly fallback quotas and unknown short balances remain distinct from zero', () => {
  const monthly = { windowMinutes: 43200, remainingPercent: 100 };
  assert.equal(quotaDisplay(monthly, [monthly, week()], now).remainingPercent, 100);
  assert.equal(quotaDisplay({ ...short(), remainingPercent: null }, [], now).remainingPercent, null);
});
