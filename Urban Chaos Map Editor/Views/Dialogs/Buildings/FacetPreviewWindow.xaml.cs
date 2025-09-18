using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using System.Windows.Shapes;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.Models.Styles; // TMAFile
using UrbanChaosMapEditor.Services;
using UrbanChaosMapEditor.Services.DataServices;

// --- resolve common ambiguities cleanly ---
using IOPath = System.IO.Path;
using StyleTextureEntry = UrbanChaosMapEditor.Models.Styles.TextureEntry;

namespace UrbanChaosMapEditor.Views.Dialogs.Buildings
{
    public partial class FacetPreviewWindow : Window
    {
        private const int PanelPx = 64;
      

        private readonly DFacetRec _facet;

        // Snapshot tables we need to resolve styles
        private short[] _dstyles = Array.Empty<short>();
        private BuildingArrays.DStoreyRec[] _storeys = Array.Empty<BuildingArrays.DStoreyRec>();
        private byte[] _paintMem = Array.Empty<byte>();

        // Texture location hints
        private readonly string _variant;      // "Release" or "Beta"
        private readonly int _worldNumber;
        private readonly string? _texturesRoot;



        public FacetPreviewWindow(DFacetRec facet)
        {
            InitializeComponent();
            _facet = facet;

            // Get both values directly from the shell's Map object. No defaults.
            if (!TryResolveVariantAndWorld(out _variant, out _worldNumber))
            {
                _variant = null;
                _worldNumber = 0;
                System.Diagnostics.Debug.WriteLine("[FacetPreview] FATAL: Could not resolve Map.UseBetaTextures/Map.TextureWorld from shell Map. No defaults will be used.");
            }

            Loaded += async (_, __) => await BuildUIAsync();
        }

        // ---------- Small VMs for paint bytes grid ----------
        private sealed class PaintByteVM
        {
            public int Index { get; init; }
            public string ByteHex { get; init; } = "";
            public int Page { get; init; }
            public string Flag { get; init; } = "";
        }

        // ---------- Entry point ----------
        private async Task BuildUIAsync()
        {
            // Meta
            FacetIdText.Text = $"Facet (file-order id not shown)";
            FacetTypeText.Text = $"Type: {_facet.Type}";
            FacetCoordsText.Text = $"Coords: ({_facet.X0},{_facet.Z0}) → ({_facet.X1},{_facet.Z1})";
            FacetHeightText.Text = $"Height: coarse={_facet.Height} fine={_facet.FHeight}";
            FacetFlagsText.Text = $"Flags: 0x{((ushort)_facet.Flags):X4}   Building={_facet.Building} Storey={_facet.Storey} StyleIndex={_facet.StyleIndex}";

            // Grab building block snapshot (for dstyles / paint mem / storeys)
            var arrays = new BuildingsAccessor(MapDataService.Instance).ReadSnapshot();
            _dstyles = arrays.Styles ?? Array.Empty<short>();
            _paintMem = arrays.PaintMem ?? Array.Empty<byte>();
            _storeys = arrays.Storeys ?? Array.Empty<BuildingArrays.DStoreyRec>();

            Debug.WriteLine($"[FacetPreview] region 0x{arrays.StartOffset:X} len=0x{arrays.Length:X} dstyles={(_dstyles?.Length ?? 0)} paintMem={(_paintMem?.Length ?? 0)} storeys={(_storeys?.Length ?? 0)}");

            // Ensure TMA is loaded
            await EnsureStylesLoadedAsync();

            // Style resolve summary (RAW vs PAINTED) + recipe text
            SummarizeStyleAndRecipe(_facet);

            // Draw exact-pixel preview
            DrawPreview(_facet);
        }

