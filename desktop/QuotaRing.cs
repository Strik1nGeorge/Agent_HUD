using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AgentHud
{
    // Remaining-quota ring in the platform's own colours, with the remaining percentage and when the
    // quota resets written inside it. The platform itself is named by the pet next to the ring.
    public class QuotaRing : FrameworkElement
    {
        // Animated stand-in for Percent, so the arc moves to new values instead of jumping.
        public static readonly DependencyProperty DisplayProperty = DependencyProperty.Register("Display", typeof(double), typeof(QuotaRing), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public double Display { get { return (double)GetValue(DisplayProperty); } set { SetValue(DisplayProperty, value); } }
        public const double Diameter = 42;
        public Brush ArcBrush { get; set; }
        public Color Track { get; set; }
        // Text on glass stays in the primary colour (grey on glass measures as low as 2.3 : 1);
        // hierarchy comes from size and weight. Red only for a nearly used-up quota, on the number itself.
        public Brush TextBrush { get; set; }
        public Brush LowBrush { get; set; }
        public double? Percent { get; private set; }
        public bool Stale { get; private set; }
        public string Platform { get; set; }
        // What the ring says: "62%" or a status word, and under it the reset time, "离线" or "剩余".
        public string Primary { get; private set; }
        public string Secondary { get; private set; }
        Pen trackPen, arcPen;
        Color penTrack;
        Brush penArc;
        bool penUnknown;
        Geometry cachedArc;
        double cachedDisplay = Double.NaN;
        static readonly Typeface FaceBold = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        static readonly Typeface FaceRegular = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        static readonly Typeface FaceSmall = new Typeface(new FontFamily("Segoe UI Variable Small, Segoe UI, Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        public QuotaRing() { Width = Diameter; Height = Diameter; }
        // The numbers are drawn, not text elements, so screen readers get them from the ring's own name.
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() { return new RingPeer(this); }
        sealed class RingPeer : System.Windows.Automation.Peers.FrameworkElementAutomationPeer
        {
            public RingPeer(FrameworkElement owner) : base(owner) { }
            protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() { return System.Windows.Automation.Peers.AutomationControlType.Text; }
            protected override string GetClassNameCore() { return "QuotaRing"; }
        }

        public void PauseAnimation()
        {
            BeginAnimation(DisplayProperty, null);
            Display = Percent ?? 0;
        }

        public void Update(RingValue value, bool offline)
        {
            var changed = Percent != value.Percent;
            var staleChanged = Stale != value.Stale;
            Percent = value.Percent; Stale = value.Stale;
            string primary, secondary;
            if (Percent.HasValue)
            {
                primary = Math.Round(Percent.Value).ToString(CultureInfo.InvariantCulture) + "%";
                var badge = value.ResetsAt.HasValue ? RingValue.RingBadge(value.ResetsAt.Value, RingValue.Now()) : null;
                // Offline keeps its word; without a reset time the ring just says what the number is.
                secondary = offline ? Lang.T("离线", "Offline") : badge ?? (Stale ? Lang.T("快照", "Snapshot") : Lang.T("剩余", "left"));
            }
            else
            {
                primary = offline ? Lang.T("离线", "Offline")
                    : value.Status == RingStatus.Setup ? Lang.T("待连接", "Setup")
                    : value.Status == RingStatus.NotLinked ? Lang.T("未接入", "No link")
                    : value.Status == RingStatus.Expired ? Lang.T("待更新", "Pending")
                    : value.Status == RingStatus.Waiting ? Lang.T("等待", "Waiting")
                    : Lang.T("未提供", "N/A");
                secondary = null;
            }
            var textChanged = primary != Primary || secondary != Secondary;
            Primary = primary; Secondary = secondary;
            var name = Platform + Lang.T(" 剩余额度 " + primary + (secondary != null ? "，" + secondary : ""), " quota remaining " + primary + (secondary != null ? ", " + secondary : ""));
            if (AutomationProperties.GetName(this) != name) AutomationProperties.SetName(this, name);
            if (changed)
            {
                // Critically damped: a strong ease-out, no overshoot.
                if (IsVisible && Percent.HasValue && SystemParameters.ClientAreaAnimation)
                    BeginAnimation(DisplayProperty, new DoubleAnimation(Percent.Value, TimeSpan.FromMilliseconds(600)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } });
                else { BeginAnimation(DisplayProperty, null); Display = Percent ?? 0; }
            }
            if (changed || staleChanged || textChanged) InvalidateVisual();
        }

        FormattedText Text(string text, Typeface face, double size, Brush brush)
        {
            return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            const double half = Diameter / 2, thickness = 3.2, radius = half - thickness / 2 - 0.4;
            var center = new Point(half, half);
            if (trackPen == null || penTrack != Track || penUnknown != !Percent.HasValue)
            {
                trackPen = new Pen(new SolidColorBrush(Track), thickness);
                if (!Percent.HasValue) { trackPen.DashStyle = new DashStyle(new double[] { 0, 1.8 }, 0); trackPen.DashCap = PenLineCap.Round; }
                trackPen.Freeze(); penTrack = Track; penUnknown = !Percent.HasValue;
            }
            dc.DrawEllipse(null, trackPen, center, radius, radius);

            dc.PushOpacity(!Percent.HasValue ? 0.4 : Stale ? 0.5 : 1);
            var value = Math.Max(0, Math.Min(100, Display));
            if (Percent.HasValue && value > 0.05 && ArcBrush != null)
            {
                if (cachedArc == null || cachedDisplay != value)
                {
                    if (value >= 99.95) cachedArc = new EllipseGeometry(center, radius, radius);
                    else
                    {
                        var angle = value / 100 * 2 * Math.PI;
                        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
                        var geometry = new StreamGeometry();
                        using (var ctx = geometry.Open())
                        {
                            ctx.BeginFigure(new Point(center.X, center.Y - radius), false, false);
                            ctx.ArcTo(end, new Size(radius, radius), 0, value > 50, SweepDirection.Clockwise, true, false);
                        }
                        cachedArc = geometry;
                    }
                    cachedArc.Freeze(); cachedDisplay = value;
                }
                if (arcPen == null || penArc != ArcBrush)
                {
                    arcPen = new Pen(ArcBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    if (arcPen.CanFreeze) arcPen.Freeze(); penArc = ArcBrush;
                }
                dc.DrawGeometry(null, arcPen, cachedArc);
            }
            dc.Pop();

            if (TextBrush == null || Primary == null) return;
            if (Percent.HasValue)
            {
                // "62" large with a small "%" on the same baseline, the reset time underneath.
                var number = Text(Primary.TrimEnd('%'), Stale ? FaceRegular : FaceBold, 13, Percent.Value < 20 && LowBrush != null ? LowBrush : TextBrush);
                var sign = Text("%", FaceRegular, 7.5, TextBrush);
                var width = number.WidthIncludingTrailingWhitespace + sign.WidthIncludingTrailingWhitespace;
                var top = half - 1.5 - number.Height / 2 - (Secondary != null ? 3.5 : 0);
                var left = half - width / 2;
                dc.DrawText(number, new Point(left, top));
                dc.DrawText(sign, new Point(left + number.WidthIncludingTrailingWhitespace, top + number.Baseline - sign.Baseline));
                if (Secondary != null)
                {
                    var under = Text(Secondary, FaceSmall, 7.2, TextBrush);
                    under.MaxTextWidth = Diameter - 10; under.MaxLineCount = 1; under.Trimming = TextTrimming.CharacterEllipsis;
                    dc.PushOpacity(0.86);
                    dc.DrawText(under, new Point(half - Math.Min(under.Width, Diameter - 10) / 2, half + 4.6));
                    dc.Pop();
                }
            }
            else
            {
                var status = Text(Primary, FaceRegular, 8.5, TextBrush);
                status.MaxTextWidth = Diameter - 8; status.MaxLineCount = 1; status.Trimming = TextTrimming.CharacterEllipsis;
                dc.DrawText(status, new Point(half - Math.Min(status.Width, Diameter - 8) / 2, half - status.Height / 2));
            }
        }
    }
}
