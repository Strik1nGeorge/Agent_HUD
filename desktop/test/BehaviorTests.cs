using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AgentHud.Tests
{
    public static class BehaviorTests
    {
        [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public NativeRect Bounds, Work; public int Flags; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        static bool InsideWorkArea(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            NativeRect rect;
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            return GetWindowRect(handle, out rect) && GetMonitorInfo(MonitorFromWindow(handle, 2), ref info) &&
                rect.Left >= info.Work.Left && rect.Top >= info.Work.Top && rect.Right <= info.Work.Right && rect.Bottom <= info.Work.Bottom;
        }
        sealed class CountingRing : QuotaRing
        {
            public int Renders;
            protected override void OnRender(DrawingContext dc) { Renders++; base.OnRender(dc); }
        }
        static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
            Console.WriteLine("PASS " + message);
        }
        static void Pump()
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        public static void Run()
        {
            // The Chinese assertions below must not depend on the machine's display language.
            Lang.Apply("zh");
            var now = RingValue.Now();
            Check(RingValue.UpdatedText(now - 4 * 60000, now).Contains("4 分钟前"), "quota age comes from provider collection time");
            Check(RingValue.UpdatedText(null, now).Contains("未知"), "missing timestamp is not reported as fresh");
            Check(RingValue.UpdatedText(now + 120000, now).Contains("异常"), "future timestamps are not reported as fresh");
            var provider = ProviderAt(now);
            var live = RingValue.From(provider, 300, now, true);
            Check(live.Detail.Contains("4 分钟前") && live.Detail.Contains("5 分钟查询") && live.Detail.Contains("后重置"), "update age, polling cadence and reset countdown remain distinct");
            Check(RingValue.From(provider, 300, now, false).Detail.Contains("历史快照"), "offline quotas retain their real age and snapshot label");
            Check(live.ResetsAt == provider.limits.windows[0].resetsAt && live.Detail.Contains("（"), "tooltip pairs the reset countdown with its date and time");
            Func<DateTime, double> stamp = local => (local.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            var monday = stamp(new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Local));
            Check(RingValue.RingBadge(stamp(new DateTime(2026, 10, 19, 14, 30, 0, DateTimeKind.Local)), monday) == "14:30", "reset within a day shows its clock time");
            Check(RingValue.RingBadge(stamp(new DateTime(2026, 10, 20, 3, 5, 0, DateTimeKind.Local)), monday) == "03:05", "overnight reset under 24 hours still shows clock time");
            Check(RingValue.RingBadge(stamp(new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Local)), monday) == "周四", "reset days away shows the weekday");
            Check(RingValue.RingBadge(stamp(new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Local)), monday) == "10/26", "reset on the same weekday next week shows its date");
            Check(RingValue.RingBadge(monday - 1, monday) == null, "a passed reset time has no badge");
            var ring = new QuotaRing { Platform = "Codex" };
            ring.Update(live, false);
            Check(ring.Primary == "80%" && ring.Secondary != null && ring.Secondary.Contains(":"), "the ring shows the percentage with the reset time under it");
            ring.Update(RingValue.From(provider, 300, now, false), true);
            Check(ring.Primary == "80%" && ring.Secondary == "离线", "offline keeps the number and says offline instead of a reset time");
            provider.limits.windows[0].resetsAt = null;
            ring.Update(RingValue.From(provider, 300, now, true), false);
            Check(ring.Secondary == "剩余", "without a reset time the ring says what the number is");
            provider.limits.windows[0].resetsAt = now - 1;
            Check(!RingValue.From(provider, 300, now, true).Percent.HasValue, "expired quota is not shown as a live percentage");
            ring.Update(RingValue.From(provider, 300, now, true), false);
            Check(ring.Primary == "待更新" && ring.Secondary == null, "an expired quota shows a status word in the ring");
            var free = new Provider { id = "codex", limits = new Limits { collectedAt = now - 60000, staleAfterMs = 360000, source = "Codex 官方额度接口", windows = new List<QuotaWindow> {
                new QuotaWindow { windowMinutes = 43200, remainingPercent = 100, resetsAt = now + 30 * 86400000.0 } } } };
            var freeRing = RingValue.From(free, 300, now, true);
            Check(freeRing.Percent == 100 && RingValue.From(free, 10080, now, true).Percent == 100 && freeRing.Detail.Contains("此账户没有 5 小时额度，这里显示的是 30 天额度"), "an account with only a 30-day window shows it under 5h and 7d and says so");
            Check(RingValue.RingBadge(stamp(new DateTime(2026, 11, 24, 14, 2, 0, DateTimeKind.Local)), monday) == "11/24", "a reset weeks away shows its date instead of a weekday");
            Check(!RingValue.From(ProviderAt(now), 300, now, true).Detail.Contains("此账户没有"), "an account with the selected window shows no fallback note");

            // Pets: an empty window beats work, work beats a low quota, and offline beats everything.
            var busy = ProviderAt(now); busy.activity = new Activity { working = true, activeSessions = 2, todayTokens = 1234567, todayRequests = 42, model = "gpt-6.1-sol", effort = "xhigh", lastActiveAt = now - 30000 };
            var busyRing = RingValue.From(busy, 300, now, true);
            Check(AgentPet.MoodFor(busy, busyRing, true, now) == PetMood.Working, "a platform with a working session gets a working pet");
            Check(AgentPet.MoodFor(busy, busyRing, false, now) == PetMood.Offline, "an offline service greys the pet");
            busy.limits.windows[1].remainingPercent = 0;
            Check(AgentPet.MoodFor(busy, RingValue.From(busy, 300, now, true), true, now) == PetMood.Exhausted, "any used-up window puts the pet to sleep, even while working");
            busy.limits.windows[1].remainingPercent = 65; busy.limits.windows[0].remainingPercent = 12; busy.activity.working = false;
            Check(AgentPet.MoodFor(busy, RingValue.From(busy, 300, now, true), true, now) == PetMood.Tired, "under 20% left the idle pet is tired");
            busy.limits.windows[0].remainingPercent = 80;
            Check(AgentPet.MoodFor(busy, RingValue.From(busy, 300, now, true), true, now) == PetMood.Idle, "otherwise the pet idles");
            var pet = new AgentPet(false);
            foreach (PetMood mood in Enum.GetValues(typeof(PetMood))) { pet.SetMood(mood); pet.Pause(); pet.Resume(); }
            Check(pet.Mood == PetMood.Exhausted, "every pet mood can be shown, paused and resumed");

            // Hover card: every window with its reset, today's usage, the latest model and the source.
            busy.activity.working = true;
            var card = AgentDetail.Build(busy, true, 300, now, true, PetMood.Working);
            var cardText = String.Join("\n", Texts(card));
            Check(cardText.Contains("Codex") && cardText.Contains("5 小时") && cardText.Contains("7 天") && cardText.Contains("圆环显示") && cardText.Contains("80%") && cardText.Contains("65%"), "the card lists every quota window and marks the one in the ring");
            Check(cardText.Contains("后重置") && cardText.Contains("今日 1.2M tokens · 42 次请求") && cardText.Contains("gpt-6.1-sol · xhigh") && cardText.Contains("工作中 · 2 个会话") && cardText.Contains("Claude 桌面账户额度"), "the card shows resets, today's usage, the latest model, what the agents are doing and the source");
            var emptyCard = String.Join("\n", Texts(AgentDetail.Build(new Provider { id = "claude", quotaMessage = "未找到 Claude 桌面版登录连接，请先打开 Claude 并登录。" }, false, 300, now, true, PetMood.Idle)));
            Check(emptyCard.Contains("未找到 Claude 桌面版登录连接"), "without a quota the card explains why");

            // Docking: a window flush against the left or right edge of its work area is docked there.
            var work = new Rect(0, 0, 2560, 1400);
            Check(WindowPlacement.SideEdge(new Rect(0, 300, 58, 290), work, 2) == "left" && WindowPlacement.SideEdge(new Rect(2502, 300, 58, 290), work, 2) == "right" && WindowPlacement.SideEdge(new Rect(40, 0, 282, 52), work, 2) == null, "only the left and right edges dock");

            Lang.Apply("en");
            try
            {
                var english = ProviderAt(now);
                var enLive = RingValue.From(english, 300, now, true);
                Check(RingValue.UpdatedText(now - 4 * 60000, now).Contains("4 min ago") && enLive.Detail.Contains("Resets in") && enLive.Detail.Contains("Claude desktop account"), "English tooltip translates age, countdown and the service's source name");
                Check(RingValue.RingBadge(stamp(new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Local)), monday) == "Thu", "English reset days away shows the weekday");
                Check(RingValue.From(free, 300, now, true).Detail.Contains("no 5-hour quota; showing its 30-day quota"), "English names the fallback window");
                var enRing = new QuotaRing { Platform = "Codex" };
                enRing.Update(RingValue.From(english, 300, now, false), true);
                Check(enRing.Secondary == "Offline", "English offline word in the ring");
                english.limits.windows[0].resetsAt = null;
                enRing.Update(RingValue.From(english, 300, now, true), false);
                Check(enRing.Secondary == "left", "English fallback says what the number is");
                var setup = new Provider { id = "claude", quotaStatus = "desktop-connection-disabled", quotaMessage = "桌面版账户额度读取通道已准备好，尚未启用连接。" };
                var setupRing = RingValue.From(setup, 300, now, true);
                enRing.Update(setupRing, false);
                Check(enRing.Primary == "Setup" && setupRing.Detail.StartsWith("The Claude desktop account connection"), "English status words come from status codes, not Chinese captions");
                busy.activity.todayRequests = 1;
                var enCard = String.Join("\n", Texts(AgentDetail.Build(busy, true, 300, now, true, PetMood.Working)));
                Check(enCard.Contains("Today 1.2M tokens · 1 request") && enCard.Contains("in the ring") && enCard.Contains("Working · 2 sessions"), "the card speaks English too");
                Check(Lang.Server("5 小时") == "5 hours" && Lang.Server("7 天") == "7 days" && Lang.Server("Opus · 7 天") == "Opus · 7 days" && Lang.Server("30 天") == "30 days" && Lang.Server("90 分钟") == "90 min" && Lang.Server("支出额度") == "Spend limit", "the service's window names are translated");
            }
            finally { Lang.Apply("zh"); }

            var areas = new[] { new Rect(-1920, 0, 1920, 1040), new Rect(0, 0, 2560, 1400), new Rect(3000, 300, 1920, 1040) };
            var gap = new Rect(2700, 100, 260, 48);
            var selected = WindowPlacement.NearestArea(gap, areas);
            Check(selected >= 0 && areas[selected].Contains(WindowPlacement.Fit(gap, areas[selected], 0)), "monitor gap resolves to a real work area");
            var negative = new Rect(-1800, 900, 260, 48);
            Check(WindowPlacement.Fit(negative, areas[0], 0) == negative, "valid negative screen coordinates are preserved");
            Check(areas[1].Contains(WindowPlacement.Fit(new Rect(2400, 1390, 390, 72), areas[1], 0)), "scaled widget avoids the taskbar and right edge");
            Check(WindowPlacement.Fit(new Rect(8, 9, 260, 48), areas[1], 10).Location == new Point(0, 0), "drag release snaps near work-area edges");
            Check(WindowPlacement.Fit(new Rect(80, 90, 260, 48), areas[1], 10).Location == new Point(80, 90), "drag release preserves positions away from edges");
            Check(areas[0].Contains(WindowPlacement.AtOffset(areas[0], new Size(520, 96), 1, 1)), "relative restore respects resized dimensions at 200 percent DPI");
            var unplugged = new Rect(-1600, 700, 260, 48);
            Check(WindowPlacement.NearestArea(unplugged, new[] { areas[1] }) == 0 && areas[1].Contains(WindowPlacement.Fit(unplugged, areas[1], 0)), "disconnected monitor position falls back inside a remaining screen");

            var counted = new CountingRing { ArcBrush = Brushes.Blue, Track = Colors.LightGray, Platform = "Test" };
            counted.Update(new RingValue { Percent = 75, Detail = "first" }, false);
            var host = new Window { Width = 60, Height = 80, Content = counted, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                host.Show(); Pump(); Pump();
                var count = counted.Renders;
                for (var i = 0; i < 20; i++) counted.Update(new RingValue { Percent = 75, Detail = "first" }, false);
                Pump();
                Check(counted.Renders == count, "unchanged quota does not redraw the counted");
                counted.Update(new RingValue { Percent = 75, Detail = "updated age" }, false); Pump();
                Check(counted.Renders == count, "tooltip age changes do not redraw the counted");
                counted.Update(new RingValue { Percent = 75, Detail = "snapshot", Stale = true }, false); Pump();
                Check(counted.Renders > count, "stale state still redraws the counted");
            }
            finally { host.Close(); }
        }
        // The work area of the monitor a window is on, in physical pixels.
        public static Rect WorkAreaOf(Window window)
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            GetMonitorInfo(MonitorFromWindow(new WindowInteropHelper(window).Handle, 2), ref info);
            return new Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        }
        static IEnumerable<string> Texts(object node)
        {
            var block = node as System.Windows.Controls.TextBlock;
            if (block != null) { yield return block.Text; yield break; }
            var element = node as DependencyObject;
            if (element == null) yield break;
            foreach (var child in LogicalTreeHelper.GetChildren(element)) foreach (var text in Texts(child)) yield return text;
        }
        static Provider ProviderAt(double now)
        {
            return new Provider { id = "codex", limits = new Limits { collectedAt = now - 4 * 60000, staleAfterMs = 360000, source = "Claude 桌面账户额度", windows = new List<QuotaWindow> {
                new QuotaWindow { windowMinutes = 300, remainingPercent = 80, resetsAt = now + 7200000 },
                new QuotaWindow { windowMinutes = 10080, remainingPercent = 65, resetsAt = now + 86400000 }
            } } };
        }
        static FieldInfo Field(string name) { return typeof(HudWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance); }
        static void Call(HudWindow hud, string name, params object[] args) { typeof(HudWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hud, args); }
        public static void CheckHiddenRefresh(HudWindow hud)
        {
            var placement = (WindowPlacement)Field("placement").GetValue(hud);
            hud.Left = -100000; hud.Top = -100000;
            placement.EnsureVisible(false); Pump();
            Check(InsideWorkArea(hud), "off-screen native window is moved into a real monitor work area");
            placement.Reset(); Pump(); Pump();
            Check(InsideWorkArea(hud), "reset position lands inside the primary work area");
            var timer = (DispatcherTimer)Field("timer").GetValue(hud);
            timer.Stop();
            var provider = ProviderAt(RingValue.Now());
            Field("snapshot").SetValue(hud, new Snapshot { providers = new List<Provider> { provider }, collectedAt = RingValue.Now() });
            Field("online").SetValue(hud, true);
            Call(hud, "SetPeriod", 300);
            var ring = (QuotaRing)Field("codex").GetValue(hud);
            Check(ring.Percent == 80, "visible widget consumes latest snapshot");
            hud.Hide(); Pump();
            Call(hud, "SetPeriod", 10080);
            Call(hud, "RenderData");
            Check(ring.Percent == 80 && !ring.HasAnimatedProperties, "hidden widget skips rendering and stops animation");
            hud.Show(); Pump();
            Check(ring.Percent == 65, "restored widget immediately renders the latest selected quota");
            timer.Start();
        }
    }
}
