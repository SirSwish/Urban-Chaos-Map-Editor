// /ViewModels/BuildingsTabViewModel.cs
// BuildingsTabViewModel.cs  (updated)
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.Services;
using UrbanChaosMapEditor.Services.DataServices;
using static UrbanChaosMapEditor.ViewModels.BuildingsTabViewModel;

namespace UrbanChaosMapEditor.ViewModels
{
    public sealed class BuildingsTabViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<BuildingVM> Buildings { get; } = new();

        private FacetVM? _selectedFacet;
        public FacetVM? SelectedFacet { get => _selectedFacet; set { _selectedFacet = value; OnPropertyChanged(); } }

        private int _selectedBuildingId;
        public int SelectedBuildingId { get => _selectedBuildingId; set { _selectedBuildingId = value; OnPropertyChanged(); } }

        private int _selectedStoreyId;
        public int SelectedStoreyId { get => _selectedStoreyId; set { _selectedStoreyId = value; OnPropertyChanged(); } }

        private string _filterText = string.Empty;
        public string FilterText { get => _filterText; set { _filterText = value; OnPropertyChanged(); ApplyFilter(); } }

        public ObservableCollection<StyleVM> Styles { get; } = new();
        public int StylesCount { get; private set; }
        public int PaintMemCount { get; private set; }
        public string PaintMemPreview { get; private set; } = string.Empty;

        public int DStoreysNext { get; private set; }          // raw “next”
        public int DStoreysTotal => Math.Max(0, DStoreysNext - 1);
        private short[] _lastDstyles = Array.Empty<short>();

        // ---- style.tma (world styles) ----
        public ObservableCollection<TmaStyleVM> TmaStyles { get; } = new();
        public int TmaStylesCount => TmaStyles.Count;

        private short[] _styles = Array.Empty<short>();
        private byte[] _paintMem = Array.Empty<byte>();
        private readonly StylesAccessor _stylesAcc = new(Services.DataServices.StyleDataService.Instance);

        public IReadOnlyList<string> FacetTypeOptions { get; } =
            new[] { "All", "Normal", "Roof", "Wall", "RoofQuad", "FloorPoints", "FireEscape",
                    "Staircase", "Skylight", "Cable", "Fence", "FenceBrick", "Ladder",
                    "FenceFlat", "Trench", "JustCollision", "Partition", "Inside", "Door",
                    "InsideDoor", "OInside", "OutsideDoor", "NormalFoundation" };

        private string _selectedFacetType = "All";
        public string SelectedFacetType { get => _selectedFacetType; set { _selectedFacetType = value; OnPropertyChanged(); ApplyFilter(); } }

        public ICommand RefreshCommand => _refreshCommand ??= new RelayCommand(_ => Refresh());
        private RelayCommand? _refreshCommand;

        private List<BuildingVM> _allBuildings = new();

        public BuildingsTabViewModel()
        {
            // refresh style captions when styles file loads/changes
            Services.DataServices.StyleDataService.Instance.StylesBytesReset += (_, __) =>
            {
                // Update all currently-built rows. For cables, use the cable-aware formatter.
                foreach (var b in Buildings)
                    foreach (var s in b.Storeys)
                        foreach (var g in s.Groups)
                            foreach (var f in g.Facets)
                                f.StyleDisplay = StyleDisplayForVM(f);

                OnPropertyChanged(nameof(Buildings));
                OnPropertyChanged(nameof(SelectedFacet));

                // Update the header whenever items are added/removed.
                TmaStyles.CollectionChanged += (_, __2) => OnPropertyChanged(nameof(TmaStylesCount));

                // TEMP seed so you can see rows immediately; safe to remove later.
                if (TmaStyles.Count == 0)
                    SeedWorldStylesDemo();
            };

            // keep header in sync with collection changes
            TmaStyles.CollectionChanged += (_, __) => OnPropertyChanged(nameof(TmaStylesCount));

            // TEMP: prove the UI works. Replace with your real loader.
            if (TmaStyles.Count == 0)
            {
                var demo = new TmaStyleVM
                {
                    Index = 0,
                    Name = "Demo style",
                    FlagsSummary = "Opaque, NoAlpha"
                };
                demo.Entries.Add(new TmaEntryVM { Page = 1, Tx = 2, Ty = 3, FlipDisplay = "-" });
                TmaStyles.Add(demo);
            }

            // TODO: call your real loader when a map is opened/loaded
            LoadWorldStyles();
        }

