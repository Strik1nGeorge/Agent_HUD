using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AgentHud
{
    // Native screen rectangles are physical pixels. Never compare them with WPF's
    // device-independent Left/Top, especially across monitors with different DPI.
    public sealed class WindowPlacement : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct MonitorInfo
        {
            public int Size;
            public NativeRect Bounds, Work;
            public int Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        }
        sealed class Monitor { public string Name; public Rect Work; public bool Primary; }
        delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

        readonly Window window;
        readonly WidgetState saved;
        readonly Action save;
        HwndSource source;
        bool pending, dragging, disposed;
        // Raised once a position has settled (after load, a drag, or a display change), so the widget can
        // pick its layout for where it ended up.
        public event Action Settled;

        public WindowPlacement(Window window, WidgetState saved, Action save)
        {
            this.window = window; this.saved = saved; this.save = save;
            window.SourceInitialized += OnSource;
            window.Loaded += OnLoaded;
            window.Closed += OnClosed;
            SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        }

        static Rect ToRect(NativeRect value) { return new Rect(value.Left, value.Top, Math.Max(0, value.Right - value.Left), Math.Max(0, value.Bottom - value.Top)); }
        static List<Monitor> Monitors()
        {
            var monitors = new List<Monitor>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr handle, IntPtr dc, IntPtr rect, IntPtr data)
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
                if (GetMonitorInfo(handle, ref info) && info.Work.Right > info.Work.Left && info.Work.Bottom > info.Work.Top)
                    monitors.Add(new Monitor { Name = info.Device, Work = ToRect(info.Work), Primary = (info.Flags & 1) != 0 });
                return true;
            }, IntPtr.Zero);
            return monitors;
        }
        bool TryRect(out Rect rect)
        {
            NativeRect native;
            rect = Rect.Empty;
            if (disposed || source == null || source.IsDisposed || !GetWindowRect(source.Handle, out native)) return false;
            rect = ToRect(native); return true;
        }
        double Scale()
        {
            try { var dpi = GetDpiForWindow(source.Handle); if (dpi > 0) return dpi / 96.0; }
            catch (EntryPointNotFoundException) { }
            return source.CompositionTarget.TransformToDevice.M11;
        }
        public static int NearestArea(Rect window, IList<Rect> areas)
        {
            int selected = -1;
            double bestOverlap = -1, bestDistance = Double.MaxValue;
            for (var i = 0; i < areas.Count; i++)
            {
                var overlap = Rect.Intersect(window, areas[i]);
                var size = overlap.IsEmpty ? 0 : overlap.Width * overlap.Height;
                var cx = window.Left + window.Width / 2; var cy = window.Top + window.Height / 2;
                var dx = Math.Max(areas[i].Left - cx, Math.Max(0, cx - areas[i].Right));
                var dy = Math.Max(areas[i].Top - cy, Math.Max(0, cy - areas[i].Bottom));
                var distance = dx * dx + dy * dy;
                if (size > bestOverlap || (size == bestOverlap && distance < bestDistance)) { selected = i; bestOverlap = size; bestDistance = distance; }
            }
            return selected;
        }
        public static Rect Fit(Rect window, Rect work, double snap)
        {
            var right = Math.Max(work.Left, work.Right - window.Width);
            var bottom = Math.Max(work.Top, work.Bottom - window.Height);
            var x = Math.Max(work.Left, Math.Min(right, window.Left));
            var y = Math.Max(work.Top, Math.Min(bottom, window.Top));
            if (x - work.Left <= snap) x = work.Left; else if (right - x <= snap) x = right;
            if (y - work.Top <= snap) y = work.Top; else if (bottom - y <= snap) y = bottom;
            return new Rect(x, y, window.Width, window.Height);
        }
        // "left" / "right" when the window sits against that side of its work area (within tolerance, in the same units).
        public static string SideEdge(Rect window, Rect work, double tolerance)
        {
            if (Math.Abs(window.Left - work.Left) <= tolerance) return "left";
            if (Math.Abs(window.Right - work.Right) <= tolerance) return "right";
            return null;
        }
        public string SideEdge()
        {
            Rect current;
            if (!TryRect(out current)) return null;
            var monitors = Monitors();
            var index = NearestArea(current, monitors.Select(m => m.Work).ToArray());
            return index < 0 ? null : SideEdge(current, monitors[index].Work, 2 * Scale());
        }
        public bool TryGetRect(out Rect rect) { return TryRect(out rect); }
        // After the window changes shape (strip ⇄ column), put it back where it was: flush against the edge it
        // docked to, or, leaving an edge, on the side of the screen it was dragged to, without touching an edge.
        public void Reanchor(Rect before, string edge)
        {
            window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate
            {
                Rect now;
                if (!TryRect(out now)) return;
                var monitors = Monitors();
                var index = NearestArea(before, monitors.Select(m => m.Work).ToArray());
                if (index < 0) return;
                var work = monitors[index].Work;
                var centerX = before.Left + before.Width / 2;
                var x = edge == "left" ? work.Left : edge == "right" ? work.Right - now.Width
                    : centerX < work.Left + work.Width / 2 ? before.Left : before.Right - now.Width;
                var y = before.Top + before.Height / 2 - now.Height / 2;
                Move(Fit(new Rect(x, y, now.Width, now.Height), work, 0));
                save();
            }));
        }
        static bool Fraction(double? value) { return value.HasValue && !Double.IsNaN(value.Value) && value.Value >= 0 && value.Value <= 1; }
        public static Rect AtOffset(Rect work, Size size, double x, double y)
        {
            return new Rect(work.Left + Math.Max(0, work.Width - size.Width) * x, work.Top + Math.Max(0, work.Height - size.Height) * y, size.Width, size.Height);
        }
        void Move(Rect target)
        {
            Rect current;
            if (!TryRect(out current) || (Math.Abs(current.Left - target.Left) < 1 && Math.Abs(current.Top - target.Top) < 1)) return;
            SetWindowPos(source.Handle, IntPtr.Zero, (int)Math.Round(target.Left), (int)Math.Round(target.Top), 0, 0, 0x0215);
        }
        public void EnsureVisible(bool snap)
        {
            Rect current;
            if (!TryRect(out current) || dragging) return;
            var monitors = Monitors();
            var index = NearestArea(current, monitors.Select(m => m.Work).ToArray());
            if (index < 0) return;
            Move(Fit(current, monitors[index].Work, snap ? 10 * Scale() : 0));
        }
        public void Capture()
        {
            Rect current;
            if (!TryRect(out current)) return;
            var monitors = Monitors();
            var index = NearestArea(current, monitors.Select(m => m.Work).ToArray());
            if (index < 0) return;
            var monitor = monitors[index]; var work = monitor.Work;
            saved.monitor = monitor.Name;
            saved.offsetX = Math.Max(0, Math.Min(1, (current.Left - work.Left) / Math.Max(1, work.Width - current.Width)));
            saved.offsetY = Math.Max(0, Math.Min(1, (current.Top - work.Top) / Math.Max(1, work.Height - current.Height)));
        }
        void PlaceOn(Monitor monitor, double x, double y)
        {
            Rect current;
            if (!TryRect(out current)) return;
            Move(AtOffset(monitor.Work, current.Size, x, y));
            // Let WPF finish WM_DPICHANGED before using the new pixel dimensions.
            window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate
            {
                if (!TryRect(out current)) return;
                Move(AtOffset(monitor.Work, current.Size, x, y));
                EnsureVisible(false); save();
                if (Settled != null) Settled();
            }));
        }
        public void Reset()
        {
            var monitor = Monitors().OrderByDescending(m => m.Primary).FirstOrDefault();
            if (monitor == null) return;
            PlaceOn(monitor, .98, .06);
        }
        void OnLoaded(object sender, RoutedEventArgs e)
        {
            var monitors = Monitors();
            var monitor = monitors.FirstOrDefault(m => m.Name == saved.monitor);
            if (Fraction(saved.offsetX) && Fraction(saved.offsetY))
            {
                if (monitor == null) monitor = monitors.OrderByDescending(m => m.Primary).FirstOrDefault();
                if (monitor != null) { PlaceOn(monitor, saved.offsetX.Value, saved.offsetY.Value); return; }
            }
            EnsureVisible(false); save();
            if (Settled != null) Settled();
        }
        void OnSource(object sender, EventArgs e)
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            if (source != null) source.AddHook(OnMessage);
        }
        IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x0231) dragging = true;
            else if (message == 0x0232) { dragging = false; QueueCheck(true); }
            else if (message == 0x02E0 || message == 0x007E || message == 0x001A) QueueCheck(false);
            return IntPtr.Zero;
        }
        void QueueCheck(bool snap)
        {
            if (disposed || pending) return;
            pending = true;
            window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate
            {
                pending = false;
                if (!disposed && !dragging) { EnsureVisible(snap); save(); if (Settled != null) Settled(); }
            }));
        }
        void OnDisplaysChanged(object sender, EventArgs e)
        {
            if (disposed || window.Dispatcher.HasShutdownStarted) return;
            window.Dispatcher.BeginInvoke(new Action(delegate { QueueCheck(false); }));
        }
        void OnClosed(object sender, EventArgs e) { Dispose(); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
            window.SourceInitialized -= OnSource; window.Loaded -= OnLoaded; window.Closed -= OnClosed;
            if (source != null && !source.IsDisposed) source.RemoveHook(OnMessage);
            source = null;
        }
    }
}
