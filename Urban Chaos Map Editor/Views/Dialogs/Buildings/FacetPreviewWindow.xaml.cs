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
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using System.Windows.Shapes;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.Models.Styles;
using UrbanChaosMapEditor.Services;
using UrbanChaosMapEditor.Services.DataServices;
// --- resolve common ambiguities cleanly ---
using IOPath = System.IO.Path;
using StyleTextureEntry = UrbanChaosMapEditor.Models.Styles.TextureEntry;
using TextureEntry = UrbanChaosMapEditor.Models.Styles.TextureEntry;

namespace UrbanChaosMapEditor.Views.Dialogs.Buildings
{
    public partial class FacetPreviewWindow : Window
    {
        private const int PanelPx = 64;
      

        private DFacetRec _facet;

        // Snapshot tables we need to resolve styles
        private short[] _dstyles = Array.Empty<short>();
        private BuildingArrays.DStoreyRec[] _storeys = Array.Empty<BuildingArrays.DStoreyRec>();
        private byte[] _paintMem = Array.Empty<byte>();

        // Texture location hints
        private readonly string _variant;      // "Release" or "Beta"
        private readonly int _worldNumber;
        private readonly string? _texturesRoot;

        private static int NormalizeStyleId(int id) => id <= 0 ? 1 : id;
        private readonly int _facetIndex1; // 1-based file-order index for this facet
        private ObservableCollection<FlagItem>? _flagItems;

        private readonly int _facetId1;          // 1-based file-order id you already pass in
        private int _facetBaseOffset = -1;       // absolute offset of this facet in the map bytes
        private const int DFacetSize = 26;       // facet record size (bytes)



        public FacetPreviewWindow(DFacetRec facet, int facetId1)
        {
            InitializeComponent();
            _facet = facet;
            _facetIndex1 = facetId1;

            // Get both values directly from the shell's Map object. No defaults.
            if (!TryResolveVariantAndWorld(out _variant, out _worldNumber))
            {
                _variant = null;
                _worldNumber = 0;
                System.Diagnostics.Debug.WriteLine("[FacetPreview] FATAL: Could not resolve Map.UseBetaTextures/Map.TextureWorld from shell Map. No defaults will be used.");
            }

            Loaded += async (_, __) => await BuildUIAsync();
        }



        // --- Flag item model (with the bit it represents) ---
        private sealed class FlagItem
        {
            public string Name { get; init; } = "";
            public FacetFlags Bit { get; init; }
            public bool IsSet { get; set; }
        }

        // Build list from current flags
        private static IEnumerable<FlagItem> BuildFlagItemsEx(FacetFlags f)
        {
            FlagItem Make(string name, FacetFlags bit) => new() { Name = name, Bit = bit, IsSet = (f & bit) != 0 };

            yield return Make("Invisible", FacetFlags.Invisible);
            yield return Make("Inside", FacetFlags.Inside);
            yield return Make("Dlit", FacetFlags.Dlit);
            yield return Make("HugFloor", FacetFlags.HugFloor);
            yield return Make("Electrified", FacetFlags.Electrified);
            yield return Make("TwoSided", FacetFlags.TwoSided);
            yield return Make("Unclimbable", FacetFlags.Unclimbable);
            yield return Make("OnBuilding", FacetFlags.OnBuilding);
            yield return Make("BarbTop", FacetFlags.BarbTop);
            yield return Make("SeeThrough", FacetFlags.SeeThrough);
            yield return Make("Open", FacetFlags.Open);
            yield return Make("Deg90", FacetFlags.Deg90);
            yield return Make("TwoTextured", FacetFlags.TwoTextured);
            yield return Make("FenceCut", FacetFlags.FenceCut);
        }