        private void SeedWorldStylesDemo()
        {
            var demo = new TmaStyleVM
            {
                Index = 0,
                Name = "Demo style",
                FlagsSummary = "Opaque, NoAlpha"
            };
            demo.Entries.Add(new TmaEntryVM { Page = 1, Tx = 2, Ty = 3, FlipDisplay = "-" });
            TmaStyles.Add(demo);
        }

        private void LoadWorldStyles()
        {
            TmaStyles.Clear();
            SeedWorldStylesDemo(); // replace with real loader
        }

        // ---- helpers for signed reinterpret ----
        private static short AsS16(ushort v) => unchecked((short)v);

        // Cable-aware formatter for *existing* VM rows (no raw facet record handy here).
        private string StyleDisplayForVM(FacetVM f)
        {
            if (f.Type == FacetType.Cable)
                return $"Cable: step1={f.CableStep1}, step2={f.CableStep2}, mode={f.FHeight}";
            return StyleDisplayFor(_lastDstyles, f.StyleIndex, DStoreysNext);
        }

        private string StyleDisplayFor(ushort facetStyleIndex)
        {
            if (_lastDstyles is null || facetStyleIndex >= _lastDstyles.Length)
                return $"{facetStyleIndex}";

            short dstyleValue = _lastDstyles[facetStyleIndex];
            if (dstyleValue >= 0)
                return _stylesAcc.Describe(dstyleValue);
            else
                return $"painted (storey={-dstyleValue})";
        }

        // Formats a single entry from the signed dstyles[] table.
        private string StyleDisplayFor(short[] stylesTable, ushort styleIndex, int nextDStorey)
        {
            if (stylesTable == null || stylesTable.Length == 0) return $"{styleIndex}";
            if (styleIndex < 0 || styleIndex >= stylesTable.Length) return $"{styleIndex}";

            short entry = stylesTable[styleIndex];

            if (entry >= 0)
            {
                return $"{styleIndex}  (raw=0x{((ushort)entry):X4})";
            }
            else
            {
                int dstoreyIdx = -entry;
                return $"{styleIndex}  (painted → DStorey {dstoreyIdx})";
            }
        }

