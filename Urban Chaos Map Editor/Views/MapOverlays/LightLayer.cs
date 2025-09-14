using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.Services;
using UrbanChaosMapEditor.ViewModels;

namespace UrbanChaosMapEditor.Views.MapOverlays
{
    public sealed class LightLayer : Canvas
    {
        private readonly LightsAccessor _acc = new LightsAccessor(LightsDataService.Instance);

        // Drag state
        private bool _mouseDown;
        private Point _mouseDownPos;
        private int _pressedIndex = -1;
        private Shape? _pressedShape;
        private bool _isDragging;
        private int _dragIndex = -1;
        private Shape? _dragShape;
        private double _dragRadius; // half of size in UI px
        private MapViewModel? _vm;

        private const double DragThreshold = 4.0; // px (same idea as PrimsLayer)

        public LightLayer()
        {
            Width = 8192;
            Height = 8192;

            // IMPORTANT:
            // null background => the canvas itself is NOT a hit target.
            // Only child shapes (the lights) are hit. Empty clicks fall through to layers below.
            Background = null;

            IsHitTestVisible = true;
            Panel.SetZIndex(this, 6);

            // LightLayer constructor
            Loaded += (_, __) =>
            {
                Redraw();
                LightsDataService.Instance.LightsBytesReset += OnLightsBytesReset;
            };
            Unloaded += (_, __) =>
            {
                LightsDataService.Instance.LightsBytesReset -= OnLightsBytesReset;
            };

            // IMPORTANT: receive preview events even if handled upstream
            AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnMouseLeftDown), true);
            AddHandler(UIElement.PreviewMouseMoveEvent, new MouseEventHandler(OnMouseMove), true);
            AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnMouseLeftUp), true);

            // track VM for selection + redraw when it changes
            DataContextChanged += (_, __) => HookVm();
        }

        private void OnLightsBytesReset(object? sender, EventArgs e) => Dispatcher.Invoke(Redraw);

        private void Redraw()
        {
            Children.Clear();

            var svc = LightsDataService.Instance;
            var buf = svc.GetBytesCopy();

            if (!svc.IsLoaded || buf.Length < LightsAccessor.TotalSize)
                return;

            try
            {
                var entries = _acc.ReadAllEntries();
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (e.Used != 1) continue;

                    int uiX = LightsAccessor.WorldXToUiX(e.X);
                    int uiZ = LightsAccessor.WorldZToUiZ(e.Z);

                    double size = 64.0 * (e.Range / 255.0);
                    if (size < 8) size = 8;

                    var fill = new SolidColorBrush(Color.FromArgb(
                        128,
                        unchecked((byte)(e.Red + 128)),
                        unchecked((byte)(e.Green + 128)),
                        unchecked((byte)(e.Blue + 128))));

                    var ellipse = new Ellipse
                    {
                        Width = size,
                        Height = size,
                        Fill = fill,
                        Stroke = Brushes.Black,
                        StrokeThickness = 1.0,
                        Tag = i
                    };

                    SetLeft(ellipse, uiX - size / 2);
                    SetTop(ellipse, uiZ - size / 2);
                    Children.Add(ellipse);

                    // Selected ring (yellow, slightly larger), not hit-testable
                    if (_vm != null && _vm.SelectedLightIndex == i)
                    {
                        double ringSize = size + 8;
                        var ring = new Ellipse
                        {
                            Width = ringSize,
                            Height = ringSize,
                            Stroke = Brushes.Yellow,
                            StrokeThickness = 2.0,
                            Fill = Brushes.Transparent,
                            IsHitTestVisible = false
                        };
                        SetLeft(ring, uiX - ringSize / 2);
                        SetTop(ring, uiZ - ringSize / 2);
                        Children.Add(ring);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LightLayer] Redraw skipped: {ex.Message}");
            }
        }

        private static Shape? FindAncestorShape(DependencyObject? d)
        {
            while (d != null)
            {
                if (d is Shape s) return s;
                d = VisualTreeHelper.GetParent(d);
            }
            return null;
        }

        private void OnMouseLeftDown(object? sender, MouseButtonEventArgs e)
        {
            // only if we clicked a light shape
            var shape = FindAncestorShape(e.OriginalSource as DependencyObject);
            if (shape == null || shape.Tag is not int idx) return;

            // NEW: double-click -> open Edit Light (reuse the existing button handler)
            if (e.ClickCount == 2)
            {
                if (_vm != null)
                    _vm.SelectedLightIndex = idx;   // ensure it's the selected one

                // Trigger the same code path as your Edit Light button
                if (Application.Current?.MainWindow is MainWindow mw)
                {
                    if (mw.FindName("BtnEditLight") is Button btn)
                        btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }

                e.Handled = true;
                return; // IMPORTANT: don’t start drag logic
            }

            // (existing single-click/drag logic below)
            _mouseDown = true;
            _isDragging = false;
            _mouseDownPos = e.GetPosition(this);
            _pressedIndex = idx;
            _pressedShape = shape;

            var w = _pressedShape.RenderSize.Width;
            _dragRadius = (w > 0 ? w : _pressedShape.Width) / 2.0;

            CaptureMouse();
            e.Handled = true; // don’t let other layers steal this click
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            if (!_mouseDown || _pressedShape == null || e.LeftButton != MouseButtonState.Pressed)
                return;

            var pos = e.GetPosition(this);

            // if not yet dragging, check threshold
            if (!_isDragging)
            {
                var d = pos - _mouseDownPos;
                if (Math.Abs(d.X) <= DragThreshold && Math.Abs(d.Y) <= DragThreshold)
                    return;

                // start dragging now
                _isDragging = true;
                _dragIndex = _pressedIndex;
                _dragShape = _pressedShape;
            }

            // live drag
            double left = pos.X - _dragRadius;
            double top = pos.Y - _dragRadius;
            left = Math.Max(0, Math.Min(left, ActualWidth - _dragRadius * 2));
            top = Math.Max(0, Math.Min(top, ActualHeight - _dragRadius * 2));

            SetLeft(_dragShape!, left);
            SetTop(_dragShape!, top);
            e.Handled = true;
        }

        private void OnMouseLeftUp(object? sender, MouseButtonEventArgs e)
        {
            if (!_mouseDown) return;

            try
            {
                if (_isDragging && _dragShape != null && _dragIndex >= 0)
                {
                    // commit move
                    Point p = e.GetPosition(this);
                    int uiX = (int)Math.Round(p.X);
                    int uiZ = (int)Math.Round(p.Y);
                    int worldX = LightsAccessor.UiXToWorldX(uiX);
                    int worldZ = LightsAccessor.UiZToWorldZ(uiZ);

                    var entry = _acc.ReadEntry(_dragIndex);
                    entry.X = worldX;
                    entry.Z = worldZ;
                    _acc.WriteEntry(_dragIndex, entry); // this will trigger BytesReset → Redraw()
                }
                else
                {
                    // pure click → select
                    if (_vm != null && _pressedIndex >= 0)
                    {
                        _vm.SelectedLightIndex = _pressedIndex;
                        Redraw();
                    }
                }
            }
            finally
            {
                _mouseDown = false;
                _isDragging = false;
                _pressedIndex = -1;
                _pressedShape = null;
                _dragIndex = -1;
                _dragShape = null;
                if (IsMouseCaptured) ReleaseMouseCapture();
            }

            e.Handled = true;
        }
        private void HookVm()
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as MapViewModel;
            if (_vm != null) _vm.PropertyChanged += OnVmChanged;
            Redraw();
        }

        private void OnVmChanged(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MapViewModel.SelectedLightIndex) ||
                e.PropertyName == nameof(MapViewModel.ShowLights))
            {
                Dispatcher.Invoke(Redraw);
            }
        }
    }
}
