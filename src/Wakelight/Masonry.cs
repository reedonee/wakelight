using System;
using System.Windows;
using System.Windows.Controls;

namespace Wakelight
{
    /// <summary>
    /// Lays cards out in N columns, each card going into the currently shortest
    /// column, keeping their order as much as possible. Columns = 1 behaves like a StackPanel.
    /// </summary>
    public class Masonry : Panel
    {
        public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
            "Columns", typeof(int), typeof(Masonry),
            new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
            "Gap", typeof(double), typeof(Masonry),
            new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public int Columns { get { return (int)GetValue(ColumnsProperty); } set { SetValue(ColumnsProperty, value); } }
        public double Gap { get { return (double)GetValue(GapProperty); } set { SetValue(GapProperty, value); } }

        Rect[] slots = new Rect[0];

        protected override Size MeasureOverride(Size available)
        {
            int cols = Math.Max(1, Columns);
            double width = double.IsInfinity(available.Width) ? 400 : available.Width;
            double colW = (width - Gap * (cols - 1)) / cols;
            var heights = new double[cols];
            slots = new Rect[InternalChildren.Count];
            for (int i = 0; i < InternalChildren.Count; i++)
            {
                var c = InternalChildren[i];
                if (c.Visibility == Visibility.Collapsed) { slots[i] = Rect.Empty; continue; }
                c.Measure(new Size(colW, double.PositiveInfinity));
                int k = 0;
                for (int j = 1; j < cols; j++) if (heights[j] < heights[k] - 1) k = j;
                slots[i] = new Rect(k * (colW + Gap), heights[k], colW, c.DesiredSize.Height);
                heights[k] += c.DesiredSize.Height;
            }
            double h = 0; foreach (var v in heights) h = Math.Max(h, v);
            return new Size(width, h);
        }

        protected override Size ArrangeOverride(Size final)
        {
            for (int i = 0; i < InternalChildren.Count && i < slots.Length; i++)
                if (!slots[i].IsEmpty) InternalChildren[i].Arrange(slots[i]);
            return final;
        }
    }
}
