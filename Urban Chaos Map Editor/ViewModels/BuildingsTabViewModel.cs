using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.Services;

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
        private ushort[] _styles = Array.Empty<ushort>();
        private byte[] _paintMem = Array.Empty<byte>();


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

        public void Refresh()
        {
            Debug.WriteLine("=== [BuildingsTabVM] Refresh() ===");
            Buildings.Clear();
            _allBuildings.Clear();
            SelectedFacet = null;
            SelectedBuildingId = 0;
            SelectedStoreyId = 0;

            var arrays = new BuildingsAccessor(MapDataService.Instance).ReadSnapshot();
            // guard if needed:
            if (arrays.Facets.Length == 0 && arrays.Buildings.Length == 0)
            {
                // nothing to show
                return;
            }

            if (arrays is null)
            {
                Debug.WriteLine("[BuildingsTabVM] BuildingParser.TryParseFromService returned null.");
                return;
            }

            // Pre-index facets with their real 1-based ID from file order.
            var facetsIndexed = arrays.Facets
                .Select((f, i) => (Facet: f, Id1: i + 1))
                .ToArray();

            _styles = arrays.Styles ?? Array.Empty<ushort>();
            _paintMem = arrays.PaintMem ?? Array.Empty<byte>();

            Debug.WriteLine($"[BuildingsTabVM] facets.Count={facetsIndexed.Length}");

            // IMPORTANT: group by the facet’s Building id (this matches the renderer)
            var buildingIds = facetsIndexed
                .Select(t => (int)t.Facet.Building)
                .Where(id => id > 0)
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            foreach (int bId in buildingIds)
            {
                var bFacets = facetsIndexed.Where(t => t.Facet.Building == bId).ToList();
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

                        // Stable ordering; using facet ID makes it deterministic.
                        foreach (var t in typeGroup.OrderBy(tt => tt.Id1))
                        {
                            var f = t.Facet;
                            gvm.Facets.Add(new FacetVM
                            {
                                FacetId1 = t.Id1,               // ← real 1-based facet id
                                Type = f.Type,              // FacetType (enum) in your VM
                                StyleIndex = f.StyleIndex,
                                Coords = $"({f.X0},{f.Z0}) → ({f.X1},{f.Z1})",
                                Height = f.Height,
                                FHeight = f.FHeight,
                                Flags = $"0x{((ushort)f.Flags):X4}",
                                BuildingId = bId,
                                StoreyId = f.Storey
                            });
                        }

                        gvm.Count = gvm.Facets.Count;
                        svm.Groups.Add(gvm);
                    }

                    svm.UsageCount = svm.Groups.Sum(x => x.Count);
                    bvm.Storeys.Add(svm);
                }

                bvm.FacetCount = bvm.Storeys.SelectMany(st => st.Groups).Sum(g => g.Count);
                _allBuildings.Add(bvm);
            }

            Styles.Clear();
            StylesCount = 0;
            PaintMemCount = 0;
            PaintMemPreview = string.Empty;
            DStoreysNext = 0;

            if (arrays.Styles is { Length: > 0 })
            {
                int i = 1;
                foreach (var s in arrays.Styles)
                    Styles.Add(new StyleVM { Index = i++, Value = s });
                StylesCount = arrays.Styles.Length;
            }

            if (arrays.PaintMem is { Length: > 0 })
            {
                PaintMemCount = arrays.PaintMem.Length;

                // Show a short hex preview (first 128 bytes)
                var take = Math.Min(128, arrays.PaintMem.Length);
                var sb = new StringBuilder(take * 3);
                for (int i = 0; i < take; i++)
                {
                    sb.Append(arrays.PaintMem[i].ToString("X2"));
                    if (i != take - 1) sb.Append(' ');
                }
                PaintMemPreview = sb.ToString();
            }

            DStoreysNext = arrays.NextDStorey;

            ApplyFilter();

            OnPropertyChanged(nameof(StylesCount));
            OnPropertyChanged(nameof(PaintMemCount));
            OnPropertyChanged(nameof(PaintMemPreview));
            OnPropertyChanged(nameof(DStoreysNext));
            OnPropertyChanged(nameof(DStoreysTotal));

            var totalFacetsView = Buildings.SelectMany(b => b.Storeys).SelectMany(s => s.Groups).Sum(g => g.Count);
            Debug.WriteLine($"[BuildingsTabVM] AFTER POPULATE → Buildings.Count={Buildings.Count}, facets(view)={totalFacetsView}");
            Debug.WriteLine("=== [BuildingsTabVM] Refresh() done ===");
        }

        private string StyleDisplay(ushort styleIndex)
        {
            if (_styles != null && styleIndex >= 0 && styleIndex < _styles.Length)
                return $"{styleIndex} (0x{_styles[styleIndex]:X4})";
            return $"{styleIndex}";
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

            foreach (var b in src)
                Buildings.Add(b);
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
            public bool IsPhantom { get; set; }   // NEW
            public string DisplayName => IsPhantom ? $"B#{Id} (phantom)" : $"B#{Id}"; // NEW

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
                TypeName = type.ToString(); // names match your enum
            }
        }
        public sealed class FacetVM
        {
            public int FacetId1 { get; set; }
            public FacetType Type { get; set; }
            public ushort StyleIndex { get; set; }
            public string StyleDisplay { get; set; } = "";   // NEW
            public string Coords { get; set; } = "";
            public byte Height { get; set; }
            public byte FHeight { get; set; }
            public string Flags { get; set; } = "";
            public int BuildingId { get; set; }
            public int StoreyId { get; set; }
            public short Y0 { get; set; }
            public short Y1 { get; set; }
            public byte BlockHeight { get; set; }
            public byte Open { get; set; }

            public FacetVM() { }
            public FacetVM(int id1, DFacetRec f)
            {
                FacetId1 = id1;
                Type = f.Type;
                StyleIndex = f.StyleIndex;
                Coords = $"({f.X0},{f.Z0}) → ({f.X1},{f.Z1})";
                Height = f.Height;
                FHeight = f.FHeight;
                Flags = $"0x{((ushort)f.Flags):X4}";
                BuildingId = f.Building;
                StoreyId = f.Storey;
            }
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
            public int Index { get; init; }         // 1-based index
            public ushort Value { get; init; }      // raw UWORD style id
            public string Display => $"[{Index}] 0x{Value:X4}";
        }
    }

    public sealed class FacetTypeToBrushConverter : IMultiValueConverter
    {
        public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values is not { Length: >= 2 }) return Brushes.LightGray;

            byte type = values[0] switch
            {
                byte b => b,
                int i => (byte)i,
                UrbanChaosMapEditor.Models.FacetType ft => (byte)ft,
                _ => (byte)0
            };

            var wall = values.ElementAtOrDefault(1) as Brush ?? Brushes.Lime;
            var fence = values.ElementAtOrDefault(2) as Brush ?? Brushes.Yellow;
            var cable = values.ElementAtOrDefault(3) as Brush ?? Brushes.Red;
            var door = values.ElementAtOrDefault(4) as Brush ?? Brushes.MediumPurple;
            var ladder = values.ElementAtOrDefault(5) as Brush ?? Brushes.Orange;
            var other = values.ElementAtOrDefault(6) as Brush ?? Brushes.LightSkyBlue;

            return type switch
            {
                3 => wall,
                10 or 11 or 13 => fence,
                9 => cable,
                18 or 19 or 21 => door,
                12 => ladder,
                _ => other
            };
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => Array.Empty<object>();
        public static FacetTypeToBrushConverter Instance { get; } = new();
    }


}