        // ---------- Summary / recipe ----------
        private void SummarizeStyleAndRecipe(DFacetRec f)
        {
            if (_dstyles.Length == 0 || f.StyleIndex >= _dstyles.Length)
            {
                StyleModeText.Text = "Mode: (unknown - missing dstyles)";
                RawStyleText.Text = $"StyleIndex={f.StyleIndex}";
                RecipeText.Text = "";
                RawStylePanel.Visibility = Visibility.Visible;
                PaintedPanel.Visibility = Visibility.Collapsed;
                return;
            }

            short val = _dstyles[f.StyleIndex];
            if (val >= 0)
            {
                // RAW
                StyleModeText.Text = "Mode: RAW style";
                RawStyleText.Text = $"Raw style id (style.tma): {val}";
                RecipeText.Text = BuildRawRecipeString(val);
                RawStylePanel.Visibility = Visibility.Visible;
                PaintedPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                // PAINTED: negative value means DStorey id (1-based)
                int sid = -val;
                StyleModeText.Text = $"Mode: PAINTED (DStorey {sid})";

                if (sid < 1 || sid > _storeys.Length)
                {
                    PaintedSummaryText.Text = $"Invalid DStorey id: {sid}";
                    PaintBytesHexText.Text = "(none)";
                    PaintBytesGrid.ItemsSource = null;
                    RecipeText.Text = "";
                    RawStylePanel.Visibility = Visibility.Collapsed;
                    PaintedPanel.Visibility = Visibility.Visible;
                    return;
                }

                var ds = _storeys[sid - 1];
                PaintedSummaryText.Text = $"Base style: {ds.StyleIndex}   PaintMem: Index={ds.PaintIndex} Count={ds.Count}";

                // Base 5-slot recipe from style.tma
                RecipeText.Text = BuildRawRecipeString(ds.StyleIndex);

                // Slice paint bytes
                var bytes = GetPaintBytes(ds);
                PaintBytesHexText.Text = ToHexLine(bytes);

                var rows = new ObservableCollection<PaintByteVM>();
                for (int i = 0; i < bytes.Length; i++)
                {
                    byte b = bytes[i];
                    rows.Add(new PaintByteVM
                    {
                        Index = i,
                        ByteHex = b.ToString("X2"),
                        Page = b & 0x7F,
                        Flag = (b & 0x80) != 0 ? "1" : "0"
                    });
                }
                PaintBytesGrid.ItemsSource = rows;

                RawStylePanel.Visibility = Visibility.Collapsed;
                PaintedPanel.Visibility = Visibility.Visible;
            }
        }

        private string BuildRawRecipeString(int styleId)
        {
            var tma = StyleDataService.Instance.TmaSnapshot;
            if (tma == null || styleId < 0 || styleId >= tma.TextureStyles.Count)
                return "(style not in TMA)";

            var entries = tma.TextureStyles[styleId].Entries; // expect 5
            var sb = new StringBuilder();
            sb.Append($"Style #{styleId}: ");
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (i > 0) sb.Append(" | ");
                sb.Append($"[{i}] P{e.Page} Tx{e.Tx} Ty{e.Ty} F{e.Flip}");
            }
            return sb.ToString();
        }

