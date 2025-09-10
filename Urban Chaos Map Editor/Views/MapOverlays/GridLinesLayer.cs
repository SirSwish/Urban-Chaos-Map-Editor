using System.Windows;
using System.Windows.Media;
using UrbanChaosMapEditor.Models;

namespace UrbanChaosMapEditor.Views.Overlays
{
    /// <summary>Draws 64x64 tile grid (black) over the map.</summary>
    public sealed class GridLinesLayer : FrameworkElement
    {
        protected override Size MeasureOverride(Size availableSize)
            => new(MapConstants.MapPixels, MapConstants.MapPixels);

        protected override void OnRender(DrawingContext dc)
        {
            // 1px black lines; freeze for perf
            var pen = new Pen(Brushes.Black, 1.0);
            pen.Freeze();

            // For crisp 1px lines, offset by 0.5 on the axis perpendicular to the line
            double w = MapConstants.MapPixels;
            double h = MapConstants.MapPixels;

            // Vertical lines every 64 px
            for (int x = 0; x <= MapConstants.MapPixels; x += MapConstants.TileSize)
            {
                double gx = x + 0.5;
                dc.DrawLine(pen, new Point(gx, 0), new Point(gx, h));
            }

            // Horizontal lines every 64 px
            for (int y = 0; y <= MapConstants.MapPixels; y += MapConstants.TileSize)
            {
                double gy = y + 0.5;
                dc.DrawLine(pen, new Point(0, gy), new Point(w, gy));
            }
        }
    }
}
