using Rotina.Application;
using Microsoft.Maui.Graphics;

namespace Rotina.Mobile;

internal static class TrendChart
{
    public static View Create(IReadOnlyList<TrendPoint> points, string empty)
    {
        if (!points.Any(p => double.IsFinite(p.Value))) return PageLayout.Muted(empty);
        return new GraphicsView { Drawable = new LineDrawable(points), HeightRequest = 180 };
    }

    private sealed class LineDrawable(IReadOnlyList<TrendPoint> points) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            var finite = points.Where(p => double.IsFinite(p.Value)).ToList();
            if (finite.Count == 0) return;
            var min = finite.Min(p => p.Value);
            var max = finite.Max(p => p.Value);
            var span = Math.Max(0.01, max - min);
            var left = 18f; var right = rect.Width - 18f;
            var top = 16f; var bottom = rect.Height - 35f;
            canvas.StrokeColor = Color.FromArgb("#53655B");
            canvas.StrokeSize = 1;
            canvas.DrawLine(left, bottom, right, bottom);
            canvas.StrokeColor = Color.FromArgb("#88E0B0");
            canvas.StrokeSize = 3;
            float? lastX = null, lastY = null;
            var firstDay = points.Min(p => p.Date.DayNumber);
            var daySpan = Math.Max(1, points.Max(p => p.Date.DayNumber) - firstDay);
            foreach (var point in points)
            {
                if (!double.IsFinite(point.Value)) { lastX = null; lastY = null; continue; }
                var x = left + (right - left) * (point.Date.DayNumber - firstDay) / daySpan;
                var y = bottom - (bottom - top) * (float)((point.Value - min) / span);
                if (lastX is { } px && lastY is { } py) canvas.DrawLine(px, py, x, y);
                canvas.FillColor = Color.FromArgb("#88E0B0");
                canvas.FillCircle(x, y, 4);
                lastX = x; lastY = y;
            }
            canvas.FontColor = Color.FromArgb("#AABDB1");
            canvas.FontSize = 11;
            canvas.DrawString(points.First().Date.ToString("dd/MM"), left, bottom + 8, 75, 20, HorizontalAlignment.Left, VerticalAlignment.Center);
            canvas.DrawString(points.Last().Date.ToString("dd/MM"), right - 75, bottom + 8, 75, 20, HorizontalAlignment.Right, VerticalAlignment.Center);
        }
    }
}
