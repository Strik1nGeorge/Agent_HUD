using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AgentHud.Tests
{
    static class DockAnimationTests
    {
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
            Console.WriteLine("PASS " + message);
        }
        static void Pump()
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        public static void Wait(WindowPlacement place)
        {
            var clock = Stopwatch.StartNew();
            while (place.IsReanchoring && clock.Elapsed.TotalSeconds < 3) Pump();
            if (place.IsReanchoring) throw new Exception("Dock animation did not complete.");
        }
        static void Dock(HudWindow hud)
        {
            typeof(HudWindow).GetMethod("CheckDock", Flags).Invoke(hud, null);
        }
        static void Move(HudWindow hud, double x, double y)
        {
            SetWindowPos(new WindowInteropHelper(hud).Handle, IntPtr.Zero, (int)Math.Round(x), (int)Math.Round(y), 0, 0, 0x0215);
        }
        static void CheckMorph(HudWindow hud, WindowPlacement place, string side, bool column)
        {
            var work = BehaviorTests.WorkAreaOf(hud);
            Rect start; place.TryGetRect(out start);
            Move(hud, side == "left" ? work.Left + (column ? 0 : 80) : work.Right - start.Width - (column ? 0 : 80), work.Top + 300);
            place.TryGetRect(out start);
            var frames = new List<Rect>();
            var inside = true;
            var ring = (QuotaRing)typeof(HudWindow).GetField("codex", Flags).GetValue(hud);
            var diameter = ring.ActualWidth;
            var controls = new[] { "codexPet", "codex", "claudePet", "claude", "langButton", "pinButton", "periodButton", "moreButton" }
                .Select(name => (FrameworkElement)typeof(HudWindow).GetField(name, Flags).GetValue(hud)).ToArray();
            EventHandler sample = delegate
            {
                Rect bounds;
                if (!place.TryGetRect(out bounds)) return;
                frames.Add(bounds);
                var safe = bounds; safe.Inflate(2, 2);
                var frameInside = controls.All(control => safe.Contains(control.PointToScreen(new Point(control.ActualWidth / 2, control.ActualHeight / 2))))
                    && Math.Abs(ring.ActualWidth - diameter) <= 1 && hud.Opacity == 1;
                inside &= frameInside;
            };
            CompositionTarget.Rendering += sample;
            try
            {
                // Same ordering as DragMove returning while WM_EXITSIZEMOVE is already queued.
                SendMessage(new WindowInteropHelper(hud).Handle, 0x0232, IntPtr.Zero, IntPtr.Zero);
                Dock(hud); Dock(hud);
                Wait(place);
            }
            finally { CompositionTarget.Rendering -= sample; }
            Rect end; place.TryGetRect(out end);
            Check((column ? place.SideEdge() == side : place.SideEdge() == null) && work.Contains(end), side + (column ? " docking" : " undocking") + " finishes in the correct work area");
            if (!SystemParameters.ClientAreaAnimation) return;
            Check(frames.Any(rect => rect.Width > Math.Min(start.Width, end.Width) + 1 && rect.Width < Math.Max(start.Width, end.Width) - 1), side + (column ? " docking" : " undocking") + " renders intermediate shapes instead of a single jump");
            Check(frames.Zip(frames.Skip(1), (a, b) => column ? b.Width <= a.Width + 1 : b.Width >= a.Width - 1).All(value => value), side + " morph never jumps backward to an older window size");
            Check(frames.All(rect => Math.Abs((side == "left" ? rect.Left : rect.Right) - (side == "left" ? start.Left : start.Right)) <= 1), side + " morph keeps its anchor fixed on every frame");
            Check(inside, side + " morph keeps all controls visible and the rings at their original size");
        }
        public static void Run(HudWindow hud)
        {
            var place = (WindowPlacement)typeof(HudWindow).GetField("placement", Flags).GetValue(hud);
            CheckMorph(hud, place, "right", false);
            CheckMorph(hud, place, "left", true);
            CheckMorph(hud, place, "left", false);
            CheckMorph(hud, place, "right", true);

            // Hiding or starting another native drag must release the frame subscription.
            var work = BehaviorTests.WorkAreaOf(hud);
            Move(hud, work.Left + 300, work.Top + 300); Dock(hud);
            hud.Hide();
            Check(!place.IsReanchoring, "hiding during a morph completes and releases the animation");
            Move(hud, work.Left, work.Top + 300); Dock(hud);
            Check(!place.IsReanchoring && place.SideEdge() == "left", "without visible animation the layout and native bounds change together");
            hud.Show(); Pump();
            Rect current; place.TryGetRect(out current);
            Move(hud, work.Left + 300, current.Top); Dock(hud); place.Reset(); Pump(); Wait(place);
            Check(!place.IsReanchoring && place.SideEdge() == null, "reset position during a morph overrides its previous destination");
            place.TryGetRect(out current);
            Move(hud, work.Right - current.Width, current.Top); Dock(hud);
            var handle = new WindowInteropHelper(hud).Handle;
            SendMessage(handle, 0x0231, IntPtr.Zero, IntPtr.Zero);
            Check(!place.IsReanchoring && place.SideEdge() == "right", "a new drag during a morph first settles its geometry");
            SendMessage(handle, 0x0232, IntPtr.Zero, IntPtr.Zero); Pump(); Wait(place);
        }
    }
}