        // Hook events once items are set
        private void HookFlagEvents()
        {
            FlagsList.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent,
                new RoutedEventHandler(OnFlagToggled));
            FlagsList.AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent,
                new RoutedEventHandler(OnFlagToggled));
        }

        // Recompute mask + write to file
        private void OnFlagToggled(object? sender, RoutedEventArgs e)
        {
            if (_flagItems == null) return;

            FacetFlags newMask = 0;
            foreach (var it in _flagItems)
                if (it.IsSet) newMask |= it.Bit;

            // Persist via BuildingsAccessor
            var ok = new BuildingsAccessor(MapDataService.Instance).TryUpdateFacetFlags(_facetIndex1, newMask);
            if (!ok)
            {
                Debug.WriteLine($"[FacetPreview] TryUpdateFacetFlags failed for facet #{_facetIndex1}");
                return;
            }

            // Update the on-screen hex line
            FacetFlagsText.Text = $"Flags: 0x{((ushort)newMask):X4}   Building={_facet.Building} Storey={_facet.Storey} StyleIndex={_facet.StyleIndex}";
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
            // Populate the Type dropdown
            FacetTypeCombo.ItemsSource = Enum.GetValues(typeof(FacetType)).Cast<FacetType>().ToList();
            FacetTypeCombo.SelectedItem = _facet.Type;

            // Get a fresh snapshot to compute this facet's absolute byte offset
            var arrays = new BuildingsAccessor(MapDataService.Instance).ReadSnapshot();

            // You need the absolute start of the facets table from the snapshot.
            // If your snapshot type is `BuildingArrays`, add an int property like `FacetsStart` (absolute).
            _facetBaseOffset = arrays.FacetsStart + (_facetIndex1 - 1) * DFacetSize;

            // Show segments + an approximate storey count for non-fence types (4 segments = 1 storey)
            int approxStoreys = UsesVerticalUnitsAsPanels(_facet.Type)
                ? Math.Max(1, (int)_facet.Height)
                : Math.Max(1, ((int)_facet.Height + 3) / 4); // ceil(height/4)

            // Meta
            FacetIdText.Text = $"Facet (file-order id not shown)";
            InitFacetTypeCombo();
            FacetCoordsText.Text = $"Coords: ({_facet.X0},{_facet.Z0}) → ({_facet.X1},{_facet.Z1})";
            FacetHeightText.Text =
                                    UsesVerticalUnitsAsPanels(_facet.Type)
                                    ? $"Height: coarse={_facet.Height} fine={_facet.FHeight}  (~{approxStoreys} stacked fence panels)"
                                    : $"Height: coarse={_facet.Height} fine={_facet.FHeight}  (~{approxStoreys} storeys)";
            FacetFlagsText.Text = $"Flags: 0x{((ushort)_facet.Flags):X4}   Building={_facet.Building} Storey={_facet.Storey} StyleIndex={_facet.StyleIndex}";
            _flagItems = new ObservableCollection<FlagItem>(BuildFlagItemsEx(_facet.Flags));
            FlagsList.ItemsSource = _flagItems;
            HookFlagEvents();

            // Grab building block snapshot (for dstyles / paint mem / storeys)
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

        private bool _initializingTypeCombo;

        private void InitFacetTypeCombo()
        {
            _initializingTypeCombo = true;
            FacetTypeCombo.ItemsSource = Enum.GetValues(typeof(FacetType)).Cast<FacetType>().ToList();
            FacetTypeCombo.SelectedItem = _facet.Type;
            _initializingTypeCombo = false;
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

        private static bool UsesVerticalUnitsAsPanels(FacetType t) =>
                            t == FacetType.Fence ||
                            t == FacetType.FenceFlat ||
                            t == FacetType.FenceBrick ||
                            t == FacetType.Ladder ||
                            t == FacetType.Trench;

        private void FacetTypeCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initializingTypeCombo) return;
            if (FacetTypeCombo.SelectedItem is not FacetType newType ||
                newType == _facet.Type)
                return;

            var acc = new BuildingsAccessor(MapDataService.Instance);

            // Use the id you actually initialized in the ctor (_facetIndex1)
            if (acc.TryUpdateFacetType(_facetIndex1, newType))
            {
                // Build a preview copy WITHOUT 'with'
                var preview = CopyFacetWithType(_facet, newType);

                // Optionally keep our local snapshot in sync
                _facet = preview;

                DrawPreview(preview);
            }
        }

        private static DFacetRec CopyFacetWithType(DFacetRec f, FacetType newType)
        {
            return new DFacetRec(
                newType,
                f.X0, f.Z0, f.X1, f.Z1,
                f.Height, f.FHeight,
                f.StyleIndex, f.Building, f.Storey, f.Flags,
                f.Y0, f.Y1,
                f.BlockHeight,
                f.Open,
                f.Dfcache,   // <-- lowercase 'c'
                f.Shake,
                f.CutHole,
                f.Counter0,
                f.Counter1
            );
        }


        private string BuildRawRecipeString(int rawStyleId)
        {
            var svc = StyleDataService.Instance;
            var tma = svc.TmaSnapshot;
            if (tma == null) return "(style.tma not loaded)";

            int idx = StyleDataService.MapRawStyleIdToTmaIndex(rawStyleId);
            if (idx < 0 || idx >= tma.TextureStyles.Count) return "(style out of range)";

            var style = tma.TextureStyles[idx];
            var sb = new StringBuilder();
            sb.Append($"Style {idx}: ");
            for (int i = 0; i < style.Entries.Count; i++)
            {
                var e = style.Entries[i];
                if (i > 0) sb.Append(" | ");
                sb.Append($"[{i}] P{e.Page} Tx{e.Tx} Ty{e.Ty} F{e.Flip}");
            }
            return sb.ToString();
        }

        // If there are >1 rows, the TOP row (row==0) uses (base-1).
        // If base-1 == 0, alias to 1. If only 1 row, just use base.
        private static int StyleIdForRow(int baseId, int row, int panelsDown)
        {
            int n = NormalizeStyleId(baseId);
            if (panelsDown > 1 && row == 0)
            {
                int cap = n - 1;
                return NormalizeStyleId(cap);
            }
            return n;
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

        // Get the tile (Tx/Ty/Flip) from style.tma and page override from paint_mem if painted.
        private bool TryResolvePanelTileRaw(int rawStyleId, int slot, bool painted, in BuildingArrays.DStoreyRec ds,
                                            int panelIndexAlong, out UrbanChaosMapEditor.Models.Styles.TextureEntry entry, out byte? pageOverride)
        {
            entry = default;
            pageOverride = null;

            int styleId = NormalizeStyleId(rawStyleId);
            if (!TryGetTmaEntry(styleId, slot, out entry))
                return false;

            if (painted && _paintMem != null && ds.Count > 0 &&
                ds.PaintIndex >= 0 && ds.PaintIndex + ds.Count <= _paintMem.Length)
            {
                int clamped = Math.Min(panelIndexAlong, ds.Count - 1);
                byte b = _paintMem[ds.PaintIndex + clamped];
                pageOverride = (byte)(b & 0x7F);
            }

            return true;
        }

        // Resolve dstyles → base style (or DStorey base).
        private bool TryResolveBase(ushort styleIndex, out int baseStyleId, out bool painted, out BuildingArrays.DStoreyRec ds)
        {
            baseStyleId = -1;
            painted = false;
            ds = default;

            if (_dstyles == null || styleIndex >= _dstyles.Length) return false;

            short val = _dstyles[styleIndex];
            if (val >= 0)
            {
                baseStyleId = NormalizeStyleId(val);
                return true;
            }

            // painted
            painted = true;
            int sid = -val;         // 1-based
            int idx = sid - 1;
            if (idx < 0 || idx >= _storeys.Length) return false;

            ds = _storeys[idx];
            baseStyleId = NormalizeStyleId(ds.StyleIndex);
            return true;
        }

        // ---------- Preview drawing ----------
        private void DrawPreview(DFacetRec f)
        {
            // Horizontal panels = distance in tiles along X/Z (each tile = one 64px panel).
            int dx = Math.Abs(f.X1 - f.X0);
            int dz = Math.Abs(f.Z1 - f.Z0);
            int panelsAcross = Math.Max(dx, dz);
            if (panelsAcross <= 0) panelsAcross = 1;

            // Vertical panels from pixel height: Height is 16px units, FHeight is pixels.
            const int PanelPx = 64;
            int totalPixelsY = f.Height * 16 + f.FHeight;
            int panelsDown = Math.Max(1, (totalPixelsY + (PanelPx - 1)) / PanelPx); // ceil-div

            int width = panelsAcross * PanelPx;
            int height = panelsDown * PanelPx;

            PanelCanvas.Children.Clear();
            GridCanvas.Children.Clear();
            PanelCanvas.Width = width; PanelCanvas.Height = height;
            GridCanvas.Width = width; GridCanvas.Height = height;

            // Resolve base style (RAW or PAINTED)
            if (!TryResolveBase(f.StyleIndex, out int baseStyleId, out bool isPainted, out var dstorey))
            {
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = width,
                    Height = height,
                    Fill = new SolidColorBrush(Color.FromRgb(0x55, 0x58, 0x5E))
                };
                System.Windows.Controls.Canvas.SetLeft(rect, 0);
                System.Windows.Controls.Canvas.SetTop(rect, 0);
                PanelCanvas.Children.Add(rect);
            }
            else
            {
                for (int row = 0; row < panelsDown; row++)
                {
                    // Cap rule: only if there is more than 1 row; top row uses (base-1), with 0 ⇒ 1.
                    int rowStyleId = StyleIdForRow(baseStyleId, row, panelsDown);

                    for (int col = 0; col < panelsAcross; col++)
                    {
                        int slot = col % 5;
                        int panelIndexAlong = col;

                        if (TryResolvePanelTileRaw(rowStyleId, slot, isPainted, dstorey, panelIndexAlong,
                                                   out var texEntry, out byte? pageOverride) &&
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
                            var rect = new System.Windows.Shapes.Rectangle
                            {
                                Width = PanelPx,
                                Height = PanelPx,
                                Fill = new SolidColorBrush(Color.FromRgb(0x55, 0x58, 0x5E))
                            };
                            System.Windows.Controls.Canvas.SetLeft(rect, col * PanelPx);
                            System.Windows.Controls.Canvas.SetTop(rect, row * PanelPx);
                            PanelCanvas.Children.Add(rect);

                            Debug.WriteLine($"[FacetPreview] Resolve failed row={row} col={col} (base={baseStyleId}→rowStyle={rowStyleId}, slot={slot}, styleIdx={f.StyleIndex}).");
                        }
                    }
                }
            }

            // 64×64 grid + outline
            DrawGrid(GridCanvas, width, height, PanelPx, PanelPx);
            var outline = new System.Windows.Shapes.Rectangle
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
        private bool TryResolveTileForPanel(ushort styleIndex, int col, int row,
                                    out TextureEntry entry, out byte? pageOverride)
        {
            entry = default; pageOverride = null;
            if (_dstyles == null || styleIndex >= _dstyles.Length) return false;

            short dval = _dstyles[styleIndex];
            int slot = (col + row) % 5;                 // matches the game’s slot cycling

            // --- work out the base style id (raw or painted) ---
            int baseStyleId;
            BuildingArrays.DStoreyRec? ds = null;

            if (dval >= 0)
            {
                baseStyleId = dval;
            }
            else
            {
                int sid = -dval;
                if (sid < 1 || sid > _storeys.Length) return false;
                ds = _storeys[sid - 1];
                baseStyleId = ds.Value.StyleIndex;

                // pageOverride from paint_mem (clamped horizontally)
                var bytes = GetPaintBytes(ds.Value);
                if (bytes.Length > 0)
                {
                    int ix = Math.Min(col, bytes.Length - 1);
                    pageOverride = (byte)(bytes[ix] & 0x7F);
                }
            }

            // --- TOP CAP RULE ---
            // Top row (row==0) of exterior normal walls uses the "cap" style = baseStyleId - 1 (if > 0)
            if (row == 0 &&
                _facet.Type == FacetType.Normal &&
                (_facet.Flags & (FacetFlags.Inside | FacetFlags.TwoSided)) == 0 &&
                baseStyleId > 0)
            {
                baseStyleId -= 1;
            }

            // fetch the TMA entry with effective style + slot
            return TryGetTmaEntry(baseStyleId, slot, out entry);
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
