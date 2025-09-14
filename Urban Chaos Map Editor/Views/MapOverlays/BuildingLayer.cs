using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.Services;
using UrbanChaosMapEditor.ViewModels;

namespace UrbanChaosMapEditor.Views.MapOverlays
{
    /// <summary>
    /// Visualises the building (“super map”) block by drawing each facet as a line (x0,z0)->(x1,z1).
    /// Reads/caches the map bytes once per map load. Highlights current selection from MapViewModel:
    /// building, optional storey, and optional single facet.
    /// </summary>
    public sealed class BuildingLayer : FrameworkElement
    {
        // ---------- Fixed V1 layout (matches accessor/parser) ----------
        private const int HeaderSize = 48;
        private const int DBuildingSize = 24;
        private const int AfterBuildingsPad = 14;
        private const int DFacetSize = 26;

        // Pens for normal pass
        private static readonly Pen PenWallGreen;
        private static readonly Pen PenCableRed;
        private static readonly Pen PenFenceYellow;
        private static readonly Pen PenLadderOrange;
        private static readonly Pen PenDoorPurple;
        private static readonly Pen PenRoofBlue;
        private static readonly Pen PenInsideGray;
        private static readonly Pen PenDefault;

        static BuildingLayer()
        {
            static Pen Make(Pen p)
            {
                p.StartLineCap = PenLineCap.Round;
                p.EndLineCap = PenLineCap.Round;
                p.LineJoin = PenLineJoin.Round;
                p.Freeze();
                return p;
            }

            var fluoroGreen = new SolidColorBrush(Color.FromRgb(102, 255, 0)); fluoroGreen.Freeze();
            PenWallGreen = Make(new Pen(fluoroGreen, 6.0));
            PenCableRed = Make(new Pen(Brushes.Red, 6.0));
            PenFenceYellow = Make(new Pen(Brushes.Yellow, 5.0));
            PenLadderOrange = Make(new Pen(Brushes.Orange, 8.0));
            PenDoorPurple = Make(new Pen(Brushes.MediumPurple, 7.0));
            PenRoofBlue = Make(new Pen(Brushes.DeepSkyBlue, 4.5));
            PenInsideGray = Make(new Pen(Brushes.SlateGray, 4.5));
            PenDefault = Make(new Pen(fluoroGreen, 4.5));
        }

        // ----- glow stack for highlight -----
        private readonly Pen _glowPenWide;
        private readonly Pen _glowPenNarrow;
        private readonly Pen _edgePen;

        // ----- cached data (per map load) -----
        private byte[]? _cachedBytes;
        private int _bStart = -1;           // start of building block (header)
        private int _bLen = 0;
        private int _facetsOffsetAbs = -1;  // absolute file offset to first facet
        private int _totalBuildings = 0;
        private int _totalFacets = 0;

        // ----- selection coming from the VM (DataContext) -----
        private MapViewModel? _vm;
        private int _selBuildingId;
        private int? _selStoreyId;
        private int? _selFacetId; // null = highlight whole building/storey

