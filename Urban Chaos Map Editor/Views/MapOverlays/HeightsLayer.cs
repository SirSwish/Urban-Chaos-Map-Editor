using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.Services;
using UrbanChaosMapEditor.ViewModels;

namespace UrbanChaosMapEditor.Views.MapOverlays
{
    /// <summary>
    /// Draws a small white circle with black height text centered on the
    /// BOTTOM-RIGHT VERTEX of each 64x64 tile (i.e., at (tx+1, ty+1)*64).
    /// Adds brush-based hover highlight (N×N vertices) when a height tool is active.
    /// </summary>
    public sealed class HeightsLayer : FrameworkElement
    {
        private readonly HeightsAccessor _accessor = new HeightsAccessor(MapDataService.Instance);
        private readonly DispatcherTimer _debounceTimer;

        // Caches for text (per height value), separated by color to avoid brush mutation
        private readonly Dictionary<int, FormattedText> _textCacheBlack = new();
        private readonly Dictionary<int, FormattedText> _textCacheWhite = new();
        private double _lastPixelsPerDip = -1.0;

        // Styling
        private const double Radius = 14.0;
        private const double HitRadius = 12.0; // how close cursor must be to a vertex to count as hover

        private static readonly Brush DefaultFill;
        private static readonly Pen DefaultOutline;
        private static readonly Brush DefaultText;

        private static readonly Brush HoverFill;
        private static readonly Pen HoverOutline;
        private static readonly Brush HoverText;

        // For cursor + brush + tool
        private MapViewModel? _vm;

        static HeightsLayer()
        {
            // Defaults: white fill, black outline & text
            DefaultFill = Brushes.White;
            DefaultFill.Freeze();

            DefaultOutline = new Pen(Brushes.Black, 1.0);
            DefaultOutline.Freeze();

            DefaultText = Brushes.Black;
            DefaultText.Freeze();

            // Hover: red fill, white outline & text
            var red = new SolidColorBrush(Color.FromRgb(0xE5, 0x00, 0x00));
            red.Freeze();
            HoverFill = red;

            HoverOutline = new Pen(Brushes.White, 1.25);
            HoverOutline.Freeze();

            HoverText = Brushes.White;
            HoverText.Freeze();
        }

        public HeightsLayer()
        {
            // Redraw on map lifecycle
            MapDataService.Instance.MapLoaded += (_, __) => Dispatcher.Invoke(InvalidateVisual);
            MapDataService.Instance.MapCleared += (_, __) => Dispatcher.Invoke(InvalidateVisual);
            MapDataService.Instance.MapSaved += (_, __) => Dispatcher.Invoke(InvalidateVisual);
            MapDataService.Instance.MapBytesReset += (_, __) => Dispatcher.Invoke(InvalidateVisual);

            // Redraw on height edits (debounced)
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

            DataContextChanged += (_, __) => HookVm();
        }

        private void HookVm()
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as MapViewModel;
            if (_vm != null) _vm.PropertyChanged += OnVmChanged;
            InvalidateVisual();
        }

        private void OnVmChanged(object? s, PropertyChangedEventArgs e)
        {
            // Cursor, tool, or brush size changes → repaint
            if (e.PropertyName == nameof(MapViewModel.CursorX) ||
                e.PropertyName == nameof(MapViewModel.CursorZ) ||
                e.PropertyName == nameof(MapViewModel.SelectedTool) ||
                e.PropertyName == nameof(MapViewModel.BrushSize))
            {
                InvalidateVisual();
            }
        }

        protected override Size MeasureOverride(Size availableSize)
            => new(MapConstants.MapPixels, MapConstants.MapPixels);

        protected override void OnRender(DrawingContext dc)
        {
            if (!MapDataService.Instance.IsLoaded || MapDataService.Instance.MapBytes is null)
                return;

            // Recreate text caches if DPI changed
            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (Math.Abs(ppd - _lastPixelsPerDip) > 0.01)
            {
                _textCacheBlack.Clear();
                _textCacheWhite.Clear();
                _lastPixelsPerDip = ppd;
            }

            // Determine hovered center vertex (if a height tool is active)
            var (centerVX, centerVZ) = GetHoveredVertex();
            var brushBounds = GetBrushBounds(centerVX, centerVZ);

            double tile = MapConstants.TileSize; // 64

            for (int tx = 0; tx < MapConstants.TilesPerSide; tx++)
            {
                for (int ty = 0; ty < MapConstants.TilesPerSide; ty++)
                {
                    // Bottom-right vertex of this tile (1..128)
                    int vtx = tx + 1;
                    int vty = ty + 1;
                    double cx = vtx * tile;
                    double cy = vty * tile;

                    int h = _accessor.ReadHeight(tx, ty);

                    bool inBrush = brushBounds.Contains(vtx, vty);

                    // Circle
                    var fill = inBrush ? HoverFill : DefaultFill;
                    var stroke = inBrush ? HoverOutline : DefaultOutline;
                    dc.DrawEllipse(fill, stroke, new Point(cx, cy), Radius, Radius);

                    // Text (use cached FormattedText per color)
                    var textBrush = inBrush ? HoverText : DefaultText;
                    var ft = GetFormattedText(h, textBrush, ppd);

                    // Center text in the circle
                    dc.DrawText(ft, new Point(cx - ft.Width / 2.0, cy - ft.Height / 2.0));
                }
            }
        }