        private static string ToHexLine(byte[] data)
        {
            if (data == null || data.Length == 0) return "(empty)";
            var sb = new StringBuilder(data.Length * 3);
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        private byte[] GetPaintBytes(in BuildingArrays.DStoreyRec ds)
        {
            if (_paintMem.Length == 0 || ds.Count == 0) return Array.Empty<byte>();
            int start = ds.PaintIndex;
            int count = ds.Count;
            if (start < 0 || start + count > _paintMem.Length) return Array.Empty<byte>();
            var bytes = new byte[count];
            Buffer.BlockCopy(_paintMem, start, bytes, 0, count);
            return bytes;
        }

        // ---------- Preview drawing ----------
        private void DrawPreview(DFacetRec f)
        {
            int dx = Math.Abs(f.X1 - f.X0);
            int dz = Math.Abs(f.Z1 - f.Z0);
            int panelsAcross = Math.Max(dx, dz);                    // tiles along the wall
            if (panelsAcross <= 0) panelsAcross = Math.Max(1, (int)f.Height);

            int panelsDown = 1;
            if (f.Type == FacetType.Fence || f.Type == FacetType.FenceFlat ||
                f.Type == FacetType.FenceBrick || f.Type == FacetType.Ladder ||
                f.Type == FacetType.Trench)
            {
                panelsDown = Math.Max(1, (int)f.Height);            // these use Height≈panels vertically
            }

            int width = Math.Max(1, panelsAcross) * PanelPx;
            int height = Math.Max(1, panelsDown) * PanelPx;

            PanelCanvas.Children.Clear();
            GridCanvas.Children.Clear();
            PanelCanvas.Width = width; PanelCanvas.Height = height;
            GridCanvas.Width = width; GridCanvas.Height = height;

            for (int row = 0; row < panelsDown; row++)
            {
                for (int col = 0; col < panelsAcross; col++)
                {
                    int panelIndexAlong = col;

                    if (TryResolveTileForPanel(_facet.StyleIndex, panelIndexAlong, out var texEntry, out byte? pageOverride) &&
                        TryLoadTileBitmap(pageOverride ?? texEntry.Page, texEntry.Tx, texEntry.Ty, texEntry.Flip, out var bmp))
                    {
                        var img = new System.Windows.Controls.Image
                        {
                            Width = PanelPx,
                            Height = PanelPx,
                            Source = bmp
                        };
                        System.Windows.Controls.Canvas.SetLeft(img, col * PanelPx);
                        System.Windows.Controls.Canvas.SetTop(img, row * PanelPx);
                        PanelCanvas.Children.Add(img);
                    }
                    else
                    {
                        // fallback grey
                        var rect = new Rectangle
                        {
                            Width = PanelPx,
                            Height = PanelPx,
                            Fill = new SolidColorBrush(Color.FromRgb(0x55, 0x58, 0x5E))
                        };
                        System.Windows.Controls.Canvas.SetLeft(rect, col * PanelPx);
                        System.Windows.Controls.Canvas.SetTop(rect, row * PanelPx);
                        PanelCanvas.Children.Add(rect);

                        Debug.WriteLine($"[FacetPreview] Style resolve failed for panel {panelIndexAlong} (styleIdx={_facet.StyleIndex}).");
                    }
                }
            }

            // 64×64 grid (full panels only)
            DrawGrid(GridCanvas, width, height, PanelPx, PanelPx);

            // outline
            var outline = new Rectangle
            {
                Width = width,
                Height = height,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                Fill = Brushes.Transparent
            };
            GridCanvas.Children.Add(outline);
        }

        private bool TryResolveVariantAndWorld(out string? variant, out int world)
        {
            variant = null;
            world = 0;

            try
            {
                var shell = System.Windows.Application.Current.MainWindow?.DataContext;
                if (shell == null)
                {
                    System.Diagnostics.Debug.WriteLine("[FacetPreview] TryResolveVariantAndWorld: MainWindow.DataContext is null.");
                    return false;
                }

                var shellType = shell.GetType();
                var mapProp = shellType.GetProperty("Map");
                var map = mapProp?.GetValue(shell);
                if (map == null)
                {
                    System.Diagnostics.Debug.WriteLine("[FacetPreview] TryResolveVariantAndWorld: 'Map' property not found or null on shell.");
                    return false;
                }

                var mapType = map.GetType();
                // REQUIRED: bool UseBetaTextures, int TextureWorld
                var useBetaProp = mapType.GetProperty("UseBetaTextures");
                var worldProp = mapType.GetProperty("TextureWorld");

                if (useBetaProp == null || worldProp == null)
                {
                    System.Diagnostics.Debug.WriteLine("[FacetPreview] TryResolveVariantAndWorld: Map.UseBetaTextures or Map.TextureWorld not found.");
                    return false;
                }

                if (useBetaProp.GetValue(map) is bool useBeta &&
                    worldProp.GetValue(map) is int w &&
                    w > 0)
                {
                    variant = useBeta ? "Beta" : "Release";
                    world = w;
                    System.Diagnostics.Debug.WriteLine($"[FacetPreview] TryResolveVariantAndWorld: Variant='{variant}', World={world} (from Map).");
                    return true;
                }

                System.Diagnostics.Debug.WriteLine("[FacetPreview] TryResolveVariantAndWorld: Values present but invalid (world <= 0?).");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FacetPreview] TryResolveVariantAndWorld: exception: {ex.Message}");
                return false;
            }
        }