        public BuildingLayer()
        {
            Width = MapConstants.MapPixels;
            Height = MapConstants.MapPixels;
            IsHitTestVisible = false;

            // Chalk-white glow pens
            var glowOuter = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)); glowOuter.Freeze();
            var glowInner = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)); glowInner.Freeze();
            var edgeBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)); edgeBrush.Freeze();

            _glowPenWide = new Pen(glowOuter, 12.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            _glowPenNarrow = new Pen(glowInner, 7.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            _edgePen = new Pen(edgeBrush, 2.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            _glowPenWide.Freeze(); _glowPenNarrow.Freeze(); _edgePen.Freeze();

            // lifecycle hooks
            var svc = MapDataService.Instance;
            svc.MapLoaded += (_, __) => { SeedFromService(); Dispatcher.Invoke(InvalidateVisual); };
            svc.MapBytesReset += (_, __) => { SeedFromService(); Dispatcher.Invoke(InvalidateVisual); };
            svc.MapCleared += (_, __) => { ClearCache(); Dispatcher.Invoke(InvalidateVisual); };

            DataContextChanged += (_, __) => HookVm();
        }

        private void HookVm()
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as MapViewModel;
            if (_vm != null)
            {
                _vm.PropertyChanged += OnVmChanged;

                // seed selection from VM
                _selBuildingId = _vm.SelectedBuildingId;
                _selStoreyId = _vm.SelectedStoreyId;
                _selFacetId = _vm.SelectedFacetId;
            }
            InvalidateVisual();
        }

        private void OnVmChanged(object? s, PropertyChangedEventArgs e)
        {
            if (_vm == null) return;

            if (e.PropertyName == nameof(MapViewModel.SelectedBuildingId) ||
                e.PropertyName == nameof(MapViewModel.SelectedStoreyId) ||
                e.PropertyName == nameof(MapViewModel.SelectedFacetId))
            {
                _selBuildingId = _vm.SelectedBuildingId;
                _selStoreyId = _vm.SelectedStoreyId;
                _selFacetId = _vm.SelectedFacetId;
                Dispatcher.Invoke(InvalidateVisual);
                return;
            }

            // keep any other invalidations you had
            if (e.PropertyName == nameof(MapViewModel.Prims) ||
                e.PropertyName == nameof(MapViewModel.SelectedPrim))
            {
                Dispatcher.Invoke(InvalidateVisual);
            }
        }

        protected override Size MeasureOverride(Size availableSize)
            => new(MapConstants.MapPixels, MapConstants.MapPixels);

        private void ClearCache()
        {
            _cachedBytes = null;
            _bStart = -1;
            _bLen = 0;
            _facetsOffsetAbs = -1;
            _totalBuildings = 0;
            _totalFacets = 0;
        }

        /// <summary>Seed once per load using the same math as BuildingsAccessor/MapDataService region.</summary>
        private void SeedFromService()
        {
            ClearCache();

            var svc = MapDataService.Instance;
            if (!svc.IsLoaded) { Debug.WriteLine("[Buildings] Seed: no map loaded."); return; }

            // Use cached region (preferred, includes strict finder fallback)
            svc.ComputeAndCacheBuildingRegion();
            if (!svc.TryGetBuildingRegion(out _bStart, out _bLen))
            {
                Debug.WriteLine("[Buildings] Seed: building region unavailable.");
                return;
            }

            _cachedBytes = svc.GetBytesCopy();

            int hdr = _bStart;
            if (_cachedBytes.Length < hdr + HeaderSize)
            {
                Debug.WriteLine("[Buildings] Seed: block too short for header.");
                ClearCache();
                return;
            }

            // Same counters used elsewhere
            ushort nextBuildings = ReadU16(_cachedBytes, hdr + 2);
            ushort nextFacets = ReadU16(_cachedBytes, hdr + 4);
            _totalBuildings = Math.Max(0, nextBuildings - 1);
            _totalFacets = Math.Max(0, nextFacets - 1);

            _facetsOffsetAbs = hdr + HeaderSize + _totalBuildings * DBuildingSize + AfterBuildingsPad;

            long facetsEnd = (long)_facetsOffsetAbs + (long)_totalFacets * DFacetSize;
            if (_facetsOffsetAbs < _bStart || facetsEnd > (_bStart + _bLen))
            {
                Debug.WriteLine($"[Buildings] Seed: bad facet bounds. " +
                                $"bStart=0x{_bStart:X} len={_bLen} facetsOff=0x{_facetsOffsetAbs:X} " +
                                $"count={_totalFacets} end=0x{facetsEnd:X}");
                ClearCache();
                return;
            }

            Debug.WriteLine($"[Buildings] Seed OK. region=[0x{_bStart:X}..0x{_bStart + _bLen:X}) " +
                            $"buildings={_totalBuildings} facets={_totalFacets} facetsOff=0x{_facetsOffsetAbs:X}");
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            if (_cachedBytes is null)
            {
                SeedFromService();
                if (_cachedBytes is null) return;
            }
            if (_facetsOffsetAbs < 0 || _totalFacets <= 0) return;

            // ---------- Normal pass (all facets) ----------
            int drawn = 0;
            for (int i = 0; i < _totalFacets; i++)
            {
                int off = _facetsOffsetAbs + i * DFacetSize;
                if (off + DFacetSize > _cachedBytes.Length) break;

                byte facetType = _cachedBytes[off + 0];
                byte x0 = _cachedBytes[off + 2];
                byte x1 = _cachedBytes[off + 3];
                byte z0 = _cachedBytes[off + 8];
                byte z1 = _cachedBytes[off + 9];
                if ((x0 | x1 | z0 | z1) == 0) continue;

                var p1 = new Point((128 - x0) * 64.0, (128 - z0) * 64.0);
                var p2 = new Point((128 - x1) * 64.0, (128 - z1) * 64.0);
                dc.DrawLine(GetPenForFacet(facetType), p1, p2);
                drawn++;
            }

            // ---------- Highlight pass (selected building/storey/facet from VM) ----------
            if (_selBuildingId > 0)
            {
                for (int i = 0; i < _totalFacets; i++)
                {
                    int off = _facetsOffsetAbs + i * DFacetSize;
                    if (off + DFacetSize > _cachedBytes.Length) break;

                    // Single facet selected? Facet ids are 1-based -> (i + 1)
                    if (_selFacetId.HasValue && (i + 1) != _selFacetId.Value)
                        continue;

                    if (!FacetMatchesSelection(_cachedBytes, off, _selBuildingId, _selStoreyId))
                        continue;

                    byte x0 = _cachedBytes[off + 2];
                    byte x1 = _cachedBytes[off + 3];
                    byte z0 = _cachedBytes[off + 8];
                    byte z1 = _cachedBytes[off + 9];
                    if ((x0 | x1 | z0 | z1) == 0) continue;

                    var p1 = new Point((128 - x0) * 64.0, (128 - z0) * 64.0);
                    var p2 = new Point((128 - x1) * 64.0, (128 - z1) * 64.0);

                    // glow stack
                    dc.DrawLine(_glowPenWide, p1, p2);
                    dc.DrawLine(_glowPenNarrow, p1, p2);
                    dc.DrawLine(_edgePen, p1, p2);
                }
            }

            Debug.WriteLine($"[Buildings] Render drew {drawn} facet segments.");
        }

        private static bool FacetMatchesSelection(byte[] bytes, int facetAbsOffset, int selBuildingId, int? selStoreyId)
        {
            // SAME offsets as parser/accessor: +14 = Building (u16, 1-based), +16 = Storey (u16, 1-based)
            ushort buildingId = ReadU16(bytes, facetAbsOffset + 14);
            if (buildingId != (ushort)selBuildingId) return false;

            if (selStoreyId.HasValue)
            {
                ushort storeyId = ReadU16(bytes, facetAbsOffset + 16);
                return storeyId == (ushort)selStoreyId.Value;
            }
            return true;
        }

        private static ushort ReadU16(byte[] b, int off)
            => (ushort)(b[off + 0] | (b[off + 1] << 8));

        private static Pen GetPenForFacet(byte type) => type switch
        {
            3 => PenWallGreen,                 // Wall
            9 => PenCableRed,                  // Cable
            10 or 11 or 13 => PenFenceYellow,   // Fences
            12 => PenLadderOrange,              // Ladder
            18 or 19 or 21 => PenDoorPurple,    // Doors
            2 or 4 => PenRoofBlue,              // Roof / RoofQuad
            15 or 16 or 17 => PenInsideGray,    // JustCollision / Partition / Inside
            _ => PenDefault
        };
    }
}
