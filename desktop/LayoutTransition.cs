using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace AgentHud
{
    // Lay out the destination once, then move the live controls from their old screen
    // positions as the native glass window changes shape. Text and rings never stretch.
    sealed class LayoutTransition
    {
        sealed class Item
        {
            public FrameworkElement Element;
            public Point Start, End;
            public Transform Original;
            public readonly TranslateTransform Offset = new TranslateTransform();
        }
        readonly FrameworkElement root;
        readonly Item[] items;
        readonly Point inset;
        readonly Matrix scale;

        public LayoutTransition(FrameworkElement root, Rect before, params FrameworkElement[] elements)
        {
            this.root = root;
            var origin = root.PointToScreen(new Point());
            inset = new Point(origin.X - before.Left, origin.Y - before.Top);
            scale = PresentationSource.FromVisual(root).CompositionTarget.TransformToDevice;
            items = elements.Select(element => new Item {
                Element = element,
                Start = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2)),
                Original = element.RenderTransform
            }).ToArray();
        }

        public void Prepare(Action arrange)
        {
            arrange();
            root.UpdateLayout();
            foreach (var item in items)
            {
                item.End = item.Element.TranslatePoint(new Point(item.Element.ActualWidth / 2, item.Element.ActualHeight / 2), root);
                var transform = new TransformGroup();
                transform.Children.Add(item.Original);
                transform.Children.Add(item.Offset);
                item.Element.RenderTransform = transform;
            }
        }

        public void Render(double progress, Rect current, Rect target)
        {
            // Use the upcoming native origin, before the HWND resize can trigger a draw.
            var origin = new Point(current.Left + inset.X, current.Top + inset.Y);
            foreach (var item in items)
            {
                var endX = target.Left + inset.X + item.End.X * scale.M11;
                var endY = target.Top + inset.Y + item.End.Y * scale.M22;
                var x = item.Start.X + (endX - item.Start.X) * progress;
                var y = item.Start.Y + (endY - item.Start.Y) * progress;
                item.Offset.X = (x - origin.X) / scale.M11 - item.End.X;
                item.Offset.Y = (y - origin.Y) / scale.M22 - item.End.Y;
            }
        }

        public void Finish()
        {
            foreach (var item in items) item.Element.RenderTransform = item.Original;
        }
    }
}
