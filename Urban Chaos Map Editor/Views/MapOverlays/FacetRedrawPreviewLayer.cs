// /Views/MapOverlays/FacetRedrawPreviewLayer.cs
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.ViewModels;

namespace UrbanChaosMapEditor.Views.MapOverlays
{
    /// <summary>
    /// Renders the preview line during facet redraw mode or multi-draw mode.
    /// Shows a dashed yellow line from the first click point to the current mouse position.
    /// Also renders small circles at the snap points.
    /// </summary>
    public sealed class FacetRedrawPreviewLayer : FrameworkElement
    {
        private MapViewModel? _vm;

        private static readonly Pen PreviewPen;
        private static readonly Pen MultiDrawPen;
        private static readonly Pen DotPen;
        private static readonly Brush DotFill;
        private static readonly Brush MultiDrawDotFill;

        static FacetRedrawPreviewLayer()
        {
            // Dashed yellow line for single redraw preview
            PreviewPen = new Pen(Brushes.Yellow, 4.0)
            {
                DashStyle = new DashStyle(new double[] { 6, 4 }, 0),
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            PreviewPen.Freeze();

            // Cyan line for multi-draw (to distinguish from single redraw)
            MultiDrawPen = new Pen(Brushes.Cyan, 4.0)
            {
                DashStyle = new DashStyle(new double[] { 6, 4 }, 0),
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            MultiDrawPen.Freeze();

            // Dots at endpoints
            DotFill = new SolidColorBrush(Color.FromRgb(255, 255, 100));
            ((SolidColorBrush)DotFill).Freeze();

            MultiDrawDotFill = new SolidColorBrush(Color.FromRgb(100, 255, 255));
            ((SolidColorBrush)MultiDrawDotFill).Freeze();

            DotPen = new Pen(Brushes.Black, 2.0);
            DotPen.Freeze();
        }

        public FacetRedrawPreviewLayer()
        {
            Width = MapConstants.MapPixels;
            Height = MapConstants.MapPixels;
            IsHitTestVisible = false;

            DataContextChanged += (_, __) => HookVm();
        }

        private void HookVm()
        {
            if (_vm != null)
                _vm.PropertyChanged -= OnVmChanged;

            _vm = DataContext as MapViewModel;

            if (_vm != null)
                _vm.PropertyChanged += OnVmChanged;

            InvalidateVisual();
        }

        private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MapViewModel.FacetRedrawPreviewLine) ||
                e.PropertyName == nameof(MapViewModel.IsRedrawingFacet) ||
                e.PropertyName == nameof(MapViewModel.MultiDrawPreviewLine) ||
                e.PropertyName == nameof(MapViewModel.IsMultiDrawingFacets))
            {
                Dispatcher.Invoke(InvalidateVisual);
            }
        }

        protected override Size MeasureOverride(Size availableSize)
            => new(MapConstants.MapPixels, MapConstants.MapPixels);

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            if (_vm == null)
                return;

            // Single facet redraw mode
            if (_vm.IsRedrawingFacet && _vm.FacetRedrawPreviewLine != null)
            {
                var line = _vm.FacetRedrawPreviewLine.Value;
                DrawPreviewLine(dc, line.uiX0, line.uiZ0, line.uiX1, line.uiZ1,
                               PreviewPen, DotFill, "Start (X0,Z0)", "End (X1,Z1)");
            }

            // Multi-draw mode
            if (_vm.IsMultiDrawingFacets && _vm.MultiDrawPreviewLine != null)
            {
                var line = _vm.MultiDrawPreviewLine.Value;
                DrawPreviewLine(dc, line.uiX0, line.uiZ0, line.uiX1, line.uiZ1,
                               MultiDrawPen, MultiDrawDotFill, "Start", "End");
            }
        }

        private void DrawPreviewLine(DrawingContext dc, int x0, int z0, int x1, int z1,
                                     Pen linePen, Brush dotBrush, string startLabel, string endLabel)
        {
            var p1 = new Point(x0, z0);
            var p2 = new Point(x1, z1);

            // Draw the preview line
            dc.DrawLine(linePen, p1, p2);

            // Draw dots at both endpoints
            const double dotRadius = 8.0;
            dc.DrawEllipse(dotBrush, DotPen, p1, dotRadius, dotRadius);
            dc.DrawEllipse(dotBrush, DotPen, p2, dotRadius, dotRadius);

            // Draw labels
            var startText = new FormattedText(
                startLabel,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                12,
                Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(startText, new Point(p1.X + 12, p1.Y - 8));

            // If line has length, draw end label
            if (p1 != p2)
            {
                var endText = new FormattedText(
                    endLabel,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    12,
                    Brushes.White,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);

                dc.DrawText(endText, new Point(p2.X + 12, p2.Y - 8));
            }
        }
    }
}