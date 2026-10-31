// Derive availability for display; leave the platform's quota records untouched.
export function quotaDisplay(window, windows, now = Date.now()) {
  const weekly = window.windowMinutes === 300 && windows.find(w => w?.windowMinutes === 10080
    && (!w.id || w.id === 'seven_day') && (!w.name || !w.name.includes('·'))
    && Number.isFinite(w.remainingPercent) && w.remainingPercent <= 0
    && (w.resetsAt == null || w.resetsAt > now));
  let resetsAt = weekly ? weekly.resetsAt : window.resetsAt;
  if (weekly && window.remainingPercent != null && window.remainingPercent <= 0 && (window.resetsAt == null || window.resetsAt > now)) {
    resetsAt = window.resetsAt != null && weekly.resetsAt != null ? Math.max(window.resetsAt, weekly.resetsAt) : null;
  }
  return {
    weeklyBlocked: Boolean(weekly),
    remainingPercent: weekly ? 0 : Number.isFinite(window.remainingPercent) ? Math.max(0, Math.min(100, window.remainingPercent)) : null,
    resetsAt,
    expired: !weekly && window.resetsAt != null && window.resetsAt <= now,
  };
}
