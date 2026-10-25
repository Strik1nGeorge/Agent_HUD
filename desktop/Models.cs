using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AgentHud
{
    public class Snapshot
    {
        public List<Provider> providers { get; set; }
        public double? collectedAt { get; set; }
        public bool loading { get; set; }
    }
    public class Provider
    {
        public string id { get; set; }
        public string name { get; set; }
        public bool connected { get; set; }
        public bool bridgeConnected { get; set; }
        public string quotaStatus { get; set; }
        public string quotaMessage { get; set; }
        public Limits limits { get; set; }
    }
    public class Limits
    {
        public List<QuotaWindow> windows { get; set; }
        public double? collectedAt { get; set; }
        public string source { get; set; }
        public double? staleAfterMs { get; set; }
        public bool stale { get; set; }
    }
    public class QuotaWindow
    {
        public string name { get; set; }
        public double? remainingPercent { get; set; }
        public double? windowMinutes { get; set; }
        public double? resetsAt { get; set; }
    }
    public class WidgetState
    {
        public double? left { get; set; }
        public double? top { get; set; }
        public string monitor { get; set; }
        public double? offsetX { get; set; }
        public double? offsetY { get; set; }
        public bool pinned { get; set; }
        public int period { get; set; }
        // "auto" (follow Windows), "zh" or "en".
        public string language { get; set; }
        public WidgetState() { pinned = true; period = 300; language = "auto"; }
    }
    public static class RingStatus
    {
        // Language-independent states; the label picks its words from these, never from Caption.
        public const string Waiting = "waiting", Setup = "setup", NotLinked = "not-linked", Unavailable = "unavailable", Expired = "expired", Live = "live";
    }
    public class RingValue
    {
        public double? Percent;
        public double? ResetsAt;
        public bool Stale;
        public string Status = RingStatus.Waiting;
        public string Caption;
        public string Detail;
        public static double Now() { return (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds; }
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        static readonly string[] WeekDaysZh = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
        static readonly string[] WeekDaysEn = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

        public static RingValue From(Provider provider, int period, double now, bool online)
        {
            var result = new RingValue { Caption = Lang.T("等待数据", "Waiting for data"), Detail = Lang.T("等待平台提供额度，未将未知值当作零。", "Waiting for the platform to report its quota; an unknown value is never treated as zero.") };
            if (provider == null) return result;
            var limits = provider.limits;
            var win = PickWindow(limits == null ? null : limits.windows, period);
            if (win == null)
            {
                result.Status = provider.quotaStatus == "desktop-connection-disabled" ? RingStatus.Setup : provider.quotaStatus == "desktop-not-connected" ? RingStatus.NotLinked : RingStatus.Unavailable;
                result.Caption = result.Status == RingStatus.Setup ? Lang.T("等待连接", "Connection not enabled") : result.Status == RingStatus.NotLinked ? Lang.T("桌面版未接入", "Desktop app not linked") : Lang.T("额度未提供", "Quota not provided");
                result.Detail = Lang.Server(provider.quotaMessage) ?? Lang.T("这个平台还没有提供所选周期的额度。可打开完整面板查看接入状态。", "This platform hasn't reported a quota for the selected window yet. Open the dashboard to check the connection.");
                result.Detail += "\n" + UpdatedText(limits == null ? null : limits.collectedAt, now);
                result.Detail += provider.id == "claude" ? Lang.T("\n点 ⋯ → Claude 官方用量，查看当前账户额度。", "\n⋯ → Claude usage shows this account's quota.") : Lang.T("\n点 ⋯ → Codex 官方用量，查看当前账户额度。", "\n⋯ → Codex usage shows this account's quota.");
                return result;
            }
            if (win.resetsAt.HasValue && win.resetsAt.Value <= now)
            {
                result.Status = RingStatus.Expired;
                result.Caption = Lang.T("等待额度重置", "Waiting for reset");
                result.Detail = Lang.T("上次快照的重置时间已过，等待新数据；不会自动显示 100%。\n", "The last snapshot's reset time has passed; waiting for new data instead of assuming 100%.\n") + UpdatedText(limits.collectedAt, now);
                return result;
            }
            result.Status = RingStatus.Live;
            result.Percent = Math.Max(0, Math.Min(100, win.remainingPercent.Value));
            result.ResetsAt = win.resetsAt;
            result.Stale = limits.stale || !online || !limits.collectedAt.HasValue || now - limits.collectedAt.Value > (limits.staleAfterMs ?? 120000);
            result.Caption = win.resetsAt.HasValue ? ResetText(win.resetsAt.Value - now) : Lang.T("重置时间未提供", "Reset time not provided");
            var resetDate = win.resetsAt.HasValue ? LocalTime(win.resetsAt.Value) : null;
            var resetStamp = resetDate.HasValue ? Lang.T("（" + resetDate.Value.ToString("MM-dd HH:mm") + "）", " (" + resetDate.Value.ToString("MM-dd HH:mm") + ")") : "";
            result.Detail = (Lang.Server(limits.source) ?? Lang.T("平台额度", "Platform quota")) + "\n" + UpdatedText(limits.collectedAt, now) + "\n" + (result.Stale ? Lang.T("历史快照 · ", "Snapshot · ") : "") + result.Caption + resetStamp;
            if (limits.source == "Codex 官方额度接口") result.Detail += Lang.T("\n正常约每 1 分钟查询，连接失败时可能延迟。", "\nNormally queried about once a minute; may lag when the connection fails.");
            else if (limits.source == "Claude 桌面账户额度") result.Detail += Lang.T("\n正常约每 5 分钟查询，连接失败时可能延迟。", "\nNormally queried about every 5 minutes; may lag when the connection fails.");
            result.Detail += Lang.T("\n同一平台账户内共享，非单个 agent 的独立额度。", "\nShared across the platform account, not a per-agent quota.");
            if (win.windowMinutes != period)
                result.Detail += Lang.T("\n此账户没有" + PeriodName(period, false) + "额度，这里显示的是" + PeriodName(win.windowMinutes ?? 0, false) + "额度。",
                    "\nThis account has no " + PeriodName(period, true) + " quota; showing its " + PeriodName(win.windowMinutes ?? 0, true) + " quota instead.");
            if (!String.IsNullOrEmpty(provider.quotaMessage)) result.Detail += "\n" + Lang.Server(provider.quotaMessage);
            if (result.Stale) result.Caption = Lang.T("快照 · ", "Snapshot · ") + result.Caption;
            return result;
        }
        // Plans differ: the free plan reports one 30-day window instead of 5 h / 7 d. The selected window wins;
        // without it the ring shows the account's nearest window rather than nothing, and the tooltip says so.
        static QuotaWindow PickWindow(List<QuotaWindow> windows, int period)
        {
            if (windows == null) return null;
            var usable = windows.Where(w => w != null && w.remainingPercent.HasValue && !Double.IsNaN(w.remainingPercent.Value) && !Double.IsInfinity(w.remainingPercent.Value)).ToList();
            return usable.FirstOrDefault(w => w.windowMinutes == period)
                ?? usable.Where(w => w.windowMinutes > 0).OrderBy(w => Math.Abs(Math.Log(w.windowMinutes.Value / period))).FirstOrDefault();
        }
        static string PeriodName(double minutes, bool english)
        {
            if (minutes > 0 && minutes % 1440 == 0) return english ? (minutes / 1440) + "-day" : " " + (minutes / 1440) + " 天";
            if (minutes > 0 && minutes % 60 == 0) return english ? (minutes / 60) + "-hour" : " " + (minutes / 60) + " 小时";
            return english ? minutes + "-minute" : " " + minutes + " 分钟";
        }
        public static string UpdatedText(double? collectedAt, double now)
        {
            if (!collectedAt.HasValue) return Lang.T("额度更新时间未知", "Quota update time unknown");
            var stamp = collectedAt.Value;
            if (Double.IsNaN(stamp) || Double.IsInfinity(stamp) || stamp > now + 60000) return Lang.T("额度更新时间异常，请检查系统时间", "Quota update time looks wrong; check the system clock");
            try
            {
                var date = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(stamp).ToLocalTime();
                var minutes = Math.Floor(Math.Max(0, now - stamp) / 60000);
                var age = minutes < 1 ? Lang.T("刚刚", "just now")
                    : minutes < 60 ? Lang.T(minutes + " 分钟前", minutes + " min ago")
                    : minutes < 1440 ? Lang.T(Math.Floor(minutes / 60) + " 小时前", Math.Floor(minutes / 60) + " h ago")
                    : Lang.T(Math.Floor(minutes / 1440) + " 天前", Math.Floor(minutes / 1440) + " d ago");
                return Lang.T("额度更新：" + age + "（" + date.ToString("MM-dd HH:mm") + "）", "Quota updated " + age + " (" + date.ToString("MM-dd HH:mm") + ")");
            }
            catch (ArgumentOutOfRangeException) { return Lang.T("额度更新时间未知", "Quota update time unknown"); }
        }
        static DateTime? LocalTime(double stamp)
        {
            if (Double.IsNaN(stamp) || Double.IsInfinity(stamp)) return null;
            try { return Epoch.AddMilliseconds(stamp).ToLocalTime(); }
            catch (ArgumentOutOfRangeException) { return null; }
        }
        // Short form for the strip (≤ 52 DIP): the clock time within a day, the weekday beyond it,
        // like Claude's own usage page. The exact countdown and date stay in the tooltip.
        public static string ResetBadge(double resetsAt, double now)
        {
            var reset = LocalTime(resetsAt);
            if (!reset.HasValue || resetsAt <= now) return null;
            var clock = reset.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (resetsAt - now < 86400000) return Lang.T(clock + " 重置", "Resets " + clock);
            var daysAhead = (reset.Value.Date - LocalTime(now).Value.Date).TotalDays;
            // Longer windows (the free plan's 30 days) reset weeks out, where a weekday would mislead: show the date.
            if (daysAhead >= 13) { var date = reset.Value.ToString("M/d", CultureInfo.InvariantCulture); return Lang.T(date + " 重置", "Resets " + date); }
            // A 7-day window can land on today's weekday next week. ("Resets next Thu" won't fit; "Next Thu" does.)
            var nextWeek = daysAhead >= 7;
            var day = (int)reset.Value.DayOfWeek;
            return nextWeek ? Lang.T("下" + WeekDaysZh[day] + "重置", "Next " + WeekDaysEn[day]) : Lang.T(WeekDaysZh[day] + "重置", "Resets " + WeekDaysEn[day]);
        }
        public static string ResetText(double milliseconds)
        {
            var minutes = (long)Math.Ceiling(Math.Max(0, milliseconds) / 60000);
            if (minutes >= 1440) return Lang.T(String.Format("{0}天 {1}小时后重置", minutes / 1440, (minutes % 1440) / 60), String.Format("Resets in {0}d {1}h", minutes / 1440, (minutes % 1440) / 60));
            if (minutes >= 60) return Lang.T(String.Format("{0}小时 {1}分后重置", minutes / 60, minutes % 60), String.Format("Resets in {0}h {1}m", minutes / 60, minutes % 60));
            return Lang.T(String.Format("{0}分钟后重置", minutes), String.Format("Resets in {0} min", minutes));
        }
    }
}
