using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace AgentHud
{
    public enum PetMood { Offline, Idle, Working, Tired, Exhausted }

    // A small original mascot per platform in its brand colours: Codex is a blue-violet cloud with a ">_"
    // prompt for a mouth, Claude an orange blob with a sparkle on a sprout. Its pose says what the
    // platform's agents are doing; the numbers are in the hover card. Moods:
    //   Working   — hops; Claude's sparkle spins, Codex's cursor blinks
    //   Idle      — breathes and blinks
    //   Tired     — under 20% left: half-closed eyes, slow breath, a bead of sweat
    //   Exhausted — a quota window is used up: melted flat, asleep, "z z Z" drifting up
    //   Offline   — the local service is down: greyed, eyes closed, still
    public sealed class AgentPet : Canvas
    {
        public const double Size = 30;
        readonly string platform;
        readonly ScaleTransform squash = new ScaleTransform(1, 1, 15, 27);
        readonly RotateTransform lean = new RotateTransform(0, 15, 27);
        readonly TranslateTransform hop = new TranslateTransform();
        readonly ScaleTransform blink;
        readonly Canvas openEyes = new Canvas(), closedEyes = new Canvas();
        readonly RotateTransform spin;
        readonly UIElement cursor;
        readonly TextBlock[] zs = new TextBlock[3];
        readonly TranslateTransform[] zMoves = new TranslateTransform[3];
        readonly Path sweat;
        readonly TranslateTransform sweatMove = new TranslateTransform();
        readonly Ellipse shadow;
        bool paused;
        public PetMood Mood { get; private set; }

        static readonly Color CodexTop = Color.FromRgb(0xb1, 0xa7, 0xff), CodexMid = Color.FromRgb(0x7a, 0x9d, 0xff), CodexBottom = Color.FromRgb(0x39, 0x41, 0xff);
        static readonly Color Clay = Color.FromRgb(0xd9, 0x77, 0x57), ClayDark = Color.FromRgb(0xb3, 0x5c, 0x40);

        public AgentPet(bool codex)
        {
            platform = codex ? "Codex" : "Claude Code";
            Width = Size; Height = Size;
            IsHitTestVisible = true; Background = Brushes.Transparent;

            shadow = new Ellipse { Width = 18, Height = 3, Fill = new SolidColorBrush(Color.FromArgb(34, 0, 0, 0)) };
            SetLeft(shadow, 6); SetTop(shadow, 26.2); Children.Add(shadow);

            var body = new Canvas { Width = Size, Height = Size };
            var moves = new TransformGroup(); moves.Children.Add(squash); moves.Children.Add(lean); moves.Children.Add(hop);
            body.RenderTransform = moves;
            Children.Add(body);

            double eyeY;
            Brush lid;
            if (codex)
            {
                // The Codex cloud: a rounded body with three bumps on top, lit from above like the logo.
                var cloud = new GeometryGroup { FillRule = FillRule.Nonzero };
                cloud.Children.Add(new RectangleGeometry(new Rect(5, 11.5, 20, 15.5), 7.5, 7.5));
                cloud.Children.Add(new EllipseGeometry(new Point(10, 12.5), 4.6, 4.6));
                cloud.Children.Add(new EllipseGeometry(new Point(15.4, 9.6), 5.3, 5.3));
                cloud.Children.Add(new EllipseGeometry(new Point(20.6, 12.8), 4.3, 4.3));
                var fill = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
                fill.GradientStops.Add(new GradientStop(CodexTop, 0)); fill.GradientStops.Add(new GradientStop(CodexMid, 0.5)); fill.GradientStops.Add(new GradientStop(CodexBottom, 1));
                body.Children.Add(new Path { Data = cloud, Fill = fill });
                eyeY = 16.3; lid = new SolidColorBrush(Color.FromRgb(0x14, 0x17, 0x52));
                // ">_" for a mouth; the underscore is the cursor that blinks while working.
                var prompt = new Path { Data = Geometry.Parse("M12.4,20.2 L14.2,21.4 L12.4,22.6"), Stroke = Brushes.White, StrokeThickness = 1.1, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Opacity = 0.92 };
                body.Children.Add(prompt);
                var underscore = new Rectangle { Width = 2.8, Height = 0.95, Fill = Brushes.White, Opacity = 0.92, RadiusX = 0.4, RadiusY = 0.4 };
                SetLeft(underscore, 15.0); SetTop(underscore, 22.1);
                body.Children.Add(underscore); cursor = underscore;
                AddCheeks(body, Color.FromArgb(90, 0xff, 0xb3, 0xe6), eyeY + 3.4);
            }
            else
            {
                // The Claude blob, with a sparkle like its mark growing from a little sprout.
                body.Children.Add(new Path { Data = new RectangleGeometry(new Rect(5, 11, 20, 16), 7.5, 7.5), Fill = new SolidColorBrush(Clay) });
                body.Children.Add(new Path { Data = Geometry.Parse("M15,11.2 Q15,8.6 15.9,7.4"), Stroke = new SolidColorBrush(ClayDark), StrokeThickness = 1.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                var sparkle = new GeometryGroup();
                for (var i = 0; i < 8; i++)
                {
                    var angle = i * Math.PI / 4; var reach = i % 2 == 0 ? 3.1 : 2.2;
                    sparkle.Children.Add(new LineGeometry(new Point(16, 5), new Point(16 + reach * Math.Sin(angle), 5 - reach * Math.Cos(angle))));
                }
                spin = new RotateTransform(0, 16, 5);
                body.Children.Add(new Path { Data = sparkle, Stroke = new SolidColorBrush(Clay), StrokeThickness = 1.25, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = spin });
                eyeY = 17.2; lid = new SolidColorBrush(Color.FromRgb(0x3b, 0x24, 0x18));
                body.Children.Add(new Path { Data = Geometry.Parse("M13.8,21.1 Q15,22.3 16.2,21.1"), Stroke = lid, StrokeThickness = 0.95, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                AddCheeks(body, Color.FromArgb(110, 0xff, 0xa8, 0x8c), eyeY + 3.2);
            }

            // Eyes: whites with dark pupils; they blink by squashing toward the eye line.
            var left = codex ? 11.3 : 11.6; var right = codex ? 18.7 : 18.4;
            foreach (var x in new[] { left, right })
            {
                var white = new Ellipse { Width = 4.4, Height = 5.0, Fill = Brushes.White };
                SetLeft(white, x - 2.2); SetTop(white, eyeY - 2.5); openEyes.Children.Add(white);
                var pupil = new Ellipse { Width = 2.4, Height = 2.9, Fill = new SolidColorBrush(Color.FromRgb(0x1d, 0x1d, 0x1f)) };
                SetLeft(pupil, x - 0.9); SetTop(pupil, eyeY - 1.1); openEyes.Children.Add(pupil);
                var shine = new Ellipse { Width = 0.9, Height = 0.9, Fill = Brushes.White };
                SetLeft(shine, x - 0.2); SetTop(shine, eyeY - 0.9); openEyes.Children.Add(shine);
                closedEyes.Children.Add(new Path { Data = Geometry.Parse(String.Format(System.Globalization.CultureInfo.InvariantCulture, "M{0},{2} Q{1},{3} {4},{2}", x - 2, x, eyeY, eyeY + 1.6, x + 2)), Stroke = lid, StrokeThickness = 1.1, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            }
            blink = new ScaleTransform(1, 1, 15, eyeY);
            openEyes.RenderTransform = blink;
            body.Children.Add(openEyes); body.Children.Add(closedEyes);

            sweat = new Path { Data = Geometry.Parse("M0,0 C-0.2,0.9 -1.5,2.2 -1.5,3.2 A1.5,1.5 0 0 0 1.5,3.2 C1.5,2.2 0.2,0.9 0,0 Z"), Fill = new SolidColorBrush(Color.FromRgb(0x7c, 0xc8, 0xff)), RenderTransform = sweatMove };
            SetLeft(sweat, 25); SetTop(sweat, 8.5); Children.Add(sweat);

            for (var i = 0; i < 3; i++)
            {
                zMoves[i] = new TranslateTransform();
                zs[i] = new TextBlock { Text = i == 2 ? "Z" : "z", FontSize = 5.5 + i * 1.2, FontWeight = FontWeights.Bold, RenderTransform = zMoves[i], IsHitTestVisible = false };
                zs[i].SetResourceReference(TextBlock.ForegroundProperty, "Text");
                SetLeft(zs[i], 20 + i * 2.6); SetTop(zs[i], 10 - i * 3.4); Children.Add(zs[i]);
            }
            Mood = (PetMood)(-1);
            SetMood(PetMood.Idle);
        }

        // Read as one image named by its mood; its parts (eyes, z's) aren't announced.
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() { return new PetPeer(this); }
        sealed class PetPeer : System.Windows.Automation.Peers.FrameworkElementAutomationPeer
        {
            public PetPeer(FrameworkElement owner) : base(owner) { }
            protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() { return System.Windows.Automation.Peers.AutomationControlType.Image; }
            protected override string GetClassNameCore() { return "AgentPet"; }
            protected override System.Collections.Generic.List<System.Windows.Automation.Peers.AutomationPeer> GetChildrenCore() { return null; }
        }

        static void AddCheeks(Canvas body, Color color, double y)
        {
            foreach (var x in new[] { 8.6, 21.4 })
            {
                var cheek = new Ellipse { Width = 3, Height = 1.9, Fill = new SolidColorBrush(color) };
                SetLeft(cheek, x - 1.5); SetTop(cheek, y - 0.95); body.Children.Add(cheek);
            }
        }

        // Which pose fits: an empty quota window beats everything (the agent can't work), then work, then a low quota.
        public static PetMood MoodFor(Provider provider, RingValue shown, bool online, double now)
        {
            if (!online || provider == null) return PetMood.Offline;
            var limits = provider.limits;
            if (limits != null && !limits.stale && limits.windows != null && limits.windows.Any(w => w != null && w.remainingPercent.HasValue && w.remainingPercent.Value <= 0.5 && (!w.resetsAt.HasValue || w.resetsAt.Value > now)))
                return PetMood.Exhausted;
            if (provider.activity != null && provider.activity.working) return PetMood.Working;
            if (shown != null && shown.Percent.HasValue && shown.Percent.Value < 20) return PetMood.Tired;
            return PetMood.Idle;
        }
        public static string Describe(PetMood mood)
        {
            switch (mood)
            {
                case PetMood.Working: return Lang.T("工作中", "Working");
                case PetMood.Tired: return Lang.T("额度偏低", "Running low");
                case PetMood.Exhausted: return Lang.T("额度已用尽", "Out of quota");
                case PetMood.Offline: return Lang.T("服务离线", "Service offline");
                default: return Lang.T("空闲", "Idle");
            }
        }

        public void SetMood(PetMood mood)
        {
            if (mood == Mood) return;
            Mood = mood;
            AutomationProperties.SetName(this, platform + " · " + Describe(mood));
            Apply();
        }
        // Hidden widgets don't animate; the pose stays so it's right the moment the widget comes back.
        public void Pause() { paused = true; Apply(); }
        public void Resume() { if (!paused) return; paused = false; Apply(); }
        // Called after a language switch so the screen-reader name follows.
        public void Relabel() { AutomationProperties.SetName(this, platform + " · " + Describe(Mood)); }

        static DoubleAnimation Loop(double from, double to, double seconds, IEasingFunction ease = null, bool reverse = true)
        {
            var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds)) { AutoReverse = reverse, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease ?? new SineEase { EasingMode = EasingMode.EaseInOut } };
            Timeline.SetDesiredFrameRate(animation, 30);
            return animation;
        }
        static void Run(Animatable target, DependencyProperty property, AnimationTimeline animation, double rest)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, rest);
            if (animation != null) target.BeginAnimation(property, animation);
        }
        static void Run(UIElement target, DependencyProperty property, AnimationTimeline animation, double rest)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, rest);
            if (animation != null) target.BeginAnimation(property, animation);
        }

        void Apply()
        {
            var moving = !paused && SystemParameters.ClientAreaAnimation;
            var mood = Mood;
            Opacity = mood == PetMood.Offline ? 0.45 : 1;
            var asleep = mood == PetMood.Offline || mood == PetMood.Exhausted;
            openEyes.Visibility = asleep ? Visibility.Collapsed : Visibility.Visible;
            closedEyes.Visibility = asleep ? Visibility.Visible : Visibility.Collapsed;
            sweat.Visibility = mood == PetMood.Tired ? Visibility.Visible : Visibility.Collapsed;
            foreach (var z in zs) z.Visibility = mood == PetMood.Exhausted ? Visibility.Visible : Visibility.Collapsed;
            shadow.Width = mood == PetMood.Exhausted ? 23 : 18; SetLeft(shadow, mood == PetMood.Exhausted ? 3.5 : 6);

            // Body: melted flat when exhausted, hopping when working, breathing otherwise.
            var flatY = mood == PetMood.Exhausted ? 0.6 : 1;
            var flatX = mood == PetMood.Exhausted ? 1.16 : 1;
            var breathe = mood == PetMood.Working ? null
                : mood == PetMood.Exhausted ? Loop(0.6, 0.63, 3.2)
                : mood == PetMood.Tired ? Loop(1, 1.03, 3.6)
                : mood == PetMood.Idle ? Loop(1, 1.035, 2.6) : null;
            Run(squash, ScaleTransform.ScaleYProperty, moving ? (mood == PetMood.Working ? Loop(0.95, 1.04, 0.34, new QuadraticEase { EasingMode = EasingMode.EaseOut }) : breathe) : null, flatY);
            Run(squash, ScaleTransform.ScaleXProperty, moving && mood == PetMood.Idle ? Loop(1, 0.985, 2.6) : null, flatX);
            Run(hop, TranslateTransform.YProperty, moving && mood == PetMood.Working ? Loop(0, -2.4, 0.34, new QuadraticEase { EasingMode = EasingMode.EaseOut }) : null, 0);
            Run(lean, RotateTransform.AngleProperty, null, mood == PetMood.Exhausted ? -6 : mood == PetMood.Tired ? 3 : 0);

            // Eyes: blink every few seconds; half-closed when tired.
            if (moving && (mood == PetMood.Idle || mood == PetMood.Working))
            {
                var frames = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(mood == PetMood.Working ? 3.2 : 4.2), RepeatBehavior = RepeatBehavior.Forever };
                var closeAt = mood == PetMood.Working ? 2.9 : 3.8;
                frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                frames.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(closeAt))));
                frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(closeAt + 0.07))));
                frames.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(closeAt + 0.16))));
                Timeline.SetDesiredFrameRate(frames, 30);
                Run(blink, ScaleTransform.ScaleYProperty, frames, 1);
            }
            else Run(blink, ScaleTransform.ScaleYProperty, null, mood == PetMood.Tired ? 0.5 : 1);

            // Accents: the sparkle spins and the cursor blinks only while working.
            if (spin != null) Run(spin, RotateTransform.AngleProperty, moving && mood == PetMood.Working ? Loop(0, 360, 1.4, new PowerEase { Power = 1 }, false) : null, 0);
            if (cursor != null) Run(cursor, OpacityProperty, moving && mood == PetMood.Working ? new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1), RepeatBehavior = RepeatBehavior.Forever, KeyFrames = { new DiscreteDoubleKeyFrame(0.92, KeyTime.FromPercent(0)), new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0.5)) } } : null, 0.92);
            Run(sweatMove, TranslateTransform.YProperty, moving && mood == PetMood.Tired ? Loop(0, 2.2, 1.4) : null, 0);

            // Sleep: three z's drift up and fade, one after another.
            for (var i = 0; i < 3; i++)
            {
                if (moving && mood == PetMood.Exhausted)
                {
                    var rise = new DoubleAnimation(0, -7, TimeSpan.FromSeconds(2.7)) { BeginTime = TimeSpan.FromSeconds(i * 0.9), RepeatBehavior = RepeatBehavior.Forever };
                    var drift = new DoubleAnimation(0, 3, TimeSpan.FromSeconds(2.7)) { BeginTime = TimeSpan.FromSeconds(i * 0.9), RepeatBehavior = RepeatBehavior.Forever };
                    var fade = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromSeconds(i * 0.9), Duration = TimeSpan.FromSeconds(2.7), RepeatBehavior = RepeatBehavior.Forever };
                    fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                    fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.3)));
                    fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                    foreach (var t in new Timeline[] { rise, drift, fade }) Timeline.SetDesiredFrameRate(t, 30);
                    Run(zMoves[i], TranslateTransform.YProperty, rise, 0);
                    Run(zMoves[i], TranslateTransform.XProperty, drift, 0);
                    Run(zs[i], OpacityProperty, fade, 0);
                }
                else
                {
                    Run(zMoves[i], TranslateTransform.YProperty, null, 0);
                    Run(zMoves[i], TranslateTransform.XProperty, null, 0);
                    Run(zs[i], OpacityProperty, null, 0.55 + i * 0.15);
                }
            }
        }
    }
}