        private FormattedText GetFormattedText(int height, Brush brush, double ppd)
        {
            var cache = ReferenceEquals(brush, HoverText) ? _textCacheWhite : _textCacheBlack;

            if (!cache.TryGetValue(height, out var ft))
            {
                ft = new FormattedText(
                    height.ToString(CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    12,
                    brush,
                    ppd);
                cache[height] = ft;
            }
            return ft;
        }

        private static bool IsHeightTool(EditorTool t) =>
            t == EditorTool.RaiseHeight ||
            t == EditorTool.LowerHeight ||
            t == EditorTool.LevelHeight ||
            t == EditorTool.FlattenHeight ||
            t == EditorTool.DitchTemplate;

        /// <summary>
        /// Returns the hovered vertex indices (vx, vz) in grid coords (0..128), or (-1,-1) when no hover.
        /// Uses MapViewModel.CursorX/Z (game space) converted to UI space.
        /// </summary>
        private (int vx, int vz) GetHoveredVertex()
        {
            if (_vm == null || !IsHeightTool(_vm.SelectedTool))
                return (-1, -1);

            // Convert game→UI (origin flip like other overlays)
            double uiX = MapConstants.MapPixels - _vm.CursorX;
            double uiZ = MapConstants.MapPixels - _vm.CursorZ;

            // nearest vertex on the 64px grid
            int vx = (int)Math.Round(uiX / MapConstants.TileSize);
            int vz = (int)Math.Round(uiZ / MapConstants.TileSize);

            // Only count as hovered if within HitRadius of that vertex (and within the grid bounds)
            if (vx < 0 || vx > MapConstants.TilesPerSide ||
                vz < 0 || vz > MapConstants.TilesPerSide)
            {
                return (-1, -1);
            }

            double cx = vx * MapConstants.TileSize;
            double cz = vz * MapConstants.TileSize;
            double dx = uiX - cx;
            double dz = uiZ - cz;
            double dist = Math.Sqrt(dx * dx + dz * dz);

            return dist <= HitRadius ? (vx, vz) : (-1, -1);
        }

        /// <summary>
        /// Computes the inclusive rectangle of vertices (in 1..128 for both axes) that should highlight
        /// based on BrushSize centered at the hovered vertex (odd sizes) or spanning correctly (even sizes).
        /// If no hover, returns an empty bounds.
        /// </summary>
        private VertexBounds GetBrushBounds(int centerVX, int centerVZ)
        {
            // We draw circles only for vertices (tx+1,ty+1) ∈ [1..128], so clamp to that range.
            const int MIN_V = 1;
            const int MAX_V = MapConstants.TilesPerSide; // 128

            if (centerVX < 0 || centerVZ < 0)
                return VertexBounds.Empty;

            // Brush size N → we want N vertices across.
            // Use asymmetric half extents so even sizes produce exactly N cells:
            // N=3 -> left=1 right=1  => -1..+1 (3)
            // N=2 -> left=0 right=1  =>  0..+1 (2)
            int n = Math.Max(1, _vm?.BrushSize ?? 1);
            int left = (n - 1) / 2;
            int right = n / 2;

            int minX = Math.Max(MIN_V, centerVX - left);
            int maxX = Math.Min(MAX_V, centerVX + right);
            int minZ = Math.Max(MIN_V, centerVZ - left);
            int maxZ = Math.Min(MAX_V, centerVZ + right);

            if (minX > maxX || minZ > maxZ)
                return VertexBounds.Empty;

            return new VertexBounds(minX, maxX, minZ, maxZ);
        }

        private void KickRepaint()
        {
            if (!_debounceTimer.IsEnabled) _debounceTimer.Start();
        }

        private readonly struct VertexBounds
        {
            public readonly int MinX, MaxX, MinZ, MaxZ;
            public static VertexBounds Empty => new VertexBounds(1, 0, 1, 0); // empty (min>max)

            public VertexBounds(int minX, int maxX, int minZ, int maxZ)
            {
                MinX = minX; MaxX = maxX; MinZ = minZ; MaxZ = maxZ;
            }

            public bool Contains(int vx, int vz) =>
                vx >= MinX && vx <= MaxX && vz >= MinZ && vz <= MaxZ;
        }
    }
}