        private static void DrawGrid(System.Windows.Controls.Canvas c, int width, int height, int stepX, int stepY)
        {
            var g = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
            for (int x = stepX; x < width; x += stepX)
            {
                var l = new Line { X1 = x, Y1 = 0, X2 = x, Y2 = height, StrokeThickness = 1, Stroke = g };
                c.Children.Add(l);
            }
            for (int y = stepY; y < height; y += stepY)
            {
                var l = new Line { X1 = 0, Y1 = y, X2 = width, Y2 = y, StrokeThickness = 1, Stroke = g };
                c.Children.Add(l);
            }
        }

        // ---------- RAW vs PAINTED resolve ----------
        /// <summary>
        /// Resolve (Page,Tx,Ty,Flip) for a given horizontal panel index.
        /// RAW:   dstyles[styleIdx] >= 0 → use that styleId’s slot (mod 5).
        /// PAINT: dstyles[styleIdx] <  0 → base style from DStorey + page override from paint_mem (if any).
        /// </summary>
        private bool TryResolveTileForPanel(ushort styleIndex, int panelIndexAlong,
                                            out StyleTextureEntry entry, out byte? pageOverride)
        {
            entry = default;
            pageOverride = null;

            if (_dstyles == null || styleIndex >= _dstyles.Length)
                return false;

            short styleVal = _dstyles[styleIndex];
            int slot = panelIndexAlong % 5; // 5 entries per style in style.tma

            if (styleVal >= 0)
            {
                int styleId = styleVal;
                if (TryGetTmaEntry(styleId, slot, out entry))
                    return true;

                Debug.WriteLine($"[FacetPreview] RAW: TryGetTmaEntry failed for styleId={styleId} slot={slot}");
                return false;
            }
            else
            {
                int dstorey1 = -styleVal; // 1-based
                int idx = dstorey1 - 1;
                if (idx < 0 || idx >= _storeys.Length)
                {
                    Debug.WriteLine($"[FacetPreview] PAINTED: bad dstorey id {dstorey1}");
                    return false;
                }

                var ds = _storeys[idx];
                if (!TryGetTmaEntry(ds.StyleIndex, slot, out entry))
                {
                    Debug.WriteLine($"[FacetPreview] PAINTED: base entry missing for baseStyle={ds.StyleIndex} slot={slot}");
                    return false;
                }

                // page override from paint_mem: clamp for short lists (repeat last)
                if (_paintMem != null && ds.Count > 0 && ds.PaintIndex >= 0 && ds.PaintIndex < _paintMem.Length)
                {
                    int clamped = Math.Min(panelIndexAlong, ds.Count - 1);
                    int byteIndex = ds.PaintIndex + clamped;
                    if (byteIndex >= 0 && byteIndex < _paintMem.Length)
                    {
                        byte b = _paintMem[byteIndex];
                        pageOverride = (byte)(b & 0x7F);
                    }
                }
                return true;
            }
        }

        // ---------- TMA + textures ----------
        private async Task EnsureStylesLoadedAsync()
        {
            // refuse to proceed if we didn't resolve Variant + World
            if (string.IsNullOrWhiteSpace(_variant) || _worldNumber <= 0)
            {
                System.Diagnostics.Debug.WriteLine("[FacetPreview] EnsureStylesLoadedAsync: Variant/World unresolved — aborting (no defaults).");
                return;
            }

            var svc = StyleDataService.Instance;
            if (svc.IsLoaded)
            {
                System.Diagnostics.Debug.WriteLine($"[FacetPreview] style.tma already loaded. Source hint: '{svc.CurrentPath ?? "(unknown)"}'.");
                return;
            }

            string packUri = $"pack://application:,,,/Assets/Textures/{_variant}/world{_worldNumber}/style.tma";
            System.Diagnostics.Debug.WriteLine($"[FacetPreview] Trying embedded style.tma: {packUri}");

            System.Windows.Resources.StreamResourceInfo? sri = null;
            try
            {
                sri = System.Windows.Application.GetResourceStream(new Uri(packUri, UriKind.Absolute));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FacetPreview] pack URI probe threw: {ex.Message}");
            }

