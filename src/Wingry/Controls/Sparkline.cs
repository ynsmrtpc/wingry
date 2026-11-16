using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Wingry.Controls;

/// <summary>Son N örneği çizen hafif çizgi grafik (0–100). Ölçüm/çizim maliyeti ihmal edilebilir.</summary>
public sealed partial class Sparkline : Grid
{
    private readonly Polyline _line = new() { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
    private readonly Polygon _fill = new() { Opacity = 0.18 };
    private readonly Queue<double> _values = new();

    public int Capacity { get; set; } = 60;

    public Sparkline()
    {
        Height = 44;
        Children.Add(_fill);
        Children.Add(_line);
        var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        _line.Stroke = accent;
        _fill.Fill = accent;
        SizeChanged += (_, _) => Redraw();
    }

    public void Push(double v)
    {
        v = Math.Clamp(v, 0, 100);
        if (_values.Count == 0) for (int i = 0; i < Capacity - 1; i++) _values.Enqueue(v); // ilk anda düz çizgi
        _values.Enqueue(v);
        while (_values.Count > Capacity) _values.Dequeue();
        Redraw();
    }

    private void Redraw()
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0 || _values.Count < 2) return;
        var pts = new PointCollection();
        var fill = new PointCollection();
        double step = w / (Capacity - 1);
        int offset = Capacity - _values.Count;
        int i = 0;
        foreach (var v in _values)
        {
            var p = new Point((offset + i) * step, h - 1 - v / 100.0 * (h - 2));
            pts.Add(p); fill.Add(p); i++;
        }
        fill.Add(new Point((Capacity - 1) * step, h));
        fill.Add(new Point(offset * step, h));
        _line.Points = pts;
        _fill.Points = fill;
    }
}
