using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.Services;
using UrbanChaosMapEditor.Services.Textures;
using UrbanChaosMapEditor.ViewModels;
using static UrbanChaosMapEditor.Services.TexturesAccessor;

namespace UrbanChaosMapEditor.Views.MapOverlays
{
    public sealed class GhostTexturePreviewLayer : FrameworkElement
    {
        private MapViewModel? _vm;

        // current hover tile (null when not hovering/invalid)
        private int? _hoverTx;
        private int? _hoverTy;

        public GhostTexturePreviewLayer()
        {
            Width = MapConstants.MapPixels;
            Height = MapConstants.MapPixels;
            IsHitTestVisible = false;

            // repaint when textures change globally
            TextureCacheService.Instance.Completed += (_, __) => Dispatcher.Invoke(InvalidateVisual);
            TexturesChangeBus.Instance.Changed += (_, __) => Dispatcher.Invoke(InvalidateVisual);

            DataContextChanged += (_, __) => HookVm();
        }

        private void HookVm()
        {
            if (_vm is not null)
                _vm.PropertyChanged -= OnVmChanged;

            _vm = DataContext as MapViewModel;

            if (_vm is not null)
                _vm.PropertyChanged += OnVmChanged;

            InvalidateVisual();
        }

        private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MapViewModel.SelectedTool) ||
                e.PropertyName == nameof(MapViewModel.SelectedTextureGroup) ||
                e.PropertyName == nameof(MapViewModel.SelectedTextureNumber) ||
                e.PropertyName == nameof(MapViewModel.SelectedRotationIndex) ||
                e.PropertyName == nameof(MapViewModel.TextureWorld) ||
                e.PropertyName == nameof(MapViewModel.UseBetaTextures) ||
                e.PropertyName == nameof(MapViewModel.BrushSize))   // 🆕
            {
                Dispatcher.Invoke(InvalidateVisual);
            }
        }

        private (int tx, int ty)? _hoverTile;   // origin tile of the N×N brush
        public void SetHoverTile(int? tx, int? ty)
        {
            _hoverTile = (tx.HasValue && ty.HasValue) ? (tx.Value, ty.Value) : ((int, int)?)null;
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
            => new(MapConstants.MapPixels, MapConstants.MapPixels);

        protected override void OnRender(DrawingContext dc)
        {
            if (_vm is null || _vm.SelectedTool != EditorTool.PaintTexture) return;
            if (_hoverTile is null) return;
            if (_vm.SelectedTextureNumber <= 0) return;

            var cache = TextureCacheService.Instance;

            string relKey = _vm.SelectedTextureGroup switch
            {
                TextureGroup.World => $"world{_vm.TextureWorld}_{_vm.SelectedTextureNumber:000}",
                TextureGroup.Shared => $"shared_{_vm.SelectedTextureNumber:000}",
                TextureGroup.Prims => $"shared_prims_{_vm.SelectedTextureNumber:000}",
                _ => ""
            };
            if (string.IsNullOrEmpty(relKey)) return;

            if (!cache.TryGetRelative(relKey, out var bmp) || bmp is null) return;

            int size = Math.Max(1, _vm.BrushSize);
            int rotIndex = ((_vm.SelectedRotationIndex % 4) + 4) % 4;
            double angle = rotIndex switch { 0 => 180, 1 => 90, 2 => 0, 3 => 270, _ => 0 };

            dc.PushOpacity(0.55);
            for (int dy = 0; dy < size; dy++)
                for (int dx = 0; dx < size; dx++)
                {
                    int tx = _hoverTile.Value.tx + dx;
                    int ty = _hoverTile.Value.ty + dy;
                    if (tx < 0 || tx >= MapConstants.TilesPerSide ||
                        ty < 0 || ty >= MapConstants.TilesPerSide) continue;

                    double x = tx * MapConstants.TileSize;
                    double y = ty * MapConstants.TileSize;
                    var rect = new Rect(x, y, MapConstants.TileSize, MapConstants.TileSize);

                    if (angle == 0)
                    {
                        dc.DrawImage(bmp, rect);
                    }
                    else
                    {
                        dc.PushTransform(new RotateTransform(angle, x + rect.Width / 2.0, y + rect.Height / 2.0));
                        dc.DrawImage(bmp, rect);
                        dc.Pop();
                    }
                }
            dc.Pop();
        }
    }
}