            if (sri?.Stream != null)
            {
                System.Diagnostics.Debug.WriteLine($"[FacetPreview] FOUND embedded style.tma at {packUri}.");
                try
                {
                    await StyleDataService.Instance.LoadFromResourceStreamAsync(sri.Stream, packUri);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FacetPreview] LoadFromResourceStreamAsync failed: {ex.Message}");
                }
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[FacetPreview] NOT found at {packUri}. (No disk fallback by design.)");
        }



        private bool TryGetTmaEntry(int styleId, int slot, out StyleTextureEntry entry)
        {
            entry = default;
            var tma = StyleDataService.Instance.TmaSnapshot;
            if (tma == null) return false;
            if (styleId < 0 || styleId >= tma.TextureStyles.Count) return false;

            var style = tma.TextureStyles[styleId];
            var entries = style.Entries; // List<TextureEntry>
            if (entries == null || slot < 0 || slot >= entries.Count) return false;

            entry = entries[slot];
            return true;
        }

        private bool TryLoadTileBitmap(byte page, byte tx, byte ty, byte flip, out BitmapSource? bmp)
        {
            bmp = null;

            if (string.IsNullOrWhiteSpace(_variant) || _worldNumber <= 0)
            {
                System.Diagnostics.Debug.WriteLine($"[FacetPreview] TryLoadTileBitmap: Variant/World unresolved — cannot load tile (page={page}, tx={tx}, ty={ty}).");
                return false;
            }

            // Decide subfolder based on page, same rules as before
            string? subfolder = null;
            if (page <= 3) subfolder = $"world{_worldNumber}";
            else if (page <= 7) subfolder = "shared";
            else if (page == 8) subfolder = $"world{_worldNumber}/insides";
            else
            {
                System.Diagnostics.Debug.WriteLine($"[FacetPreview] TryLoadTileBitmap: Unsupported page {page}.");
                return false;
            }

            // Try .png first (hi + non-hi), then .bmp (hi + non-hi)
            var tried = new List<string>(4);
            foreach (var packUri in EnumerateTilePackUris(_variant!, subfolder!, page, tx, ty))
            {
                tried.Add(packUri);
                try
                {
                    var sri = Application.GetResourceStream(new Uri(packUri, UriKind.Absolute));
                    if (sri?.Stream == null) continue;

                    var baseBmp = new BitmapImage();
                    baseBmp.BeginInit();
                    baseBmp.CacheOption = BitmapCacheOption.OnLoad;
                    baseBmp.StreamSource = sri.Stream;
                    baseBmp.EndInit();
                    baseBmp.Freeze();

                    bool flipX = (flip & 0x01) != 0;
                    bool flipY = (flip & 0x02) != 0;

                    if (flipX || flipY)
                    {
                        var tb = new TransformedBitmap(baseBmp, new ScaleTransform(flipX ? -1 : 1, flipY ? -1 : 1));
                        tb.Freeze();
                        bmp = tb;
                    }
                    else
                    {
                        bmp = baseBmp;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FacetPreview] Failed to load pack image '{packUri}': {ex.Message}");
                    // keep trying the next candidate
                }
            }

            System.Diagnostics.Debug.WriteLine(
                "[FacetPreview] No embedded tile found for " +
                $"page={page}, tx={tx}, ty={ty}. Tried: {string.Join(" | ", tried)}");
            return false;
        }

        private IEnumerable<string> EnumerateTilePackUris(string variant, string subfolder, byte page, byte tx, byte ty)
        {
            int indexInPage = ty * 8 + tx;     // 0..63
            int totalIndex = page * 64 + indexInPage;

            // e.g., .../tex254hi.png, .../tex254.png, .../tex254hi.bmp, .../tex254.bmp
            string basePath = $"pack://application:,,,/Assets/Textures/{variant}/{subfolder}/tex{totalIndex:D3}";
            yield return basePath + "hi.png";
            yield return basePath + ".png";
            yield return basePath + "hi.bmp";
            yield return basePath + ".bmp";
        }

    }
}
