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
            Check(RingValue.ResetBadge(stamp(new DateTime(2026, 10, 19, 14, 30, 0, DateTimeKind.Local)), monday) == "14:30 重置", "reset within a day shows its clock time");
            Check(RingValue.ResetBadge(stamp(new DateTime(2026, 10, 20, 3, 5, 0, DateTimeKind.Local)), monday) == "03:05 重置", "overnight reset under 24 hours still shows clock time");
            Check(RingValue.ResetBadge(stamp(new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Local)), monday) == "周四重置", "reset days away shows the weekday");
            Check(RingValue.ResetBadge(stamp(new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Local)), monday) == "下周一重置", "reset on the same weekday next week says next week");
            Check(RingValue.ResetBadge(monday - 1, monday) == null, "a passed reset time has no badge");
            var label = new QuotaLabel("Codex");
            var line = (System.Windows.Controls.TextBlock)label.Children[1];
            label.Show(live, false);
            Check(line.Text.EndsWith(" 重置") && line.Text.Contains(":"), "label shows the reset time under the percentage");
            label.Show(RingValue.From(provider, 300, now, false), true);
            Check(line.Text == "Codex 离线", "offline still says offline instead of a reset time");
            provider.limits.windows[0].resetsAt = null;
            label.Show(RingValue.From(provider, 300, now, true), false);
            Check(line.Text == "Codex 剩余", "missing reset time falls back to the platform name");
            provider.limits.windows[0].resetsAt = now - 1;
            Check(!RingValue.From(provider, 300, now, true).Percent.HasValue, "expired quota is not shown as a live percentage");
            var free = new Provider { id = "codex", limits = new Limits { collectedAt = now - 60000, staleAfterMs = 360000, source = "Codex 官方额度接口", windows = new List<QuotaWindow> {
                new QuotaWindow { windowMinutes = 43200, remainingPercent = 100, resetsAt = now + 30 * 86400000.0 } } } };
            var freeRing = RingValue.From(free, 300, now, true);
            Check(freeRing.Percent == 100 && RingValue.From(free, 10080, now, true).Percent == 100 && freeRing.Detail.Contains("此账户没有 5 小时额度，这里显示的是 30 天额度"), "an account with only a 30-day window shows it under 5h and 7d and says so");
            Check(RingValue.ResetBadge(stamp(new DateTime(2026, 11, 24, 14, 2, 0, DateTimeKind.Local)), monday) == "11/24 重置", "a reset weeks away shows its date instead of a weekday");
            Check(!RingValue.From(ProviderAt(now), 300, now, true).Detail.Contains("此账户没有"), "an account with the selected window shows no fallback note");

            Lang.Apply("en");
            try
            {
                var english = ProviderAt(now);
                var enLive = RingValue.From(english, 300, now, true);
                Check(RingValue.UpdatedText(now - 4 * 60000, now).Contains("4 min ago") && enLive.Detail.Contains("Resets in") && enLive.Detail.Contains("Claude desktop account"), "English tooltip translates age, countdown and the service's source name");
                Check(RingValue.ResetBadge(stamp(new DateTime(2026, 10, 19, 14, 30, 0, DateTimeKind.Local)), monday) == "Resets 14:30", "English reset within a day shows its clock time");
                Check(RingValue.ResetBadge(stamp(new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Local)), monday) == "Resets Thu", "English reset days away shows the weekday");
                Check(RingValue.ResetBadge(stamp(new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Local)), monday) == "Next Mon", "English next-week reset fits the strip");
                Check(RingValue.ResetBadge(stamp(new DateTime(2026, 11, 24, 14, 2, 0, DateTimeKind.Local)), monday) == "Resets 11/24" && RingValue.From(free, 300, now, true).Detail.Contains("no 5-hour quota; showing its 30-day quota"), "English names the fallback window and dates a distant reset");
                var enLabel = new QuotaLabel("Codex");
                var enValue = (System.Windows.Controls.TextBlock)enLabel.Children[0];
                var enLine = (System.Windows.Controls.TextBlock)enLabel.Children[1];
                enLabel.Show(RingValue.From(english, 300, now, false), true);
                Check(enLine.Text == "Offline", "English offline label");
                english.limits.windows[0].resetsAt = null;
                enLabel.Show(RingValue.From(english, 300, now, true), false);
                Check(enLine.Text == "Codex left", "English fallback names the platform");
                var setup = new Provider { id = "claude", quotaStatus = "desktop-connection-disabled", quotaMessage = "桌面版账户额度读取通道已准备好，尚未启用连接。" };
                var setupRing = RingValue.From(setup, 300, now, true);
                enLabel.Show(setupRing, false);
                Check(enValue.Text == "Setup" && setupRing.Detail.StartsWith("The Claude desktop account connection"), "English status words come from status codes, not Chinese captions");
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

            var ring = new CountingRing { ArcBrush = Brushes.Blue, Track = Colors.LightGray, Platform = "Test" };
            ring.Update(new RingValue { Percent = 75, Detail = "first" });
            var host = new Window { Width = 60, Height = 80, Content = ring, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                host.Show(); Pump(); Pump();
                var count = ring.Renders;
                for (var i = 0; i < 20; i++) ring.Update(new RingValue { Percent = 75, Detail = "first" });
                Pump();
                Check(ring.Renders == count, "unchanged quota does not redraw the ring");
                ring.Update(new RingValue { Percent = 75, Detail = "updated age" }); Pump();
                Check(ring.Renders == count, "tooltip age changes do not redraw the ring");
                ring.Update(new RingValue { Percent = 75, Detail = "snapshot", Stale = true }); Pump();
                Check(ring.Renders > count, "stale state still redraws the ring");
            }
            finally { host.Close(); }
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
