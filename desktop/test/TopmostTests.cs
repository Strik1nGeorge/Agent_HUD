using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AgentHud.Tests
{
    public static class TopmostTests
    {
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        static bool NativePinned(Window window) { return (GetWindowLong(new WindowInteropHelper(window).Handle, -20) & 8) != 0; }
        static void Pump()
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            Console.WriteLine("PASS " + message);
        }
        static ContextMenu Menu(HudWindow hud) { return (ContextMenu)typeof(HudWindow).GetField("menu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(hud); }
        static void Toggle(HudWindow hud, bool pinned)
        {
            var item = (MenuItem)Menu(hud).Items[4];
            item.IsChecked = pinned;
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump();
        }
        static Button FindButton(DependencyObject node, string automationId)
        {
            var button = node as Button;
            if (button != null && AutomationProperties.GetAutomationId(button) == automationId) return button;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var found = FindButton(VisualTreeHelper.GetChild(node, i), automationId);
                if (found != null) return found;
            }
            return null;
        }
        static void Click(HudWindow hud, string automationId)
        {
            var button = FindButton(hud, automationId);
            if (button == null) throw new Exception(automationId + " not found");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
        }
        static string Header(HudWindow hud, int index) { return (string)((MenuItem)Menu(hud).Items[index]).Header; }
        [STAThread]
        public static int Main(string[] args)
        {
            var baseline = args.Length > 0 && args[0] == "--baseline";
            var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "state-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            HudWindow hud = null;
            Window other = null;
            using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset))
            using (var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset))
            {
                try
                {
                    BehaviorTests.Run();
                    hud = new HudWindow(directory, new Uri("http://127.0.0.1:1"), signal, false, exitSignal);
                    hud.Left = 100; hud.Top = 100;
                    hud.Show(); Pump();
                    var handle = new WindowInteropHelper(hud).Handle;
                    var owner = GetWindow(handle, 4);
                    Console.WriteLine("Initial: managed=" + hud.Topmost + ", native=" + NativePinned(hud) + ", hiddenOwnerTopmost=" + (owner != IntPtr.Zero && (GetWindowLong(owner, -20) & 8) != 0));
                    Check(NativePinned(hud), "pinned at startup");
                    BehaviorTests.CheckHiddenRefresh(hud);
                    hud.Hide(); signal.Set(); Pump(); Pump();
                    Check(hud.IsVisible, "show signal restores hidden widget without polling");
                    other = new Window { Title = "Agent HUD test window", Width = 280, Height = 70, Left = 100, Top = 100, ShowActivated = true };
                    other.Show(); other.Activate(); Pump();
                    Check(NativePinned(hud), "stays pinned when an ordinary window activates");
                    Toggle(hud, false);
                    Check(!NativePinned(hud) && !hud.Topmost, "unpin clears managed and native state");
                    hud.Hide(); Pump(); hud.Show(); Pump();
                    Check(!NativePinned(hud), "unpin survives hide/show");
                    Toggle(hud, true);
                    Check(NativePinned(hud) && hud.Topmost, "pin restores managed and native state");
                    Click(hud, "PinButton");
                    Check(!NativePinned(hud) && !hud.Topmost, "pin button click unpins");
                    Click(hud, "PinButton");
                    Check(NativePinned(hud) && hud.Topmost, "second pin button click pins again");
                    Check(((FrameworkElement)hud.Content).ContextMenu == null, "right-click on the strip no longer opens a menu");
                    Click(hud, "MoreButton");
                    var menu = Menu(hud);
                    var strip = (FrameworkElement)hud.Content;
                    var stripCorner = strip.PointToScreen(new Point(strip.ActualWidth, strip.ActualHeight));
                    // The template's 14 DIP shadow margin surrounds the visible menu.
                    var menuCorner = menu.PointToScreen(new Point(menu.ActualWidth - 14, 14));
                    Console.WriteLine("Strip bottom-right " + stripCorner + ", menu top-right " + menuCorner);
                    Check(menu.IsOpen && Math.Abs(menuCorner.X - stripCorner.X) <= 3 && menuCorner.Y > stripCorner.Y && menuCorner.Y - stripCorner.Y < 20, "the more button opens the menu right-aligned just below the strip");
                    menu.IsOpen = false; Pump();
                    Check(NativePinned(hud), "opening and closing the menu preserves pinning");
                    // Start from Chinese whatever the machine's display language, so the toggle direction is known.
                    typeof(HudWindow).GetMethod("SetLanguage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(hud, new object[] { "zh" }); Pump();
                    Click(hud, "LanguageButton");
                    Check(Header(hud, 4) == "Always on top" && AutomationProperties.GetName(FindButton(hud, "LanguageButton")) == "切换到中文" && AutomationProperties.GetName(FindButton(hud, "MoreButton")) == "More options" && AutomationProperties.GetName(FindButton(hud, "PinButton")).StartsWith("Pinned"), "the language button switches the menu and button labels to English live");
                    Check(NativePinned(hud), "language switch keeps the window pinned");
                    Click(hud, "LanguageButton");
                    Check(Header(hud, 4) == "始终置顶" && AutomationProperties.GetName(FindButton(hud, "LanguageButton")) == "Switch to English", "a second click switches back to Chinese");
                    // Docking: flush against the left edge of the screen the strip becomes a column; moved off, a strip again.
                    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    var place = (WindowPlacement)typeof(HudWindow).GetField("placement", flags).GetValue(hud);
                    var checkDock = typeof(HudWindow).GetMethod("CheckDock", flags);
                    var work = BehaviorTests.WorkAreaOf(hud);
                    Rect before; place.TryGetRect(out before);
                    SetWindowPos(handle, IntPtr.Zero, (int)work.Left, (int)before.Top, 0, 0, 0x0215); Pump();
                    checkDock.Invoke(hud, null); Pump(); Pump(); DockAnimationTests.Wait(place);
                    Rect docked; place.TryGetRect(out docked);
                    Console.WriteLine("Docked rect " + docked + " in work area " + work + "; size " + hud.Width + "x" + hud.Height + ", edge " + place.SideEdge());
                    // Sizes snap to whole physical pixels, so compare within a pixel.
                    Check(Math.Abs(hud.Width - 58) < 1 && Math.Abs(hud.Height - 290) < 1 && place.SideEdge() == "left" && docked.Left == work.Left && docked.Top >= work.Top && docked.Bottom <= work.Bottom, "against the left edge the strip becomes a column, flush with that edge");
                    Click(hud, "MoreButton");
                    var sideMenu = Menu(hud);
                    var menuLeft = sideMenu.PointToScreen(new Point(14, 14)).X;
                    Check(sideMenu.IsOpen && menuLeft > docked.Right && menuLeft - docked.Right < 20, "in the column the menu opens beside it, toward the screen");
                    sideMenu.IsOpen = false; Pump();
                    SetWindowPos(handle, IntPtr.Zero, (int)work.Left + 300, (int)docked.Top, 0, 0, 0x0215); Pump();
                    checkDock.Invoke(hud, null); Pump(); Pump(); DockAnimationTests.Wait(place);
                    Check(Math.Abs(hud.Width - 282) < 1 && Math.Abs(hud.Height - 52) < 1 && place.SideEdge() == null, "moved off the edge, it turns back into a strip");
                    // A drag's native settled notification can run before the deferred reanchor.
                    // Rechecking in that interval must not undo a right-edge layout change.
                    place.TryGetRect(out before);
                    SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(work.Right - before.Width), (int)before.Top, 0, 0, 0x0215); Pump();
                    SendMessage(handle, 0x0232, IntPtr.Zero, IntPtr.Zero); // WM_EXITSIZEMOVE queues the native settled check.
                    checkDock.Invoke(hud, null);
                    checkDock.Invoke(hud, null);
                    Pump(); Pump(); DockAnimationTests.Wait(place);
                    place.TryGetRect(out docked);
                    Console.WriteLine("Right docked rect " + docked + "; size " + hud.Width + "x" + hud.Height + ", edge " + place.SideEdge());
                    Check(Math.Abs(hud.Width - 58) < 1 && Math.Abs(hud.Height - 290) < 1 && place.SideEdge() == "right" && Math.Abs(docked.Right - work.Right) <= 1 && work.Contains(docked), "repeated settled checks keep the column docked to the right edge");
                    Click(hud, "MoreButton");
                    sideMenu = Menu(hud);
                    var menuRight = sideMenu.PointToScreen(new Point(sideMenu.ActualWidth - 14, 14)).X;
                    Check(sideMenu.IsOpen && menuRight < docked.Left && docked.Left - menuRight < 20, "the right-edge column opens its menu inward to the left");
                    sideMenu.IsOpen = false; Pump();
                    var rightTip = (ToolTip)typeof(HudWindow).GetField("codexTip", flags).GetValue(hud);
                    Check(rightTip.Placement == System.Windows.Controls.Primitives.PlacementMode.Left, "right-edge hover cards open toward the screen");
                    SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(work.Right - docked.Width - 60), (int)docked.Top, 0, 0, 0x0215); Pump();
                    SendMessage(handle, 0x0232, IntPtr.Zero, IntPtr.Zero);
                    checkDock.Invoke(hud, null); checkDock.Invoke(hud, null); Pump(); Pump(); DockAnimationTests.Wait(place);
                    place.TryGetRect(out before);
                    Console.WriteLine("Undocked right rect " + before + "; size " + hud.Width + "x" + hud.Height + ", edge " + place.SideEdge());
                    Check(Math.Abs(hud.Width - 282) < 1 && place.SideEdge() == null && Math.Abs(before.Right - (work.Right - 60)) <= 1 && work.Contains(before), "undocking from the right restores a strip without jumping across the screen");
                    SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(work.Right - before.Width), (int)Math.Round(work.Bottom - before.Height), 0, 0, 0x0215); Pump();
                    SendMessage(handle, 0x0232, IntPtr.Zero, IntPtr.Zero);
                    checkDock.Invoke(hud, null); checkDock.Invoke(hud, null); Pump(); Pump(); DockAnimationTests.Wait(place);
                    place.TryGetRect(out docked);
                    Check(place.SideEdge() == "right" && Math.Abs(hud.Width - 58) < 1 && work.Contains(docked), "docking near the bottom-right corner keeps the column above the taskbar");
                    DockAnimationTests.Run(hud);
                    var foreground = GetForegroundWindow();
                    // Reproduce a native z-order change without changing WPF's cached Topmost value.
                    Check(SetWindowPos(handle, new IntPtr(-2), 0, 0, 0, 0, 0x213), "native demotion succeeded");
                    hud.Topmost = true;
                    Pump();
                    Console.WriteLine("After native demotion: managed=" + hud.Topmost + ", native=" + NativePinned(hud));
                    if (!baseline) Check(NativePinned(hud), "repairs native demotion while preference remains pinned");
                    Check(GetForegroundWindow() == foreground, "repair does not steal foreground focus");
                    hud.Hide(); Pump(); hud.Show(); Pump();
                    if (!baseline) Check(NativePinned(hud), "pinned hide/show restores native state");
                    SetWindowPos(handle, new IntPtr(-2), 0, 0, 0, 0, 0x213);
                    // Toggle before pumping the dispatcher: a previously queued repair must use the new preference.
                    Toggle(hud, false);
                    Pump();
                    Check(!NativePinned(hud), "queued recovery cannot override an explicit unpin");
                    hud.Close(); hud = null;
                    Pump();
                    hud = new HudWindow(directory, new Uri("http://127.0.0.1:1"), signal, false, exitSignal);
                    hud.Show(); Pump();
                    Check(!NativePinned(hud), "saved unpinned preference survives restart");
                    var restoredPlace = (WindowPlacement)typeof(HudWindow).GetField("placement", flags).GetValue(hud);
                    Check(Math.Abs(hud.Width - 58) < 1 && restoredPlace.SideEdge() == "right", "the right-edge column and its position survive restart");
                    Toggle(hud, true);
                    hud.Close(); hud = null; Pump();
                    hud = new HudWindow(directory, new Uri("http://127.0.0.1:1"), signal, false, exitSignal);
                    hud.Show(); Pump();
                    Check(NativePinned(hud), "saved pinned preference survives restart");
                    var closed = false;
                    hud.Closed += delegate { closed = true; };
                    exitSignal.Set(); Pump(); Pump();
                    Check(closed, "update signal closes widget gracefully and saves state");
                    hud = null;
                    Console.WriteLine("Desktop topmost checks complete.");
                    return 0;
                }
                catch (Exception e) { Console.Error.WriteLine(e); return 1; }
                finally
                {
                    if (hud != null) hud.Close();
                    if (other != null) other.Close();
                    app.Shutdown();
                }
            }
        }
    }
}
