using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BatonE2E.Core;
using BatonE2E.Core.Engine;

namespace BatonE2E.Gui;

/// <summary>中継画面最下部の転送量グラフ（入力・出力を1秒ごとに記録し、直近2分を折れ線で表示）。</summary>
public partial class MainWindow
{
    private readonly BandwidthMeter _meter = new();
    private Polyline _lineIn = null!, _lineOut = null!;
    private Line _lineMid = null!;
    private TextBlock _graphMax = null!;

    private void InitGraph()
    {
        tbGraphTitle.Text = Loc.Get("gui.graph.title");
        _lineMid = new Line { Stroke = (Brush)FindResource("LineBrush"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 } };
        _lineOut = new Polyline { Stroke = (Brush)FindResource("AccentHoverBrush"), StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round };
        _lineIn = new Polyline { Stroke = _okBrush, StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round };
        _graphMax = new TextBlock { FontSize = 11, Foreground = (Brush)FindResource("MutedBrush") };
        cvGraph.Children.Add(_lineMid);
        cvGraph.Children.Add(_lineOut);
        cvGraph.Children.Add(_lineIn);
        cvGraph.Children.Add(_graphMax);
        cvGraph.SizeChanged += (_, _) => DrawGraph();
        UpdateGraphLegend();
        DrawGraph();
    }

    private void GraphReset()
    {
        _meter.Reset();
        UpdateGraphLegend();
        DrawGraph();
    }

    /// <summary>1秒ごとに呼ぶ。中継中だけ記録する（停止後は最後の形を残す）。</summary>
    private void GraphTick()
    {
        var eng = _engine;
        if (eng == null) return;
        _meter.Sample(eng.Stats.BytesIn, eng.Stats.BytesOut, DateTime.UtcNow);
        UpdateGraphLegend();
        DrawGraph();
    }

    private void UpdateGraphLegend()
    {
        var t = tbGraphNow;
        t.Inlines.Clear();
        t.Inlines.Add(new System.Windows.Documents.Run("■ ") { Foreground = _okBrush });
        t.Inlines.Add(new System.Windows.Documents.Run(Loc.Get("gui.graph.in", _meter.LastIn.ToString("0")) + "    "));
        t.Inlines.Add(new System.Windows.Documents.Run("■ ") { Foreground = (Brush)FindResource("AccentHoverBrush") });
        t.Inlines.Add(new System.Windows.Documents.Run(Loc.Get("gui.graph.out", _meter.LastOut.ToString("0"))));
    }

    private void DrawGraph()
    {
        var w = cvGraph.ActualWidth;
        var h = cvGraph.ActualHeight;
        if (w < 20 || h < 20) return;
        var max = BandwidthMeter.NiceMax(Math.Max(_meter.In.DefaultIfEmpty(0).Max(), _meter.Out.DefaultIfEmpty(0).Max()));
        _graphMax.Text = Loc.Get("gui.graph.max", max.ToString("0"));
        _lineMid.X1 = 0; _lineMid.X2 = w; _lineMid.Y1 = _lineMid.Y2 = h / 2;
        Fill(_lineIn, _meter.In);
        Fill(_lineOut, _meter.Out);

        void Fill(Polyline pl, IReadOnlyList<double> v)
        {
            var pts = new PointCollection();
            var step = w / (BandwidthMeter.Capacity - 1);
            for (var i = 0; i < v.Count; i++)
            {
                // 新しい点が右端。値は縦軸の上限に対する割合
                var x = w - (v.Count - 1 - i) * step;
                pts.Add(new Point(x, h - 1 - Math.Min(1, v[i] / max) * (h - 2)));
            }
            pl.Points = pts;
        }
    }
}
