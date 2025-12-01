// /Views/Dialogs/Buildings/FacetPreviewWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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

        private void DebugDumpStyleRow(int rawStyleId)
        {
            var tma = StyleDataService.Instance.TmaSnapshot;
            if (tma == null)
            {
                Debug.WriteLine($"[StyleDump] style.tma not loaded.");
                return;
            }

            int idx = StyleDataService.MapRawStyleIdToTmaIndex(rawStyleId);
            if (idx < 0 || idx >= tma.TextureStyles.Count)
            {
                Debug.WriteLine($"[StyleDump] Raw style {rawStyleId} -> index {idx} out of range.");
                return;
            }

            var style = tma.TextureStyles[idx];
            Debug.WriteLine($"[StyleDump] Raw style {rawStyleId} (TMA row {idx}), entries={style.Entries.Count}");

            for (int i = 0; i < style.Entries.Count; i++)
            {
                var e = style.Entries[i];
                int globalIndex = e.Page * 64 + e.Ty * 8 + e.Tx;
                Debug.WriteLine(
                    $"  slot[{i}]: Page={e.Page} Tx={e.Tx} Ty={e.Ty} Flip={e.Flip} -> tex{globalIndex:D3}");
            }
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

            // DEBUG: dump style chain + DStorey for this facet
            try
            {
                var styles = arrays.Styles ?? Array.Empty<short>();
                var storeys = arrays.Storeys ?? Array.Empty<BuildingArrays.DStoreyRec>();
                var paintMem = arrays.PaintMem ?? Array.Empty<byte>();

                Debug.WriteLine("===== PAINT DEBUG FOR FACET =====");
                Debug.WriteLine($"FacetId1={_facetIndex1}  Facet.StyleIndex={_facet.StyleIndex}  Height={_facet.Height}  FHeight={_facet.FHeight}");
                Debug.WriteLine($"FacetFlags=0x{(ushort)_facet.Flags:X4}  Type={_facet.Type}");

                // 1) What does dstyles look like around this facet?
                int baseIdx = _facet.StyleIndex;
                int span = 8;   // dump a small window
                for (int i = 0; i < span; i++)
                {
                    int idx = baseIdx + i;
                    if (idx < 0 || idx >= styles.Length) break;
                    short val = styles[idx];

                    if (val >= 0)
                    {
                        Debug.WriteLine($"dstyles[{idx}] = {val}  (RAW style id)");
                    }
                    else
                    {
                        int sid = -val;
                        string extra = (sid >= 1 && sid <= storeys.Length)
                            ? $" -> DStorey[{sid}]"
                            : " -> (OUT OF RANGE)";
                        Debug.WriteLine($"dstyles[{idx}] = {val}  (PAINTED) {extra}");
                    }
                }

                // 2) If the first entry is painted, dump that DStorey and its bytes
                if (baseIdx >= 0 && baseIdx < styles.Length && styles[baseIdx] < 0)
                {
                    int sid = -styles[baseIdx];       // 1-based id in Fallen
                    int sIdx = sid - 1;               // 0-based C# index

                    if (sIdx >= 0 && sIdx < storeys.Length)
                    {
                        var ds = storeys[sIdx];

                        Debug.WriteLine($"DStorey sid={sid} -> Style(base)={ds.StyleIndex}  PaintIndex={ds.PaintIndex}  Count={ds.Count}");

                        if (ds.PaintIndex + ds.Count <= paintMem.Length)
                        {
                            var sb = new StringBuilder();
                            for (int i = 0; i < ds.Count; i++)
                            {
                                if (i > 0) sb.Append(' ');
                                sb.Append(paintMem[ds.PaintIndex + i].ToString("X2"));
                            }
                            Debug.WriteLine($"Paint bytes[{ds.PaintIndex}..+{ds.Count}): {sb}");
                        }
                        else
                        {
                            Debug.WriteLine($"Paint bytes out of range: idx={ds.PaintIndex} count={ds.Count} paintMemLen={paintMem.Length}");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"ERROR: DStorey id {sid} not in storeys[] (len={storeys.Length})");
                    }
                }

                Debug.WriteLine("=================================");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PaintDebug] Exception while dumping: {ex}");
            }

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

            DebugDumpStyleRow(11);
            DebugDumpStyleRow(12);

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

        // Build the 5-slot ring of tile indices (0..63) for a raw style id.
        // Each slot i uses Entries[i].Tx/Ty; we return Ty*8 + Tx for each.
        // Build the 5-slot ring of *global* tile indices (texNNN) for a raw style id.
        // Each slot i uses Entries[i].(Page,Tx,Ty); we return Page*64 + Ty*8 + Tx for each.
        private bool GetStyleRing(int rawStyleId, out int[] ring, out IList<StyleTextureEntry> entries)
        {
            ring = Array.Empty<int>();
            entries = Array.Empty<StyleTextureEntry>();

            var tma = StyleDataService.Instance.TmaSnapshot;
            if (tma == null) return false;

            int idx = StyleDataService.MapRawStyleIdToTmaIndex(rawStyleId); // 0->1, 1->1, else raw
            if (idx < 0 || idx >= tma.TextureStyles.Count) return false;

            var style = tma.TextureStyles[idx];
            entries = style.Entries;
            if (entries == null || entries.Count < 5) return false;

            var r = new int[5];
            for (int i = 0; i < 5; i++)
            {
                var e = entries[i];

                // Global tex index NNN, matching texNNNhi.png
                int indexInPage = e.Ty * 8 + e.Tx;      // 0..63 within the 8×8 page
                int globalIndex = e.Page * 64 + indexInPage;

                r[i] = globalIndex;
            }

            ring = r;
            return true;
        }



        [MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static int Wrap(int x, int n) => n <= 0 ? 0 : ((x % n) + n) % n;

        [MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static int Mod64(int x) => (x & 63);

        /// <summary>
        /// Resolve (page, tx, ty, flip) for one cell of the facet preview.
        /// col: 0..panelsAcross-1
        /// rowFromBottom: 0 = ground row, panelsDown-1 = top row
        /// </summary>
        private bool TryResolvePanelTile(int col, int rowFromBottom, int panelsAcross, int panelsDown,
                                         out byte page, out byte tx, out byte ty, out byte flip)
        {
            page = tx = ty = flip = 0;

            if (_dstyles == null || _dstyles.Length == 0)
                return false;
            if (rowFromBottom < 0 || rowFromBottom >= panelsDown)
                return false;

            // --- Match FACET_draw's style_index stepping ---
            bool twoTextured = (_facet.Flags & FacetFlags.TwoTextured) != 0;
            bool twoSided = (_facet.Flags & FacetFlags.TwoSided) != 0;
            bool hugFloor = (_facet.Flags & FacetFlags.HugFloor) != 0;

            int styleIndexStep = (!hugFloor && (twoTextured || twoSided)) ? 2 : 1;

            int styleIndexStart = _facet.StyleIndex;
            if (twoTextured)
                styleIndexStart--;   // FACET_draw does this

            // ----- Row remap for painted section -----
            // We treat the top row (rowFromBottom == panelsDown-1) as the cap row and
            // leave it alone. The painted rows are [0 .. paintedRows-1].
            int paintedRows = Math.Max(0, panelsDown - 1);

            int styleRow = rowFromBottom;

            if (paintedRows > 0 && rowFromBottom < paintedRows)
            {
                // Rotate the painted rows by +1:
                //   desired row 0 uses former row 1's style,
                //   desired row 1 uses former row 2's style,
                //   desired row 2 uses former row 3's style,
                //   desired row 3 uses former row 0's style, etc.
                styleRow = (rowFromBottom + 1) % paintedRows;
            }
            // else: top cap row or non-painted case → use rowFromBottom as-is.

            int styleIndexForRow = styleIndexStart + styleRow * styleIndexStep;
            if (styleIndexForRow < 0 || styleIndexForRow >= _dstyles.Length)
                return false;

            short dval = _dstyles[styleIndexForRow];

            int count = panelsAcross + 1;   // like Fallen: segments+1
            int pos = col;                // horizontal segment index

            // Fallen-like core resolver: raw vs painted.
            if (!TryResolveTileIdForCell(dval, pos, count, out int tileId, out byte flipFlag))
                return false;

            if (tileId < 0)
                return false;

            // Map tileId -> hi-res (page, tx, ty) so texNNNhi uses N = tileId.
            int hiPage = tileId / 64;
            int idxInPage = tileId % 64;

            page = (byte)hiPage;
            tx = (byte)(idxInPage % 8);
            ty = (byte)(idxInPage / 8);
            flip = flipFlag;

            return true;
        }

        /// <summary>
        /// Core Fallen-like resolve: given a dstyles entry value (raw or painted),
        /// and horizontal pos/count, compute the global texture id ("tileId") and flip flag.
        /// This emulates the C code paths in texture_quad/get_texture_page:
        ///   - texture_style &lt; 0 → DStorey + paint_mem bytes
        ///   - texture_style ≥ 0 → raw style → style.tma
        /// </summary>
        private bool TryResolveTileIdForCell(short dstyleValue, int pos, int count,
                                             out int tileId, out byte flip)
        {
            tileId = -1;
            flip = 0;

            if (dstyleValue >= 0)
            {
                // RAW style.
                return ResolveRawTileId(dstyleValue, pos, count, out tileId, out flip);
            }
            else
            {
                // PAINTED: negative DStorey id (1-based).
                int storeyId = -dstyleValue;  // 1-based
                if (_storeys == null || storeyId < 1 || storeyId > _storeys.Length)
                    return false;

                var ds = _storeys[storeyId - 1];
                return ResolvePaintedTileId(ds, pos, count, out tileId, out flip);
            }
        }

        /// <summary>
        /// Resolve RAW style: use style.tma row for 'rawStyleId' and map TEXTURE_PIECE to a slot.
        /// We then compute a global tileId = Page*64 + Ty*8 + Tx.
        /// </summary>
        private bool ResolveRawTileId(int rawStyleId, int pos, int count,
                                      out int tileId, out byte flip)
        {
            tileId = -1;
            flip = 0;

            var tma = StyleDataService.Instance.TmaSnapshot;
            if (tma == null)
                return false;

            int styleId = rawStyleId;
            if (styleId <= 0)
                styleId = 1;   // Fallen: if(texture_style==0) texture_style=1

            int idx = StyleDataService.MapRawStyleIdToTmaIndex(styleId);
            if (idx < 0 || idx >= tma.TextureStyles.Count)
                return false;

            var style = tma.TextureStyles[idx];
            var entries = style.Entries;
            if (entries == null || entries.Count == 0)
                return false;

            // TEXTURE_PIECE mapping (deterministic, no randomness):
            // pos == 0       → RIGHT
            // pos == count-2 → LEFT
            // otherwise      → MIDDLE
            // We map:
            //   RIGHT  → slot 0
            //   LEFT   → slot 1
            //   MIDDLE → slot 2
            int pieceIndex;
            if (pos == 0)
                pieceIndex = 0;        // RIGHT
            else if (pos == count - 2)
                pieceIndex = 1;        // LEFT
            else
                pieceIndex = 2;        // MIDDLE

            if (pieceIndex >= entries.Count)
                pieceIndex = entries.Count - 1;

            var e = entries[pieceIndex];

            // Global "tile id" like original 'page' in older engines.
            tileId = e.Page * 64 + e.Ty * 8 + e.Tx;
            flip = e.Flip;
            return true;
        }

        /// <summary>
        /// PAINTED: use DStorey + paint_mem bytes.
        /// Mirrors texture_quad / get_texture_page semantics:
        ///   - b = paint_mem[Index + pos]
        ///   - bit7 = flip; low 7 bits = texture index (0..127)
        ///   - 0 => "no paint" → fall back to base style.
        /// </summary>
        private bool ResolvePaintedTileId(BuildingArrays.DStoreyRec ds,
                                          int pos, int count,
                                          out int tileId, out byte flip)
        {
            tileId = -1;
            flip = 0;

            int baseStyle = ds.StyleIndex;

            if (_paintMem == null || _paintMem.Length == 0 || ds.Count == 0)
            {
                // No paint bytes at all → act like RAW base style.
                return ResolveRawTileId(baseStyle, pos, count, out tileId, out flip);
            }

            int paintStart = ds.PaintIndex;
            int paintCount = ds.Count;

            if (paintStart < 0 || paintStart + paintCount > _paintMem.Length)
            {
                // Corrupt / out of range → fall back to base.
                return ResolveRawTileId(baseStyle, pos, count, out tileId, out flip);
            }

            if (pos >= paintCount)
            {
                // Above painted range: "else texture_style = p_storey->Style"
                return ResolveRawTileId(baseStyle, pos, count, out tileId, out flip);
            }

            byte raw = _paintMem[paintStart + pos];

            // High bit = flip X
            flip = (byte)(((raw & 0x80) != 0) ? 1 : 0);

            // Low 7 bits = "page"/texture index 0..127
            int val = raw & 0x7F;

            if (val == 0)
            {
                // 0 means "no paint" → use base style.
                return ResolveRawTileId(baseStyle, pos, count, out tileId, out flip);
            }

            // Here 'val' is the global texture id we want (e.g. 10, 15, 16, 17).
            tileId = val;
            return true;
        }

        /// <summary>
        /// RAW style: use style.tma mapping for (styleId, texturePiece).
        /// texturePiece: 0 = RIGHT, 1 = LEFT, 2 = MIDDLE.
        /// We map to slots: 0→0, 1→1, 2→2 (MIDDLE family).
        /// </summary>
        private bool ResolveRawTile(int rawStyleId, int texturePiece,
                                    out byte page, out byte tx, out byte ty, out byte flip)
        {
            page = tx = ty = flip = 0;

            // Map piece → slot index in TMA row.
            int slot = texturePiece switch
            {
                0 => 0, // RIGHT
                1 => 1, // LEFT
                _ => 2  // MIDDLE/M1/M2 family
            };

            if (!TryGetTmaEntry(rawStyleId, slot, out StyleTextureEntry entry))
                return false;

            page = (byte)entry.Page;
            tx = (byte)entry.Tx;
            ty = (byte)entry.Ty;
            flip = (byte)entry.Flip;
            return true;
        }

        /// <summary>
        /// PAINTED: we have a DStorey row with base StyleIndex + paint_mem bytes.
        /// We mimic Fallen texture_quad/get_texture_page:
        ///   - Take paint byte for this column (wrapping across Count).
        ///   - Bit 7 = flip X; low 7 bits = "page" override.
        ///   - If low 7 bits == 0 → fall back to base style.
        /// For our hi-res tiles we:
        ///   - Use 'pageOverride' (if non-zero) as the PAGE,
        ///   - Use base style's Tx/Ty as the tile inside that page,
        ///   - XOR flip from base style with paint flip bit.
        /// </summary>
        private bool ResolvePaintedTile(BuildingArrays.DStoreyRec ds,
                                        int texturePiece, int col, int panelsAcross,
                                        out byte page, out byte tx, out byte ty, out byte flip)
        {
            page = tx = ty = flip = 0;

            // Base style from DStorey
            int baseStyleId = ds.StyleIndex;
            if (baseStyleId <= 0)
                baseStyleId = 1;

            // Get base entry from style.tma for orientation and Tx/Ty.
            if (!ResolveRawTile(baseStyleId, texturePiece, out byte basePage, out byte baseTx, out byte baseTy, out byte baseFlip))
                return false;

            byte finalPage = basePage;
            byte finalTx = baseTx;
            byte finalTy = baseTy;
            byte finalFlip = baseFlip;

            // No paint_mem or zero count -> behave like RAW base style.
            if (_paintMem == null || _paintMem.Length == 0 || ds.Count == 0)
            {
                page = finalPage; tx = finalTx; ty = finalTy; flip = finalFlip;
                return true;
            }

            int paintStart = ds.PaintIndex;
            int paintCount = ds.Count;

            if (paintStart < 0 || paintStart + paintCount > _paintMem.Length)
            {
                // Corrupt indices; fall back to base.
                page = finalPage; tx = finalTx; ty = finalTy; flip = finalFlip;
                return true;
            }

            // Fallen uses 'pos' horizontally; we clamp/wrap across Count.
            int paintIx = paintStart + (col % paintCount);
            byte paintByte = _paintMem[paintIx];

            byte paintFlip = (byte)((paintByte & 0x80) != 0 ? 1 : 0);
            byte paintPage = (byte)(paintByte & 0x7F);

            if (paintPage == 0)
            {
                // "no paint" for this column → base style only.
                page = finalPage;
                tx = finalTx;
                ty = finalTy;
                flip = finalFlip;
                return true;
            }

            // Painted: override PAGE but keep Tx/Ty from base style.
            finalPage = paintPage;
            finalFlip = (byte)(finalFlip ^ paintFlip); // XOR base flip with paint flip bit

            page = finalPage;
            tx = finalTx;
            ty = finalTy;
            flip = finalFlip;
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

            for (int rowFromTop = 0; rowFromTop < panelsDown; rowFromTop++)
            {
                // Engine logic is bottom-up. Convert UI row → "engine row".
                int rowFromBottom = panelsDown - 1 - rowFromTop;

                for (int col = 0; col < panelsAcross; col++)
                {
                    if (TryResolvePanelTile(col, rowFromBottom, panelsAcross, panelsDown,
                                            out byte page, out byte tx, out byte ty, out byte flip))
                    {
                        int tileId = page * 64 + ty * 8 + tx;   // global tile id
                        string tooltipText = $"tex{tileId:D3}hi";

#if DEBUG
                        Debug.WriteLine(
                            $"[FacetPreview] r(bottom)={rowFromBottom} c={col} pd={panelsDown} " +
                            $"-> page={page} tx={tx} ty={ty} flip={flip} (tileId={tileId}, {tooltipText}.png)");
#endif

                        if (TryLoadTileBitmap(page, tx, ty, flip, out var bmp))
                        {
                            var img = new System.Windows.Controls.Image
                            {
                                Width = PanelPx,
                                Height = PanelPx,
                                Source = bmp,
                                ToolTip = tooltipText
                            };

                            Canvas.SetLeft(img, col * PanelPx);
                            Canvas.SetTop(img, rowFromTop * PanelPx);
                            PanelCanvas.Children.Add(img);
                        }
                        else
                        {
                            var rect = new System.Windows.Shapes.Rectangle
                            {
                                Width = PanelPx,
                                Height = PanelPx,
                                Fill = new SolidColorBrush(Color.FromRgb(0x55, 0x58, 0x5E)),
                                ToolTip = tooltipText
                            };
                            Canvas.SetLeft(rect, col * PanelPx);
                            Canvas.SetTop(rect, rowFromTop * PanelPx);
                            PanelCanvas.Children.Add(rect);
                        }
                    }
                    else
                    {
                        var rect = new System.Windows.Shapes.Rectangle
                        {
                            Width = PanelPx,
                            Height = PanelPx,
                            Fill = new SolidColorBrush(Color.FromRgb(0x55, 0x58, 0x5E)),
                            ToolTip = "(no tile)"
                        };
                        Canvas.SetLeft(rect, col * PanelPx);
                        Canvas.SetTop(rect, rowFromTop * PanelPx);
                        PanelCanvas.Children.Add(rect);

#if DEBUG
                        Debug.WriteLine(
                            $"[FacetPreview] resolve failed r(bottom)={rowFromBottom} c={col} pd={panelsDown}");
#endif
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

        // Resolve a raw style id (style.tma row) for a given column.
        // We *ignore* vertical row for raw styles (walls are vertically uniform).
        private bool ResolveRawFromStyle(int rawStyleId, int col,
                                         out byte page, out byte tx, out byte ty, out byte flip)
        {
            page = tx = ty = flip = 0;

            rawStyleId = NormalizeStyleId(rawStyleId);
            if (!GetStyleRing(rawStyleId, out var ring, out var entries))
                return false;

            int ringLen = ring.Length;
            if (ringLen <= 0 || entries.Count < ringLen)
                return false;

            // Approximate Fallen’s RIGHT/LEFT/MIDDLE selection with a simple horizontal cycle.
            int slot = col % ringLen;

            var e = entries[slot];
            flip = e.Flip;
            page = e.Page;

            int idxInPage = ring[slot];   // 0..63
            tx = (byte)(idxInPage & 7);
            ty = (byte)(idxInPage >> 3);

            return true;
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
            int slot = col % 5;                // matches the game’s slot cycling

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



        private bool TryGetTmaEntry(int rawStyleId, int slot, out StyleTextureEntry entry)
        {
            entry = default;

            var tma = StyleDataService.Instance.TmaSnapshot;
            if (tma == null) return false;

            // Map raw style id to the actual TMA row index:
            // (your current rule: raw 0 => 1, raw 1 => 1, otherwise raw => raw)
            int idx = StyleDataService.MapRawStyleIdToTmaIndex(rawStyleId);

            if (idx < 0 || idx >= tma.TextureStyles.Count) return false;

            var style = tma.TextureStyles[idx];
            var entries = style.Entries;
            if (entries == null || slot < 0 || slot >= entries.Count) return false;

            entry = entries[slot];
            return true;
        }

        private static void DecomposeEnginePage(int enginePage, out byte page, out byte tx, out byte ty)
        {
            int idx = enginePage & 63;      // 0..63 within “page”
            page = (byte)(enginePage >> 6);
            tx = (byte)(idx & 7);
            ty = (byte)(idx >> 3);
        }


        private bool TryLoadTileBitmap(byte page, byte tx, byte ty, byte flip, out BitmapSource? bmp)
        {
            bmp = null;

            if (string.IsNullOrWhiteSpace(_variant) || _worldNumber <= 0)
            {
                Debug.WriteLine($"[FacetPreview] TryLoadTileBitmap: Variant/World unresolved — cannot load tile (page={page}, tx={tx}, ty={ty}).");
                return false;
            }

            // Decide candidate subfolders from the *page index* as before.
            var candidates = new List<string>(3);
            if (page <= 3)
                candidates.Add($"world{_worldNumber}");
            else if (page <= 7)
                candidates.Add("shared");
            else if (page == 8)
                candidates.Add($"world{_worldNumber}/insides");
            else
                candidates.Add($"world{_worldNumber}");   // permissive fallback

            int totalIndex;

            // Sentinel: tx==255 && ty==255 means "page is already the global texNNN index".
            if (tx == byte.MaxValue && ty == byte.MaxValue)
            {
                totalIndex = page;
            }
            else
            {
                int indexInPage = ty * 8 + tx;
                totalIndex = page * 64 + indexInPage;
            }

            foreach (var subfolder in candidates)
            {
                foreach (var packUri in EnumerateTilePackUris(_variant!, subfolder!, totalIndex))
                {
                    Debug.WriteLine($"[FacetPreview] Tile pack URI: {packUri}");
                    try
                    {
                        var sri = Application.GetResourceStream(new Uri(packUri));
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
                        Debug.WriteLine($"[FacetPreview] Failed to load pack image '{packUri}': {ex.Message}");
                    }
                }
            }

            Debug.WriteLine($"[FacetPreview] No embedded tile found for page={page}, tx={tx}, ty={ty} (totalIndex={totalIndex}).");
            return false;
        }


        private IEnumerable<string> EnumerateTilePackUris(string variant, string subfolder, int totalIndex)
        {
            yield return $"pack://application:,,,/Assets/Textures/{variant}/{subfolder}/tex{totalIndex:D3}hi.png";
        }

    }
}
