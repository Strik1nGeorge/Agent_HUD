using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AgentHud
{
    // The card shown while hovering an agent: every quota window the platform reports (the one in the
    // ring marked), when each resets, what its agents are doing now, today's usage, and where it came from.
    public static class AgentDetail
    {
        static readonly Color CodexTop = Color.FromRgb(0xb1, 0xa7, 0xff), CodexBottom = Color.FromRgb(0x39, 0x41, 0xff), Clay = Color.FromRgb(0xd9, 0x77, 0x57);

        public static UIElement Build(Provider provider, bool codex, int period, double now, bool online, PetMood mood)
        {
            var card = new StackPanel { Width = 236 };
            var limits = provider == null ? null : provider.limits;

            // Header: mark, name, plan, and what the agents are doing.
            var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
            var status = Text(StatusLine(provider, mood).Substring(2), 11.5, FontWeights.Normal, true);
            status.VerticalAlignment = VerticalAlignment.Center; status.TextWrapping = TextWrapping.NoWrap;
            // The dot carries the state's colour: green at work, red out of quota, otherwise the text's grey.
            var dot = new System.Windows.Documents.Run("● ");
            if (mood == PetMood.Working) dot.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xc7, 0x59));
            else if (mood == PetMood.Exhausted) dot.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Low");
            status.Inlines.InsertBefore(status.Inlines.FirstInline, dot);
            DockPanel.SetDock(status, Dock.Right);
            header.Children.Add(status);
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new Path { Data = codex ? Logos.Codex : Logos.Claude, Fill = Brand(codex, true), Width = 14, Height = 14, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            title.Children.Add(Text(codex ? "Codex" : "Claude Code", 13, FontWeights.SemiBold, false));
            if (limits != null && !String.IsNullOrEmpty(limits.plan))
            {
                var plan = Text(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(limits.plan.ToLowerInvariant()), 9.5, FontWeights.SemiBold, true);
                var chip = new Border { Child = plan, Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(6, 1, 0, 0), CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center };
                chip.SetResourceReference(Border.BackgroundProperty, "IconHover");
                title.Children.Add(chip);
            }
            header.Children.Add(title);
            card.Children.Add(header);

            // Quota windows, shortest first; the one the ring shows is marked.
            var windows = limits == null || limits.windows == null ? null : limits.windows.Where(w => w != null && w.remainingPercent.HasValue).OrderBy(w => w.windowMinutes ?? Double.MaxValue).ToList();
            if (windows != null && windows.Count > 0)
            {
                var shown = RingValue.PickWindow(limits.windows, period);
                foreach (var window in windows) card.Children.Add(WindowRow(window, window == shown && windows.Count > 1, codex, now));
            }
            else
            {
                var message = provider == null ? null : Lang.Server(provider.quotaMessage);
                card.Children.Add(Paragraph(message ?? Lang.T("这个平台还没有提供额度。", "This platform hasn't reported a quota yet.")));
            }

            // Today's usage and the latest model.
            var activity = provider == null ? null : provider.activity;
            if (activity != null)
            {
                var block = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
                block.Children.Add(Text(Lang.T("今日 ", "Today ") + Compact(activity.todayTokens) + " tokens · " + activity.todayRequests.ToString("N0", CultureInfo.InvariantCulture) + Lang.T(" 次请求", activity.todayRequests == 1 ? " request" : " requests"), 11.5, FontWeights.Normal, false));
                if (!String.IsNullOrEmpty(activity.model))
                    block.Children.Add(Text(Lang.T("最近模型 ", "Latest model ") + activity.model + (String.IsNullOrEmpty(activity.effort) ? "" : " · " + activity.effort), 11.5, FontWeights.Normal, false));
                if (activity.lastActiveAt.HasValue)
                    block.Children.Add(Text(Lang.T("最近活动：", "Last active ") + Age(activity.lastActiveAt.Value, now), 11.5, FontWeights.Normal, true));
                card.Children.Add(block);
            }

            // Where the quota came from, and how fresh it is.
            if (limits != null)
            {
                var source = Lang.Server(limits.source) ?? Lang.T("平台额度", "Platform quota");
                var footer = Text(source + "\n" + RingValue.UpdatedText(limits.collectedAt, now) + (online ? "" : Lang.T("\n本地服务离线，显示的是最后一次快照", "\nLocal service offline; showing the last snapshot")), 10.5, FontWeights.Normal, true);
                footer.Margin = new Thickness(0, 8, 0, 0);
                card.Children.Add(footer);
            }
            return card;
        }

        static string StatusLine(Provider provider, PetMood mood)
        {
            var text = "● " + AgentPet.Describe(mood);
            var activity = provider == null ? null : provider.activity;
            if (mood == PetMood.Working && activity != null && activity.activeSessions > 1)
                text += Lang.T(" · " + activity.activeSessions + " 个会话", " · " + activity.activeSessions + " sessions");
            return text;
        }

        static UIElement WindowRow(QuotaWindow window, bool current, bool codex, double now)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };
            var top = new DockPanel { LastChildFill = true };
            var percent = Math.Max(0, Math.Min(100, window.remainingPercent.Value));
            var expired = window.resetsAt.HasValue && window.resetsAt.Value <= now;
            var value = Text(expired ? "—" : Math.Round(percent).ToString(CultureInfo.InvariantCulture) + "%", 12.5, FontWeights.SemiBold, false);
            if (!expired && percent < 20) value.SetResourceReference(TextBlock.ForegroundProperty, "Low");
            DockPanel.SetDock(value, Dock.Right);
            top.Children.Add(value);
            var name = Lang.Server(window.name) ?? DurationName(window.windowMinutes);
            top.Children.Add(Text(name + (current ? Lang.T(" · 圆环显示", " · in the ring") : ""), 12, FontWeights.Normal, false));
            row.Children.Add(top);

            // Bar in the platform's colour over its track.
            var bar = new Grid { Height = 4, Margin = new Thickness(0, 3, 0, 3) };
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(codex ? Color.FromArgb(56, 0x7a, 0x9d, 0xff) : Color.FromArgb(56, 0xd9, 0x77, 0x57)) });
            var fill = new Grid();
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(expired ? 0 : percent, GridUnitType.Star) });
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(expired ? 100 : 100 - percent, GridUnitType.Star) });
            fill.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Brand(codex, false) });
            bar.Children.Add(fill);
            row.Children.Add(bar);

            var reset = expired ? Lang.T("已过重置时间，等待更新", "Reset time passed; waiting for an update")
                : window.resetsAt.HasValue ? RingValue.ResetText(window.resetsAt.Value - now) + Lang.T("（" + RingValue.Stamp(window.resetsAt.Value) + "）", " (" + RingValue.Stamp(window.resetsAt.Value) + ")")
                : Lang.T("重置时间未提供", "Reset time not provided");
            row.Children.Add(Text(reset, 10.5, FontWeights.Normal, true));
            return row;
        }

        static Brush Brand(bool codex, bool vertical)
        {
            if (!codex) return new SolidColorBrush(Clay);
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = vertical ? new Point(0, 1) : new Point(1, 0) };
            brush.GradientStops.Add(new GradientStop(CodexTop, 0)); brush.GradientStops.Add(new GradientStop(CodexBottom, 1));
            return brush;
        }

        static TextBlock Text(string text, double size, FontWeight weight, bool muted)
        {
            var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap };
            block.SetResourceReference(TextBlock.ForegroundProperty, muted ? "TextMuted" : "Text");
            return block;
        }
        static TextBlock Paragraph(string text) { var block = Text(text, 11.5, FontWeights.Normal, true); block.Margin = new Thickness(0, 0, 0, 4); return block; }

        // A window's name from its length, for when the service didn't name it.
        static string DurationName(double? minutes)
        {
            if (!minutes.HasValue || minutes.Value <= 0) return Lang.T("额度", "Quota");
            var m = minutes.Value;
            if (m % 1440 == 0) return Lang.T(m / 1440 + " 天", m / 1440 + (m == 1440 ? " day" : " days"));
            if (m % 60 == 0) return Lang.T(m / 60 + " 小时", m / 60 + (m == 60 ? " hour" : " hours"));
            return Lang.T(m + " 分钟", m + " min");
        }
        public static string Compact(double value)
        {
            if (value >= 1e9) return (value / 1e9).ToString("0.#", CultureInfo.InvariantCulture) + "B";
            if (value >= 1e6) return (value / 1e6).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (value >= 1e3) return (value / 1e3).ToString("0.#", CultureInfo.InvariantCulture) + "K";
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }
        static string Age(double time, double now)
        {
            var minutes = Math.Floor(Math.Max(0, now - time) / 60000);
            return minutes < 1 ? Lang.T("刚刚", "just now")
                : minutes < 60 ? Lang.T(minutes + " 分钟前", minutes + " min ago")
                : minutes < 1440 ? Lang.T(Math.Floor(minutes / 60) + " 小时前", Math.Floor(minutes / 60) + " h ago")
                : Lang.T(Math.Floor(minutes / 1440) + " 天前", Math.Floor(minutes / 1440) + " d ago");
        }
    }
}
