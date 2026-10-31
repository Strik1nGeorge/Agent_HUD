using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace AgentHud
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ".."));
            var url = args.Length > 1 ? args[1] : "http://127.0.0.1:4318";
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != "http" || !uri.IsLoopback) return 2;
            var data = Path.Combine(root, ".data");
            Directory.CreateDirectory(data);
            var hash = BitConverter.ToString(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(root.ToLowerInvariant()))).Replace("-", "").Substring(0, 16);
            bool first;
            using (var mutex = new Mutex(true, "Local\\AgentHUD.Widget." + hash, out first))
            {
                var startup = Path.Combine(data, "widget-startup.log");
                if (!first)
                {
                    try { File.AppendAllText(startup, DateTime.Now.ToString("HH:mm:ss.fff") + " pid " + Process.GetCurrentProcess().Id + ": already running, asking it to show\n"); } catch { }
                    try { using (var signal = EventWaitHandle.OpenExisting("Local\\AgentHUD.Show." + hash)) signal.Set(); } catch { }
                    return 0;
                }
                // The running instance starts a fresh log; a second launch only adds its one line above.
                try { File.WriteAllText(startup, DateTime.Now.ToString("HH:mm:ss.fff") + " pid " + Process.GetCurrentProcess().Id + ": starting\n"); } catch { }
                using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\AgentHUD.Show." + hash))
                using (var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\AgentHUD.Exit." + hash))
                {
                    try
                    {
                        File.AppendAllText(startup, "Creating application\n");
                        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                        app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                        {
                            File.AppendAllText(Path.Combine(data, "widget-error.log"), DateTime.UtcNow.ToString("o") + " " + e.Exception.GetType().Name + ": " + e.Exception.Message + Environment.NewLine);
                        };
                        var window = new HudWindow(data, uri, signal, args.Contains("--preview"), exitSignal);
                        File.AppendAllText(Path.Combine(data, "widget-startup.log"), "Starting dispatcher\n");
                        app.Run(window);
                        return 0;
                    }
                    catch (Exception e)
                    {
                        File.AppendAllText(Path.Combine(data, "widget-error.log"), e.ToString() + Environment.NewLine);
                        return 1;
                    }
                }
            }
        }
    }

    public sealed class HudWindow : Window
    {
        readonly string dataDir;
        readonly Uri server;
        readonly EventWaitHandle showSignal;
        readonly HttpClient http;
        readonly WindowPin pin;
        readonly WindowPlacement placement;
        RegisteredWaitHandle showRegistration, exitRegistration;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
        readonly DispatcherTimer timer = new DispatcherTimer();
        readonly DispatcherTimer saveTimer = new DispatcherTimer();
        readonly QuotaRing codex = new QuotaRing { Platform = "Codex" };
        readonly QuotaRing claude = new QuotaRing { Platform = "Claude Code" };
        readonly AgentPet codexPet = new AgentPet(true);
        readonly AgentPet claudePet = new AgentPet(false);
        // Each agent is a tile, pet + ring, beside each other in the strip and stacked in the column.
        StackPanel codexTile, claudeTile;
        ToolTip codexTip, claudeTip;
        Grid body;
        bool vertical;
        readonly Border tint = new Border();
        readonly Border sheen = new Border { IsHitTestVisible = false };
        readonly Border edge = new Border { BorderThickness = new Thickness(1), IsHitTestVisible = false };
        readonly Border rim = new Border { BorderThickness = new Thickness(1), Margin = new Thickness(1), IsHitTestVisible = false };
        readonly System.Windows.Shapes.Path pinShape = new System.Windows.Shapes.Path { Data = Geometry.Parse("M5,2 H11 L10.3,7.3 L12.5,10 H3.5 L5.7,7.3 Z M8,10 V14.5"), StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        readonly System.Windows.Shapes.Path pinSlash = new System.Windows.Shapes.Path { Data = Geometry.Parse("M2,2 L14,14"), StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        readonly TextBlock langText = new TextBlock { FontSize = 8.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid capsule;
        Palette palette;
        WidgetState saved = new WidgetState();
        Snapshot snapshot;
        Button periodButton, pinButton, langButton, moreButton;
        ContextMenu menu;
        Forms.NotifyIcon tray;
        bool busy, online, exiting, degraded;
        double lastServiceStart;
        int failedFetches;

        public HudWindow(string data, Uri url, EventWaitHandle signal, bool preview, EventWaitHandle exitSignal = null)
        {
            dataDir = data; server = url; showSignal = signal;
            http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(4), MaxResponseContentBufferSize = 1024 * 1024 };
            ReadState();
            Lang.Apply(saved.language);
            Title = "Agent Meter Widget";
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            // Not a layered window: DWM only honours the capsule region for the blur on regular windows.
            // The glass frame spans the whole client area so transparent pixels show the blurred backdrop.
            Background = Brushes.Transparent;
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, GlassFrameThickness = new Thickness(-1), ResizeBorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
            ShowInTaskbar = false; ShowActivated = false;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            Application.Current.Resources.MergedDictionaries.Add(Palette.LoadStyles());
            Content = BuildUi();
            ApplyTheme();
            SetCornerRadius(GlassWindow.SystemCornerRadius);
            SourceInitialized += delegate
            {
                var handle = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(handle).CompositionTarget.BackgroundColor = Colors.Transparent;
                GlassWindow.EnableBlur(handle);
                if (!GlassWindow.RoundCorners(handle)) SetCornerRadius(0);
            };
            pin = new WindowPin(this, saved.pinned, delegate(int error)
            {
                File.AppendAllText(Path.Combine(dataDir, "widget-error.log"), DateTime.UtcNow.ToString("o") + " Window pin: Win32 error " + error + Environment.NewLine);
            });
            placement = new WindowPlacement(this, saved, SaveState);
            placement.Settled += CheckDock;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            File.AppendAllText(Path.Combine(dataDir, "widget-startup.log"), "UI constructed\n");
            SetupTray();
            File.AppendAllText(Path.Combine(dataDir, "widget-startup.log"), "Tray constructed\n");
            SetPeriod(saved.period);
            UpdatePin();
            RestorePosition();
            saveTimer.Interval = TimeSpan.FromMilliseconds(350);
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveState(); };
            LocationChanged += delegate { if (IsLoaded) { saveTimer.Stop(); saveTimer.Start(); } };
            Loaded += async delegate
            {
                File.AppendAllText(Path.Combine(dataDir, "widget-startup.log"), "Window loaded\n");
                if (showRegistration == null) showRegistration = ThreadPool.RegisterWaitForSingleObject(showSignal, delegate { DispatchSignal(ShowWidget); }, null, Timeout.Infinite, false);
                if (exitSignal != null && exitRegistration == null) exitRegistration = ThreadPool.RegisterWaitForSingleObject(exitSignal, delegate { DispatchSignal(Close); }, null, Timeout.Infinite, false);
                await Fetch();
                if (exiting) return;
                timer.Start();
                if (preview) { await Task.Delay(750); if (!exiting) await Dispatcher.InvokeAsync(new Action(ExportPreview), DispatcherPriority.ApplicationIdle); }
            };
            ContentRendered += delegate { File.AppendAllText(Path.Combine(dataDir, "widget-startup.log"), "Window rendered\n"); };
            timer.Interval = TimeSpan.FromSeconds(5);
            timer.Tick += async delegate { await Fetch(); };
            IsVisibleChanged += delegate
            {
                if (IsVisible) { codexPet.Resume(); claudePet.Resume(); RenderData(); }
                else { codex.PauseAnimation(); claude.PauseAnimation(); codexPet.Pause(); claudePet.Pause(); }
            };
            capsule.ToolTipOpening += delegate { RenderData(); };
            Closing += delegate
            {
                exiting = true;
                if (showRegistration != null) showRegistration.Unregister(null);
                if (exitRegistration != null) exitRegistration.Unregister(null);
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                timer.Stop(); saveTimer.Stop(); SaveState(); tray.Visible = false; tray.Dispose(); http.Dispose();
            };
        }

        void DispatchSignal(Action action)
        {
            if (exiting || Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(new Action(delegate { if (!exiting) action(); }));
        }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color || e.Category == UserPreferenceCategory.VisualStyle)
                Dispatcher.BeginInvoke(new Action(ApplyTheme));
        }

        void SetCornerRadius(double radius)
        {
            tint.CornerRadius = sheen.CornerRadius = edge.CornerRadius = new CornerRadius(radius);
            rim.CornerRadius = new CornerRadius(Math.Max(0, radius - 1));
        }

        void ApplyTheme()
        {
            palette = Palette.Read();
            palette.ApplyTo(Application.Current.Resources);
            tint.Background = palette.Tint; sheen.Background = palette.Sheen; edge.BorderBrush = palette.Edge;
            rim.BorderBrush = degraded ? palette.RimOffline : palette.Rim;
            codex.ArcBrush = palette.CodexArc; codex.Track = palette.CodexTrack;
            claude.ArcBrush = palette.ClaudeArc; claude.Track = palette.ClaudeTrack;
            foreach (var ring in new[] { codex, claude }) { ring.TextBrush = new SolidColorBrush(palette.Text); ring.LowBrush = new SolidColorBrush(palette.Low); ring.InvalidateVisual(); }
            if (pinButton != null) UpdatePin();
        }

        // Glass layers: blurred backdrop (window accent) → grey-white tint → hairline edge; the rim turns heat-orange when offline.
        UIElement BuildUi()
        {
            capsule = new Grid { Cursor = Cursors.SizeAll, Background = Brushes.Transparent };
            capsule.Children.Add(tint); capsule.Children.Add(sheen); capsule.Children.Add(edge); capsule.Children.Add(rim);
            body = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            capsule.Children.Add(body);
            codexTile = Tile(codexPet, codex, "CodexTile", out codexTip);
            claudeTile = Tile(claudePet, claude, "ClaudeTile", out claudeTip);

            // Same mark as the dashboard's language button: the language it switches to, in a rounded outline.
            langText.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            var langGlyph = new Border { MinWidth = 17, Height = 13, CornerRadius = new CornerRadius(3.5), BorderThickness = new Thickness(1.1), Padding = new Thickness(2, 0, 2, 0), Child = langText, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            langGlyph.SetResourceReference(Border.BorderBrushProperty, "Text");
            langButton = IconButton(langGlyph, "LanguageButton");
            langButton.Click += delegate { ToggleLanguage(); };

            var pinIcon = new Grid { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            pinIcon.Children.Add(pinShape); pinIcon.Children.Add(pinSlash);
            // One click pins, the next unpins.
            pinButton = IconButton(pinIcon, "PinButton");
            pinButton.Click += delegate { SetPinned(!saved.pinned); };

            periodButton = new Button { Content = "5h", FontSize = 11, FontWeight = FontWeights.SemiBold, RenderTransform = new ScaleTransform() };
            periodButton.SetResourceReference(StyleProperty, "GlassChip");
            AutomationProperties.SetAutomationId(periodButton, "PeriodButton");
            periodButton.Click += delegate { SetPeriod(saved.period == 300 ? 10080 : 300); };
            AddPressFeedback(periodButton, .94);

            // The menu lives behind ⋯ rather than on right-click, so it can be found by looking.
            var dots = new GeometryGroup();
            foreach (var x in new[] { 1.5, 6, 10.5 }) dots.Children.Add(new EllipseGeometry(new Point(x, 1.5), 1.35, 1.35));
            var dotsShape = new System.Windows.Shapes.Path { Data = dots, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            dotsShape.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Text");
            moreButton = IconButton(dotsShape, "MoreButton");
            moreButton.Click += delegate { OpenMenu(); };
            menu = BuildMenu();
            UpdateLanguageButton();
            Arrange(saved.dock == "left" || saved.dock == "right");

            capsule.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                DependencyObject node = e.OriginalSource as DependencyObject;
                while (node != null && node != capsule) { if (node is Button) return; node = VisualTreeHelper.GetParent(node); }
                if (e.ButtonState == MouseButtonState.Pressed) { try { placement.CompleteReanchor(); DragMove(); placement.EnsureVisible(true); SaveState(); CheckDock(); } catch (InvalidOperationException) { } }
            };
            return capsule;
        }

        // Pet and ring for one platform; hovering either shows the platform's card.
        StackPanel Tile(AgentPet pet, QuotaRing ring, string automationId, out ToolTip tip)
        {
            var tile = new StackPanel { Background = Brushes.Transparent };
            tile.Children.Add(pet); tile.Children.Add(ring);
            AutomationProperties.SetAutomationId(tile, automationId);
            tip = new ToolTip { PlacementTarget = tile };
            tile.ToolTip = tip;
            ToolTipService.SetInitialShowDelay(tile, 250);
            ToolTipService.SetBetweenShowDelay(tile, 0);
            ToolTipService.SetShowDuration(tile, 60000);
            tile.ToolTipOpening += delegate { RenderData(); RefreshCard(tile == codexTile, true); };
            return tile;
        }

        // Two layouts from the same parts. The strip (docked nowhere, or to the top or bottom) reads left to right;
        // against the left or right edge of the screen it becomes a column, pets above rings, buttons below.
        static Size LayoutSize(bool column) { return column ? new Size(58, 290) : new Size(282, 52); }
        void Arrange(bool column, bool resizeWindow = true)
        {
            vertical = column;
            body.Children.Clear(); body.RowDefinitions.Clear(); body.ColumnDefinitions.Clear();
            var tiles = new[] { codexTile, claudeTile };
            var buttons = new[] { langButton, pinButton, periodButton, moreButton };
            foreach (var tile in tiles)
            {
                tile.Orientation = column ? Orientation.Vertical : Orientation.Horizontal;
                tile.HorizontalAlignment = HorizontalAlignment.Center; tile.VerticalAlignment = VerticalAlignment.Center;
            }
            foreach (var pet in new[] { codexPet, claudePet })
            {
                pet.Margin = column ? new Thickness(0, 0, 0, 2) : new Thickness(0, 0, 3, 0);
                pet.HorizontalAlignment = HorizontalAlignment.Center; pet.VerticalAlignment = VerticalAlignment.Center;
            }
            foreach (var ring in new[] { codex, claude }) { ring.HorizontalAlignment = HorizontalAlignment.Center; ring.VerticalAlignment = VerticalAlignment.Center; }
            // -1 = the rest, -2 = as large as the tile.
            var track = column ? new[] { 8.0, -2, 10, -2, -1, 28, 28, 34, 24, 6 } : new[] { 8.0, -2, 10, -2, -1, 24, 22, 40, 18, 6 };
            foreach (var size in track)
            {
                var length = size == -2 ? GridLength.Auto : size < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(size);
                if (column) body.RowDefinitions.Add(new RowDefinition { Height = length }); else body.ColumnDefinitions.Add(new ColumnDefinition { Width = length });
            }
            Place(codexTile, 1, column); Place(claudeTile, 3, column);
            for (var i = 0; i < buttons.Length; i++)
            {
                // Each button keeps the whole strip's height (or the column's width) as hit area; the visible chip stays small.
                buttons[i].Width = column ? (buttons[i] == periodButton ? 44 : 36) : track[5 + i];
                buttons[i].Height = column ? track[5 + i] : 50;
                Place(buttons[i], 5 + i, column);
            }
            var layoutSize = LayoutSize(column);
            body.Width = layoutSize.Width; body.Height = layoutSize.Height;
            if (resizeWindow) { Width = layoutSize.Width; Height = layoutSize.Height; }
            PlaceTips();
        }
        void Place(UIElement element, int index, bool column)
        {
            Grid.SetRow(element, column ? index : 0); Grid.SetColumn(element, column ? 0 : index);
            body.Children.Add(element);
        }
        // Cards open beside the column (toward the screen) and under the strip.
        void PlaceTips()
        {
            foreach (var tip in new[] { codexTip, claudeTip })
            {
                tip.Placement = !vertical ? PlacementMode.Bottom : saved.dock == "right" ? PlacementMode.Left : PlacementMode.Right;
                tip.HorizontalOffset = !vertical ? 0 : saved.dock == "right" ? 8 : -8;
                tip.VerticalOffset = !vertical ? -6 : -14;
            }
        }
        // After a move settles: against the left or right edge → column; anywhere else → strip.
        void CheckDock()
        {
            if (placement == null || placement.IsReanchoring || exiting) return;
            var edge = placement.SideEdge();
            var column = edge != null;
            if (column == vertical && edge == saved.dock) return;
            saved.dock = edge;
            if (column != vertical)
            {
                Rect before;
                var known = placement.TryGetRect(out before);
                if (known)
                {
                    menu.IsOpen = false; codexTip.IsOpen = false; claudeTip.IsOpen = false;
                    var motion = new LayoutTransition(capsule, before, codexPet, codex, claudePet, claude, langButton, pinButton, periodButton, moreButton);
                    placement.Reanchor(before, edge, LayoutSize(column), delegate { motion.Prepare(delegate { Arrange(column, false); }); }, motion.Render, motion.Finish);
                    return; // Reanchor saves and settles once the new geometry is in place.
                }
                Arrange(column);
            }
            else PlaceTips();
            SaveState();
        }

        static Button IconButton(UIElement content, string automationId)
        {
            var button = new Button { Content = content, RenderTransform = new ScaleTransform(), RenderTransformOrigin = new Point(.5, .5) };
            button.SetResourceReference(StyleProperty, "IconChip");
            AutomationProperties.SetAutomationId(button, automationId);
            AddPressFeedback(button, .9);
            return button;
        }
        // Feedback on pointer-down, not release.
        static void AddPressFeedback(Button button, double scale)
        {
            button.PreviewMouseLeftButtonDown += delegate { Press(button, scale, 100); };
            button.PreviewMouseLeftButtonUp += delegate { Press(button, 1, 220); };
            button.MouseLeave += delegate { Press(button, 1, 220); };
        }
        // Starts from the current scale, so a quick tap reverses mid-flight; no overshoot.
        static void Press(UIElement element, double to, int milliseconds)
        {
            var scale = element.RenderTransform as ScaleTransform;
            if (scale == null) scale = ((TransformGroup)element.RenderTransform).Children.OfType<ScaleTransform>().First();
            if (!SystemParameters.ClientAreaAnimation) { scale.ScaleX = scale.ScaleY = 1; return; }
            var move = new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, move); scale.BeginAnimation(ScaleTransform.ScaleYProperty, move);
        }
        ContextMenu BuildMenu()
        {
            var menu = new ContextMenu();
            var open = new MenuItem { Header = Lang.T("打开完整面板", "Open dashboard") }; open.Click += delegate { OpenDashboard(); };
            var codexUsage = new MenuItem { Header = Lang.T("Codex 官方用量 ↗", "Codex usage ↗") }; codexUsage.Click += delegate { OpenCodexUsage(); };
            var claudeUsage = new MenuItem { Header = Lang.T("Claude 官方用量 ↗", "Claude usage ↗") }; claudeUsage.Click += delegate { OpenClaudeUsage(); };
            var top = new MenuItem { Header = Lang.T("始终置顶", "Always on top"), IsCheckable = true };
            top.Click += delegate { SetPinned(top.IsChecked); top.IsChecked = saved.pinned; };
            var hide = new MenuItem { Header = Lang.T("收起到托盘", "Hide to tray") }; hide.Click += delegate { Hide(); };
            var reset = new MenuItem { Header = Lang.T("重置小组件位置", "Reset widget position") }; reset.Click += delegate { placement.Reset(); };
            var quit = new MenuItem { Header = Lang.T("退出小组件", "Quit widget") }; quit.Click += delegate { Close(); };
            // Keep "always on top" at index 4: the desktop tests drive it by position.
            menu.Items.Add(open); menu.Items.Add(codexUsage); menu.Items.Add(claudeUsage); menu.Items.Add(new Separator());
            menu.Items.Add(top); menu.Items.Add(hide); menu.Items.Add(reset); menu.Items.Add(new Separator()); menu.Items.Add(quit);
            menu.Opened += delegate { top.IsChecked = saved.pinned; }; return menu;
        }
        // Drops below the strip, right-aligned with it. The menu template keeps a 14 DIP transparent
        // margin for its shadow, so the offsets cancel it; WPF flips the menu above near the screen bottom.
        // In the column it opens beside ⋯, toward the screen, bottoms aligned.
        void OpenMenu()
        {
            menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (!vertical)
            {
                menu.PlacementTarget = capsule; menu.Placement = PlacementMode.Bottom;
                menu.HorizontalOffset = capsule.ActualWidth - menu.DesiredSize.Width + 14;
                menu.VerticalOffset = -14 + 6;
            }
            else
            {
                // Beside the column's edge (not the narrower button), its bottom level with the ⋯ button's.
                var buttonBottom = moreButton.TranslatePoint(new Point(0, moreButton.ActualHeight), capsule).Y;
                menu.PlacementTarget = capsule;
                menu.Placement = saved.dock == "right" ? PlacementMode.Left : PlacementMode.Right;
                menu.HorizontalOffset = saved.dock == "right" ? 14 - 8 : -14 + 8;
                menu.VerticalOffset = buttonBottom - menu.DesiredSize.Height + 14;
            }
            menu.IsOpen = true;
        }
        // Written in the language it switches to, so it can be found by someone who can't read the current one.
        static string LanguageSwitchText() { return Lang.English ? "切换到中文" : "Switch to English"; }
        void UpdateLanguageButton()
        {
            langText.Text = Lang.English ? "中" : "EN";
            langButton.ToolTip = LanguageSwitchText();
            AutomationProperties.SetName(langButton, LanguageSwitchText());
            moreButton.ToolTip = Lang.T("更多选项", "More options");
            AutomationProperties.SetName(moreButton, Lang.T("更多选项", "More options"));
        }
        void ToggleLanguage() { SetLanguage(Lang.English ? "zh" : "en"); }
        // Saved, then every visible string is rebuilt in place — no restart needed.
        void SetLanguage(string language)
        {
            saved.language = language; Lang.Apply(language); SaveState();
            menu.IsOpen = false;
            menu = BuildMenu();
            UpdateLanguageButton();
            var oldTray = tray.ContextMenuStrip;
            tray.Text = Lang.T("Agent Meter · 额度小组件", "Agent Meter · quota widget");
            tray.ContextMenuStrip = BuildTrayMenu();
            if (oldTray != null) oldTray.Dispose();
            UpdatePin();
            codexPet.Relabel(); claudePet.Relabel();
            SetPeriod(saved.period);
            RefreshCard(true, false); RefreshCard(false, false);
        }
        void SetPeriod(int period)
        {
            saved.period = period == 10080 ? 10080 : 300;
            if (periodButton == null) return;
            periodButton.Content = saved.period == 300 ? "5h" : "7d";
            periodButton.ToolTip = saved.period == 300 ? Lang.T("当前 5 小时额度 · 点击查看 7 天", "5-hour quota · click for 7-day") : Lang.T("当前 7 天额度 · 点击查看 5 小时", "7-day quota · click for 5-hour");
            AutomationProperties.SetName(periodButton, Lang.T("点击切换 5 小时 / 7 天额度", "Switch between 5-hour and 7-day quota"));
            RenderData(); if (IsLoaded) SaveState();
        }
        // Shared by the pin button and the menu item, so both always agree.
        // If Windows refuses the z-order change, the previous state is kept.
        void SetPinned(bool value)
        {
            if (pin.SetPinned(value)) saved.pinned = value;
            else pin.SetPinned(saved.pinned);
            UpdatePin(); SaveState();
        }
        void UpdatePin()
        {
            var brush = new SolidColorBrush(saved.pinned ? palette.Accent : palette.TextMuted);
            pinShape.Stroke = brush; pinShape.Fill = saved.pinned ? brush : Brushes.Transparent;
            pinSlash.Stroke = brush; pinSlash.Visibility = saved.pinned ? Visibility.Collapsed : Visibility.Visible;
            var action = saved.pinned ? Lang.T("已置顶 · 点击取消置顶", "Pinned · click to unpin") : Lang.T("未置顶 · 点击置顶", "Not pinned · click to pin");
            pinButton.ToolTip = action;
            AutomationProperties.SetName(pinButton, saved.pinned ? Lang.T("已置顶，点击取消置顶", "Pinned, click to unpin") : Lang.T("未置顶，点击置顶", "Not pinned, click to pin"));
        }
        async Task Fetch()
        {
            if (busy || exiting) return;
            busy = true;
            try
            {
                var raw = await http.GetStringAsync(new Uri(server, "/api/quotas"));
                if (exiting) return;
                var next = json.Deserialize<Snapshot>(raw);
                if (next == null || next.providers == null) throw new InvalidDataException();
                snapshot = next; online = true; failedFetches = 0;
            }
            catch { online = false; if (!exiting && ++failedFetches >= 2) StartService(); }
            finally { busy = false; if (!exiting) RenderData(); }
        }
        // The local service can die with whatever launched it (an agent session, a closed terminal).
        // The widget runs on its own, so it brings the service back through the normal launcher.
        void StartService()
        {
            var now = RingValue.Now();
            if (now - lastServiceStart < 60000) return;
            lastServiceStart = now;
            var root = Path.GetDirectoryName(dataDir);
            var script = Path.Combine(root, "start.ps1");
            if (!File.Exists(script)) return;
            try
            {
                var info = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\" -NoBrowser") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root };
                info.EnvironmentVariables["PORT"] = server.Port.ToString(CultureInfo.InvariantCulture);
                Process.Start(info);
            }
            catch (Exception e) { File.AppendAllText(Path.Combine(dataDir, "widget-error.log"), DateTime.UtcNow.ToString("o") + " Service start: " + e.Message + Environment.NewLine); }
        }
        void RenderData()
        {
            if (!IsVisible || exiting) return;
            var now = RingValue.Now();
            var cp = ProviderFor("codex"); var ap = ProviderFor("claude");
            var c = RingValue.From(cp, saved.period, now, online);
            var a = RingValue.From(ap, saved.period, now, online);
            codex.Update(c, !online); claude.Update(a, !online);
            codexPet.SetMood(AgentPet.MoodFor(cp, c, online, now)); claudePet.SetMood(AgentPet.MoodFor(ap, a, online, now));
            RefreshCard(true, false); RefreshCard(false, false);
            var scanStale = snapshot != null && (!snapshot.collectedAt.HasValue || now - snapshot.collectedAt.Value > 30000);
            // Offline: orange edge *and* the word "离线" under each ring — colour is never the only signal.
            // A slow session scan doesn't touch the quotas shown here, so it only goes in the tooltip.
            var wasDegraded = degraded;
            degraded = !online;
            if (degraded != wasDegraded) rim.BorderBrush = degraded ? palette.RimOffline : palette.Rim;
            var detail = Lang.T("拖动可移动 · 拖到屏幕左右边缘变为竖排 · ⋯ 打开菜单\n", "Drag to move · drop against the left or right edge for a column · ⋯ opens the menu\n")
                + (!online ? Lang.T("本地服务离线，正在自动重启；显示的是最后一次快照", "Local service offline, restarting automatically; showing the last snapshot")
                    : snapshot.loading ? Lang.T("正在读取额度", "Reading quotas")
                    : scanStale ? Lang.T("会话扫描延迟，额度不受影响", "Session scan delayed; quotas are unaffected")
                    : Lang.T("每 5 秒读取本地快照；平台额度按各自频率更新", "Reads the local snapshot every 5 s; each platform's quota updates on its own schedule"))
                + Lang.T("\n悬停宠物或圆环查看该 agent 的详情", "\nHover a pet or ring for that agent's details");
            if (!Equals(capsule.ToolTip, detail)) capsule.ToolTip = detail;
        }
        Provider ProviderFor(string id)
        {
            return snapshot == null || snapshot.providers == null ? null : snapshot.providers.FirstOrDefault(p => p.id == id);
        }
        // Rebuilt when it opens and while it stays open, so it follows the 5-second refresh.
        void RefreshCard(bool forCodex, bool opening)
        {
            var tip = forCodex ? codexTip : claudeTip;
            if (tip == null || (!opening && !tip.IsOpen)) return;
            var pet = forCodex ? codexPet : claudePet;
            tip.Content = AgentDetail.Build(ProviderFor(forCodex ? "codex" : "claude"), forCodex, saved.period, RingValue.Now(), online, pet.Mood);
        }
        void OpenDashboard() { try { Process.Start(new ProcessStartInfo(server.AbsoluteUri) { UseShellExecute = true }); } catch { } }
        void OpenClaudeUsage() { try { Process.Start(new ProcessStartInfo("https://claude.ai/settings/usage") { UseShellExecute = true }); } catch { } }
        void OpenCodexUsage() { try { Process.Start(new ProcessStartInfo("https://chatgpt.com/codex/settings/usage") { UseShellExecute = true }); } catch { } }
        void ShowWidget() { Show(); WindowState = WindowState.Normal; placement.EnsureVisible(false); pin.SetPinned(saved.pinned); RenderData(); Activate(); }

        // Render our own WPF content for visual QA, without capturing the user's desktop.
        void ExportPreview()
        {
            try
            {
                UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Width * 2, (int)Height * 2, 192, 192, PixelFormats.Pbgra32);
                bitmap.Render((Visual)Content);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(dataDir, "widget-preview.png"))) encoder.Save(stream);
            }
            catch (Exception e) { File.AppendAllText(Path.Combine(dataDir, "widget-error.log"), "Preview: " + e.Message + Environment.NewLine); }
        }

        void SetupTray()
        {
            tray = new Forms.NotifyIcon { Text = Lang.T("Agent Meter · 额度小组件", "Agent Meter · quota widget"), Icon = CreateIcon(), Visible = true };
            tray.ContextMenuStrip = BuildTrayMenu();
            tray.MouseClick += delegate(object sender, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(new Action(ShowWidget)); };
            var iconPath = Path.Combine(dataDir, "widget.ico");
            if (!File.Exists(iconPath)) { using (var file = File.Create(iconPath)) tray.Icon.Save(file); }
        }
        Forms.ContextMenuStrip BuildTrayMenu()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(Lang.T("显示小组件", "Show widget"), null, delegate { Dispatcher.Invoke(new Action(ShowWidget)); });
            menu.Items.Add(Lang.T("重置小组件位置", "Reset widget position"), null, delegate { Dispatcher.Invoke(new Action(delegate { ShowWidget(); placement.Reset(); })); });
            menu.Items.Add(Lang.T("打开完整面板", "Open dashboard"), null, delegate { OpenDashboard(); });
            menu.Items.Add(Lang.T("Codex 官方用量", "Codex usage"), null, delegate { OpenCodexUsage(); });
            menu.Items.Add(Lang.T("Claude 官方用量", "Claude usage"), null, delegate { OpenClaudeUsage(); });
            menu.Items.Add(Lang.T("收起小组件", "Hide widget"), null, delegate { Dispatcher.Invoke(new Action(Hide)); });
            menu.Items.Add(LanguageSwitchText(), null, delegate { Dispatcher.BeginInvoke(new Action(ToggleLanguage)); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(Lang.T("退出小组件", "Quit widget"), null, delegate { Dispatcher.Invoke(new Action(Close)); });
            return menu;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr hIcon);
        static Drawing.Icon CreateIcon()
        {
            using (var bitmap = new Drawing.Bitmap(32, 32))
            using (var g = Drawing.Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                // Same mark as public/icon.svg: graphite tile, white ring gauge.
                using (var background = new Drawing.SolidBrush(Drawing.Color.FromArgb(29, 29, 31))) g.FillEllipse(background, 0, 0, 32, 32);
                using (var track = new Drawing.Pen(Drawing.Color.FromArgb(70, 255, 255, 255), 4)) g.DrawEllipse(track, 6, 6, 20, 20);
                using (var arc = new Drawing.Pen(Drawing.Color.White, 4)) { arc.StartCap = Drawing.Drawing2D.LineCap.Round; arc.EndCap = Drawing.Drawing2D.LineCap.Round; g.DrawArc(arc, 6, 6, 20, 20, -90, 252); }
                var handle = bitmap.GetHicon();
                try { using (var original = Drawing.Icon.FromHandle(handle)) return (Drawing.Icon)original.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        void ReadState()
        {
            try { saved = json.Deserialize<WidgetState>(File.ReadAllText(Path.Combine(dataDir, "widget-state.json"))) ?? new WidgetState(); } catch { }
        }
        void SaveState()
        {
            if (placement != null && placement.IsReanchoring) return;
            try
            {
                saved.left = Left; saved.top = Top;
                if (placement != null) placement.Capture();
                var target = Path.Combine(dataDir, "widget-state.json");
                var tmp = target + ".tmp";
                File.WriteAllText(tmp, json.Serialize(saved));
                if (File.Exists(target)) File.Replace(tmp, target, null); else File.Move(tmp, target);
            }
            catch { }
        }
        void RestorePosition()
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 22; Top = area.Top + 70;
            if (!saved.left.HasValue || !saved.top.HasValue) return;
            var left = saved.left.Value; var top = saved.top.Value;
            if (Double.IsNaN(left) || Double.IsInfinity(left) || Double.IsNaN(top) || Double.IsInfinity(top)) return;
            // Migrate the legacy DIP coordinates; WindowPlacement validates against real monitor work areas after HWND creation.
            Left = left; Top = top;
        }
    }
}