        public void Refresh()
        {
            Debug.WriteLine("=== [BuildingsTabVM] Refresh() ===");
            Buildings.Clear();
            _allBuildings.Clear();
            SelectedFacet = null;
            SelectedBuildingId = 0;
            SelectedStoreyId = 0;

            var arrays = new BuildingsAccessor(MapDataService.Instance).ReadSnapshot();

            // Log the raw counters/region so we know what we got back.
            Debug.WriteLine($"[BuildingsTabVM] region: start=0x{arrays.StartOffset:X} len=0x{arrays.Length:X}");
            Debug.WriteLine($"[BuildingsTabVM] counters: nextDBuilding={arrays.NextDBuilding} nextDFacet={arrays.NextDFacet} nextDStyle={arrays.NextDStyle} nextPaintMem={arrays.NextPaintMem} nextDStorey={arrays.NextDStorey} saveType={arrays.SaveType}");
            Debug.WriteLine($"[BuildingsTabVM] arrays: Buildings={arrays.Buildings.Length} Facets={arrays.Facets.Length} Styles={arrays.Styles.Length} PaintMem={arrays.PaintMem.Length} Storeys={arrays.Storeys.Length}");

            _lastDstyles = arrays.Styles ?? Array.Empty<short>();

            if (arrays.Facets.Length == 0 && arrays.Buildings.Length == 0)
            {
                Debug.WriteLine("[BuildingsTabVM] Nothing to show: no facets and no buildings.");
                Styles.Clear();
                StylesCount = 0;
                PaintMemCount = 0;
                PaintMemPreview = string.Empty;
                DStoreysNext = 0;

                OnPropertyChanged(nameof(StylesCount));
                OnPropertyChanged(nameof(PaintMemCount));
                OnPropertyChanged(nameof(PaintMemPreview));
                OnPropertyChanged(nameof(DStoreysNext));
                OnPropertyChanged(nameof(DStoreysTotal));
                Debug.WriteLine("=== [BuildingsTabVM] Refresh() done (empty) ===");
                return;
            }

            // Pre-index facets with their real 1-based ID from file order.
            var facetsIndexed = arrays.Facets.Select((f, i) => (Facet: f, Id1: i + 1)).ToArray();

            _styles = arrays.Styles ?? Array.Empty<short>();
            _paintMem = arrays.PaintMem ?? Array.Empty<byte>();

            Debug.WriteLine($"[BuildingsTabVM] facets: total={facetsIndexed.Length}");

            if (facetsIndexed.Length > 0)
            {
                var f0 = facetsIndexed[0].Facet;
                Debug.WriteLine($"[BuildingsTabVM] sample facet[1]: type={f0.Type} bld={f0.Building} st={f0.Storey} styleIdx={f0.StyleIndex} xy=({f0.X0},{f0.Z0})->({f0.X1},{f0.Z1})");
            }

            // -------- Split cables from everything else --------
            var nonCable = facetsIndexed.Where(t => t.Facet.Type != FacetType.Cable).ToArray();
            var cables = facetsIndexed.Where(t => t.Facet.Type == FacetType.Cable)
                                        .OrderBy(t => t.Id1)
                                        .ToList();

            Debug.WriteLine($"[BuildingsTabVM] nonCable={nonCable.Length} cables={cables.Count}");

            // -------- Build normal buildings (no cables) --------
            var buildingIds = nonCable
                .Select(t => (int)t.Facet.Building)
                .Where(id => id > 0)
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            Debug.WriteLine($"[BuildingsTabVM] distinct building ids (no cables)={buildingIds.Count} :: {string.Join(",", buildingIds.Take(16))}{(buildingIds.Count > 16 ? ",..." : "")}");

            foreach (int bId in buildingIds)
            {
                var bFacets = nonCable.Where(t => t.Facet.Building == bId).ToList();
                if (bFacets.Count == 0) continue;

                var bvm = new BuildingVM { Id = bId };

                foreach (var storeyGroup in bFacets.GroupBy(t => t.Facet.Storey).OrderBy(g => g.Key))
                {
                    var svm = new StoreyVM { StoreyId = storeyGroup.Key };

                    foreach (var typeGroup in storeyGroup.GroupBy(t => t.Facet.Type).OrderBy(g => g.Key))
                    {
                        var gvm = new FacetTypeGroupVM
                        {
                            Type = typeGroup.Key,
                            TypeName = FacetTypeName(typeGroup.Key)
                        };

                        foreach (var t in typeGroup.OrderBy(tt => tt.Id1))
                        {
                            var f = t.Facet;
                            gvm.Facets.Add(new FacetVM
                            {
                                FacetId1 = t.Id1,
                                Type = f.Type,
                                StyleIndex = f.StyleIndex,
                                StyleDisplay = StyleDisplayFor(_styles, f.StyleIndex, DStoreysNext),
                                Coords = $"({f.X0},{f.Z0}) → ({f.X1},{f.Z1})",
                                Height = f.Height,
                                FHeight = f.FHeight,
                                Flags = $"0x{((ushort)f.Flags):X4}",
                                BuildingId = bId,
                                StoreyId = f.Storey,
                                Raw = f     ,            // <= NEW
                                Y0 = f.Y0,
                                Y1 = f.Y1,
                                BlockHeight = f.BlockHeight,
                                Open = f.Open
                            });
                        }

                        gvm.Count = gvm.Facets.Count;
                        if (gvm.Count > 0)
                            svm.Groups.Add(gvm);
                    }

                    svm.UsageCount = svm.Groups.Sum(x => x.Count);
                    if (svm.UsageCount > 0)
                        bvm.Storeys.Add(svm);
                }

                bvm.FacetCount = bvm.Storeys.SelectMany(st => st.Groups).Sum(g => g.Count);
                if (bvm.FacetCount > 0)
                    _allBuildings.Add(bvm);
            }

            // -------- Add a single “Cables” bucket --------
            if (cables.Count > 0)
            {
                var cablesB = new BuildingVM { Id = 0, IsCablesBucket = true };
                var svm = new StoreyVM { StoreyId = 0 };
                var gvm = new FacetTypeGroupVM { Type = FacetType.Cable, TypeName = "Cable" };

                foreach (var t in cables)
                {
                    var f = t.Facet;
                    gvm.Facets.Add(new FacetVM
                    {
                        FacetId1 = t.Id1,
                        Type = FacetType.Cable,
                        StyleIndex = f.StyleIndex,
                        StyleDisplay = CableStyleDisplay(f),           // special display for cables
                        Coords = $"({f.X0},{f.Z0}) → ({f.X1},{f.Z1})",
                        Height = f.Height,
                        FHeight = f.FHeight,
                        Flags = $"0x{((ushort)f.Flags):X4}",
                        BuildingId = 0,  // not a real building for cables
                        StoreyId = 0,
                        Y0 = f.Y0,
                        Y1 = f.Y1,
                        BlockHeight = f.BlockHeight,
                        Open = f.Open,
                        Raw = f                 // <= NEW
                    });
                }

                gvm.Count = gvm.Facets.Count;
                svm.UsageCount = gvm.Count;
                svm.Groups.Add(gvm);
                cablesB.Storeys.Add(svm);
                cablesB.FacetCount = gvm.Count;

                // Add at end; use Insert(0, cablesB) if you want it first.
                _allBuildings.Add(cablesB);
                Debug.WriteLine($"[BuildingsTabVM] cables bucket added: {cablesB.FacetCount} facets");
            }

            Debug.WriteLine($"[BuildingsTabVM] built _allBuildings={_allBuildings.Count} (total facets={_allBuildings.Sum(b => b.FacetCount)})");

            // -------- Global data cards --------
            Styles.Clear();
            StylesCount = 0;
            PaintMemCount = 0;
            PaintMemPreview = string.Empty;
            DStoreysNext = arrays.NextDStorey;

            TmaStyles.Clear();
            OnPropertyChanged(nameof(TmaStylesCount));

            if (arrays.Styles is { Length: > 0 })
            {
                for (int i = 0; i < arrays.Styles.Length; i++)
                    Styles.Add(new StyleVM { Index = i, Value = arrays.Styles[i] });
                StylesCount = arrays.Styles.Length;
            }

            if (arrays.PaintMem is { Length: > 0 })
            {
                PaintMemCount = arrays.PaintMem.Length;

                var take = Math.Min(128, arrays.PaintMem.Length);
                var sb = new StringBuilder(take * 3);
                for (int i = 0; i < take; i++)
                {
                    sb.Append(arrays.PaintMem[i].ToString("X2"));
                    if (i != take - 1) sb.Append(' ');
                }
                PaintMemPreview = sb.ToString();
            }

            // -------- Apply current filter --------
            Debug.WriteLine($"[BuildingsTabVM] applying filter: type='{SelectedFacetType}', text='{FilterText}'");
            ApplyFilter();
            Debug.WriteLine($"[BuildingsTabVM] post-filter Buildings.Count={Buildings.Count}");

            // Safety: if a filter hid everything but we *do* have data, show all and log it.
            if (Buildings.Count == 0 && _allBuildings.Count > 0 &&
                (!string.Equals(SelectedFacetType, "All", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(FilterText)))
            {
                Debug.WriteLine("[BuildingsTabVM] Filter removed all rows; temporarily clearing filters to show data.");
                var oldType = SelectedFacetType;
                var oldText = FilterText;
                _selectedFacetType = "All"; // bypass setter to avoid re-entrant filter call
                _filterText = string.Empty;
                ApplyFilter();
                Debug.WriteLine($"[BuildingsTabVM] after safety-clear: Buildings.Count={Buildings.Count} (was 0). Previous filter type='{oldType}' text='{oldText}'.");
                OnPropertyChanged(nameof(SelectedFacetType));
                OnPropertyChanged(nameof(FilterText));
            }

            OnPropertyChanged(nameof(StylesCount));
            OnPropertyChanged(nameof(PaintMemCount));
            OnPropertyChanged(nameof(PaintMemPreview));
            OnPropertyChanged(nameof(DStoreysNext));
            OnPropertyChanged(nameof(DStoreysTotal));

            var totalFacetsView = Buildings.SelectMany(b => b.Storeys).SelectMany(s => s.Groups).Sum(g => g.Count);
            Debug.WriteLine($"[BuildingsTabVM] AFTER POPULATE → Buildings.Count={Buildings.Count}, facets(view)={totalFacetsView}");
            Debug.WriteLine("=== [BuildingsTabVM] Refresh() done ===");
        }


        public void HandleTreeSelection(object? selection)
        {
            var shell = System.Windows.Application.Current.MainWindow?.DataContext as MainWindowViewModel;
            var map = shell?.Map;
            if (map is null) return;

            switch (selection)
            {
                case FacetVM f:
                    SelectedFacet = f;
                    map.SelectedBuildingId = f.BuildingId;
                    map.SelectedStoreyId = f.StoreyId;
                    map.SelectedFacetId = f.FacetId1;   // facet-only highlight
                    break;

                case FacetTypeGroupVM g when g.Facets.Count > 0:
                    SelectedFacet = g.Facets[0];
                    map.SelectedBuildingId = SelectedFacet.BuildingId;
                    map.SelectedStoreyId = SelectedFacet.StoreyId;
                    map.SelectedFacetId = null;         // whole storey, not a single facet
                    break;

                case StoreyVM s:
                    SelectedFacet = s.Groups.SelectMany(x => x.Facets).FirstOrDefault();
                    map.SelectedBuildingId = SelectedFacet?.BuildingId ?? 0;
                    map.SelectedStoreyId = s.StoreyId;
                    map.SelectedFacetId = null;         // whole storey highlight
                    break;

                case BuildingVM b:
                    SelectedFacet = b.Storeys
                                     .SelectMany(st => st.Groups)
                                     .SelectMany(x => x.Facets)
                                     .FirstOrDefault();
                    map.SelectedBuildingId = b.Id;
                    map.SelectedStoreyId = SelectedFacet?.StoreyId ?? 0;
                    map.SelectedFacetId = null;         // whole building highlight (storey if available)
                    break;

                default:
                    SelectedFacet = null;
                    map.SelectedBuildingId = 0;
                    map.SelectedStoreyId = null;
                    map.SelectedFacetId = null;
                    break;
            }
        }

        private void ApplyFilter()
        {
            Buildings.Clear();

            IEnumerable<BuildingVM> src = _allBuildings;

            if (!string.Equals(SelectedFacetType, "All", StringComparison.OrdinalIgnoreCase))
            {
                FacetType? typeCode = FacetTypeCode(SelectedFacetType);
                Debug.WriteLine($"[BuildingsTabVM] ApplyFilter: facet type='{SelectedFacetType}' → code={(typeCode.HasValue ? typeCode.ToString() : "null")}");
                if (typeCode.HasValue)
                {
                    src = src.Select(b =>
                    {
                        var nb = new BuildingVM { Id = b.Id };
                        foreach (var s in b.Storeys)
                        {
                            var ns = new StoreyVM { StoreyId = s.StoreyId };
                            foreach (var g in s.Groups.Where(g => g.Type == typeCode.Value))
                                ns.Groups.Add(g);
                            if (ns.Groups.Count > 0)
                            {
                                ns.UsageCount = ns.Groups.Sum(x => x.Count);
                                nb.Storeys.Add(ns);
                            }
                        }
                        nb.FacetCount = nb.Storeys.SelectMany(st => st.Groups).Sum(g => g.Count);
                        return nb;
                    })
                    .Where(b => b.FacetCount > 0)
                    .ToList();
                }
            }

            if (!string.IsNullOrWhiteSpace(FilterText))
            {
                string q = FilterText.Trim().ToLowerInvariant();
                Debug.WriteLine($"[BuildingsTabVM] ApplyFilter: text='{q}'");
                src = src.Where(b =>
                    b.Id.ToString().Contains(q) ||
                    b.Storeys.Any(st =>
                        st.StoreyId.ToString().Contains(q) ||
                        st.Groups.Any(g =>
                            g.TypeName.ToLowerInvariant().Contains(q) ||
                            g.Facets.Any(f =>
                                f.FacetId1.ToString().Contains(q) ||
                                f.StyleIndex.ToString().Contains(q) ||
                                f.Coords.ToLowerInvariant().Contains(q)))));
            }

            int added = 0;
            foreach (var b in src)
            {
                Buildings.Add(b);
                added++;
            }
            Debug.WriteLine($"[BuildingsTabVM] ApplyFilter: result buildings={added}");
        }


        private static string FacetTypeName(FacetType code) => code switch
        {
            FacetType.Normal => "Normal",
            FacetType.Roof => "Roof",
            FacetType.Wall => "Wall",
            FacetType.RoofQuad => "RoofQuad",
            FacetType.FloorPoints => "FloorPoints",
            FacetType.FireEscape => "FireEscape",
            FacetType.Staircase => "Staircase",
            FacetType.Skylight => "Skylight",
            FacetType.Cable => "Cable",
            FacetType.Fence => "Fence",
            FacetType.FenceBrick => "FenceBrick",
            FacetType.Ladder => "Ladder",
            FacetType.FenceFlat => "FenceFlat",
            FacetType.Trench => "Trench",
            FacetType.JustCollision => "JustCollision",
            FacetType.Partition => "Partition",
            FacetType.Inside => "Inside",
            FacetType.Door => "Door",
            FacetType.InsideDoor => "InsideDoor",
            FacetType.OInside => "OInside",
            FacetType.OutsideDoor => "OutsideDoor",
            (FacetType)100 => "NormalFoundation",
            _ => $"Type{(byte)code}"
        };

        private static FacetType? FacetTypeCode(string name) => name switch
        {
            "Normal" => FacetType.Normal,
            "Roof" => FacetType.Roof,
            "Wall" => FacetType.Wall,
            "RoofQuad" => FacetType.RoofQuad,
            "FloorPoints" => FacetType.FloorPoints,
            "FireEscape" => FacetType.FireEscape,
            "Staircase" => FacetType.Staircase,
            "Skylight" => FacetType.Skylight,
            "Cable" => FacetType.Cable,
            "Fence" => FacetType.Fence,
            "FenceBrick" => FacetType.FenceBrick,
            "Ladder" => FacetType.Ladder,
            "FenceFlat" => FacetType.FenceFlat,
            "Trench" => FacetType.Trench,
            "JustCollision" => FacetType.JustCollision,
            "Partition" => FacetType.Partition,
            "Inside" => FacetType.Inside,
            "Door" => FacetType.Door,
            "InsideDoor" => FacetType.InsideDoor,
            "OInside" => FacetType.OInside,
            "OutsideDoor" => FacetType.OutsideDoor,
            "NormalFoundation" => (FacetType)100,
            _ => null
        };

        private static string CableStyleDisplay(DFacetRec f)
        {
            // Cable fields are repurposed:
            // StyleIndex  -> step_angle1 (SWORD)
            // Building    -> step_angle2 (SWORD)  <-- not a real building id for cables!
            // Height      -> count; mode encoded in low bits (game sets flags differently)
            short step1 = unchecked((short)f.StyleIndex);
            short step2 = unchecked((short)f.Building);
            int mode = f.Height & 0x3;
            return $"Cable: step1={step1}, step2={step2}, mode={mode}";
        }

        // ----- inner types, INotify, RelayCommand -----
        private sealed class FacetRec
        {
            public int Index1;
            public byte Type;
            public byte X0, Z0, X1, Z1;
            public short Y0, Y1;
            public ushort Flags;
            public ushort Style;
            public ushort BuildingId;
            public ushort StoreyId;
            public byte Height;
            public byte FHeight;
            public string FlagsText => $"0x{Flags:X4}";
        }

        public sealed class BuildingVM
        {
            public int Id { get; set; }
            public int FacetCount { get; set; }
            public bool IsPhantom { get; set; }      // existing optional flag
            public bool IsCablesBucket { get; set; } // NEW

            // What the tree shows
            public string DisplayName =>
                IsCablesBucket ? "Cables"
                : (IsPhantom ? $"B#{Id} (phantom)" : $"B#{Id}");

            public ObservableCollection<StoreyVM> Storeys { get; } = new();
        }
        public sealed class StoreyVM
        {
            public int StoreyId { get; set; }
            public int UsageCount { get; set; }
            public ObservableCollection<FacetTypeGroupVM> Groups { get; } = new();
            public StoreyVM() { }
            public StoreyVM(int id) { StoreyId = id; }
        }
        public sealed class FacetTypeGroupVM
        {
            public FacetType Type { get; set; }
            public string TypeName { get; set; } = "";
            public int Count { get; set; }
            public List<FacetVM> Facets { get; set; } = new();

            public FacetTypeGroupVM() { }
            public FacetTypeGroupVM(FacetType type)
            {
                Type = type;
                TypeName = type.ToString();
            }
        }
        public sealed class FacetVM
        {
            public int FacetId1 { get; set; }
            public FacetType Type { get; set; }
            public ushort StyleIndex { get; set; }
            public string StyleDisplay { get; set; } = "";   // shown in tree + details
            public string Coords { get; set; } = "";
            public byte Height { get; set; }
            public byte FHeight { get; set; }
            public string Flags { get; set; } = "";
            public int BuildingId { get; set; }  // DBuilding id (1-based)
            public int StoreyId { get; set; }
            public short Y0 { get; set; }
            public short Y1 { get; set; }
            public byte BlockHeight { get; set; }
            public byte Open { get; set; }

            // NEW: keep raw cable angles so we can reformat on style reloads
            public short CableStep1 { get; set; } // from StyleIndex (as S16)
            public short CableStep2 { get; set; } // from raw facet.Building (as S16)

            public FacetVM() { }
            public FacetVM(int id1, DFacetRec f)
            {
                FacetId1 = id1;
                Type = f.Type;
                StyleIndex = f.StyleIndex;
                StyleDisplay = $"{f.StyleIndex} (0x{f.StyleIndex:X4})";
                Coords = $"({f.X0},{f.Z0}) → ({f.X1},{f.Z1})";
                Height = f.Height;
                FHeight = f.FHeight;
                Flags = $"0x{((ushort)f.Flags):X4}";
                BuildingId = f.Building;
                StoreyId = f.Storey;
            }

            public DFacetRec Raw { get; set; }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private sealed class RelayCommand : ICommand
        {
            private readonly Action<object?> _exec;
            private readonly Func<object?, bool>? _can;
            public RelayCommand(Action<object?> exec, Func<object?, bool>? can = null) { _exec = exec; _can = can; }
            public bool CanExecute(object? p) => _can?.Invoke(p) ?? true;
            public void Execute(object? p) => _exec(p);
            public event EventHandler? CanExecuteChanged { add { } remove { } }
        }

        public sealed class StyleVM
        {
            public int Index { get; init; }      // 0-based index in dstyles
            public short Value { get; init; }    // signed entry
            public string Display
                => Value >= 0
                   ? $"[{Index}] raw=0x{((ushort)Value):X4}"
                   : $"[{Index}] painted → DStorey {-Value}";
        }
        public sealed class TmaStyleVM
        {
            public int Index { get; set; }                  // 0-based or 1-based, your choice when you fill it
            public string Name { get; set; } = "";
            public string FlagsSummary { get; set; } = "";  // e.g., "Textured|TwoSided"
            public ObservableCollection<TmaEntryVM> Entries { get; } = new();
        }

        public sealed class TmaEntryVM
        {
            public byte Page { get; set; }
            public byte Tx { get; set; }
            public byte Ty { get; set; }
            public string FlipDisplay { get; set; } = "";   // e.g., "-", "X", "Y", "XY"
        }
    }
}
