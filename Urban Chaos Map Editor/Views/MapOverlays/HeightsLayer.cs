using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.Services;

namespace UrbanChaosMapEditor.Views.Overlays
{
    /// <summary>
    /// Draws a small white circle with black height text centered on the
    /// BOTTOM-RIGHT VERTEX of each 64x64 tile (i.e., at (tx+1, ty+1)*64).
    /// Edge circles will overflow the control bounds by design.
    /// </summary>
    public sealed class HeightsLayer : FrameworkElement
    {
        private readonly HeightsAccessor _accessor = new HeightsAccessor(MapDataService.Instance);
        // NEW: simple debounce to coalesce many tile signals
        private readonly DispatcherTimer _debounceTimer;

        // Cache formatted text per height value (-127..127)
        private readonly Dictionary<int, FormattedText> _textCache = new();

        // Styling
        private const double Radius = 14.0;   // subtle
        private static readonly Brush WhiteBrush;
        private static readonly Pen BlackPen;

        static HeightsLayer()
        {
            WhiteBrush = Brushes.White;
            WhiteBrush.Freeze();

            BlackPen = new Pen(Brushes.Black, 1.0);
            BlackPen.Freeze();
        }

        public HeightsLayer()
        {
            MapDataService.Instance.MapLoaded += (_, __) => Dispatcher.Invoke(InvalidateVisual);
            MapDataService.Instance.MapCleared += (_, __) => Dispatcher.Invoke(InvalidateVisual);
            MapDataService.Instance.MapSaved += (_, __) => Dispatcher.Invoke(InvalidateVisual);
            MapDataService.Instance.MapBytesReset += (_, __) => Dispatcher.Invoke(InvalidateVisual);

            // NEW: repaint on height edits
            HeightsChangeBus.Instance.TileChanged += (_, __) => KickRepaint();
            HeightsChangeBus.Instance.RegionChanged += (_, __) => KickRepaint();


            Width = MapConstants.MapPixels;   // 8192
            Height = MapConstants.MapPixels;  // 8192
            IsHitTestVisible = false;

            _debounceTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS
            };
            _debounceTimer.Tick += (_, __) =>
            {
                _debounceTimer.Stop();
                InvalidateVisual();
            };
        }

        protected override Size MeasureOverride(Size availableSize)
            => new(MapConstants.MapPixels, MapConstants.MapPixels);

        protected override void OnRender(DrawingContext dc)
        {
            if (!MapDataService.Instance.IsLoaded || MapDataService.Instance.MapBytes is null)
                return;

            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            double tile = MapConstants.TileSize; // 64

            for (int tx = 0; tx < MapConstants.TilesPerSide; tx++)
            {
                for (int ty = 0; ty < MapConstants.TilesPerSide; ty++)
                {
                    // Bottom-right vertex of this tile
                    double cx = (tx + 1) * tile;   // x + 64
                    double cy = (ty + 1) * tile;   // y + 64

                    int h = _accessor.ReadHeight(tx, ty);

                    // Circle centered exactly on the vertex (may overflow control edges)
                    dc.DrawEllipse(WhiteBrush, BlackPen, new Point(cx, cy), Radius, Radius);

                    // Text (cache per height)
                    if (!_textCache.TryGetValue(h, out var ft))
                    {
                        ft = new FormattedText(
                            h.ToString(CultureInfo.InvariantCulture),
                            CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            new Typeface("Segoe UI"),
                            12,
                            Brushes.Black,
                            pixelsPerDip);
                        _textCache[h] = ft;
                    }

                    // Center text in the circle
                    dc.DrawText(ft, new Point(cx - ft.Width / 2.0, cy - ft.Height / 2.0));
                }
            }
        }
        private void KickRepaint()
        {
            // coalesce multiple events into one redraw next frame
            if (!_debounceTimer.IsEnabled) _debounceTimer.Start();
        }
    }
}
