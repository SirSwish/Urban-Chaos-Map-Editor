using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.Services;
using static UrbanChaosMapEditor.Models.PrimCatalog;
using static UrbanChaosMapEditor.Services.TexturesAccessor; // brings in TextureGroup

namespace UrbanChaosMapEditor.ViewModels
{
    // Thumbnail model used by the Textures tab
    public sealed class TextureThumb
    {
        public string RelativeKey { get; init; } = "";
        public ImageSource? Image { get; init; }
        public TextureGroup Group { get; init; }  // World / Shared / Prims
        public int Number { get; init; }          // parsed ### from key
    }

    // Add this simple model (public so XAML can see it)
    public sealed class PrimButton
    {
        public int Number { get; init; }                 // e.g. 228
        public string Title { get; init; } = "";         // e.g. "Fire Hydrant"
        public ImageSource? Icon { get; init; }          // button image
        public UrbanChaosMapEditor.Models.PrimButtonCategory Category { get; init; } // <- needed for XAML grouping
    }

    public sealed class PrimListItem : INotifyPropertyChanged
    {
        public int Index { get; set; }          // index in array
        public int MapWhoIndex { get; set; }    // 0..1023
        public int MapWhoRow { get; set; }
        public int MapWhoCol { get; set; }
        public byte PrimNumber { get; set; }
        public string Name { get; set; } = "";
        public short Y { get; set; }
        public byte X { get; set; }
        public byte Z { get; set; }
        public byte Yaw { get; set; }
        public byte Flags { get; set; }
        public byte InsideIndex { get; set; }

        private int _pixelX;
        public int PixelX { get => _pixelX; set { if (_pixelX != value) { _pixelX = value; OnPropertyChanged(nameof(PixelX)); } } }

        private int _pixelZ;
        public int PixelZ { get => _pixelZ; set { if (_pixelZ != value) { _pixelZ = value; OnPropertyChanged(nameof(PixelZ)); } } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string prop)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public sealed class LightListItem : INotifyPropertyChanged
    {
        public int Index { get; set; }       // light slot index (0..n-1)
        public int X { get; set; }           // world X
        public int Z { get; set; }           // world Z
        public int Y { get; init; }

        public byte Range { get; set; }
        public sbyte Red { get; set; }
        public sbyte Green { get; set; }
        public sbyte Blue { get; set; }

        // handy for future centering/jumps
        public int UiX { get; set; }
        public int UiZ { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }


    public sealed class MapViewModel : INotifyPropertyChanged
    {
        // ===== View / overlays =====
        private double _zoom = 1.0;
        public double Zoom { get => _zoom; set { if (_zoom != value) { _zoom = value; OnPropertyChanged(); } } }

        private bool _showTextures = true;
        public bool ShowTextures { get => _showTextures; set { if (_showTextures != value) { _showTextures = value; OnPropertyChanged(); } } }

        private bool _showHeights = true;
        public bool ShowHeights { get => _showHeights; set { if (_showHeights != value) { _showHeights = value; OnPropertyChanged(); } } }

        private bool _showBuildings = true;
        public bool ShowBuildings { get => _showBuildings; set { if (_showBuildings != value) { _showBuildings = value; OnPropertyChanged(); } } }

        private bool _showObjects = true;
        public bool ShowObjects { get => _showObjects; set { if (_showObjects != value) { _showObjects = value; OnPropertyChanged(); } } }

        private bool _showGridLines = true;
        public bool ShowGridLines { get => _showGridLines; set { if (_showGridLines != value) { _showGridLines = value; OnPropertyChanged(); } } }

        private bool _showMapWho = true;
        public bool ShowMapWho { get => _showMapWho; set { if (_showMapWho != value) { _showMapWho = value; OnPropertyChanged(); } } }

        // ===== Cursor (pixels + tiles) =====
        private int _cursorX;
        public int CursorX { get => _cursorX; set { if (_cursorX != value) { _cursorX = value; OnPropertyChanged(); } } }

        private int _cursorZ;
        public int CursorZ { get => _cursorZ; set { if (_cursorZ != value) { _cursorZ = value; OnPropertyChanged(); } } }

        private int _cursorTileX;
        public int CursorTileX { get => _cursorTileX; set { if (_cursorTileX != value) { _cursorTileX = value; OnPropertyChanged(); } } }

        private int _cursorTileY;
        public int CursorTileZ { get => _cursorTileY; set { if (_cursorTileY != value) { _cursorTileY = value; OnPropertyChanged(); } } }

        private int _lightPlacementOffset = 0;   // 0..255 sub-pixels
        public int LightPlacementOffset
        {
            get => _lightPlacementOffset;
            set { if (_lightPlacementOffset != value) { _lightPlacementOffset = Math.Clamp(value, 0, 255); OnPropertyChanged(); } }
        }

        private int _lightPlacementYPixels = 0;
        public int LightPlacementYPixels
        {
            get => _lightPlacementYPixels;
            set { if (_lightPlacementYPixels != value) { _lightPlacementYPixels = value; OnPropertyChanged(); } }
        }

        // ===== Unified tool selection =====
        private EditorTool _selectedTool = EditorTool.None;
        public EditorTool SelectedTool
        {
            get => _selectedTool;
            set { if (_selectedTool != value) { _selectedTool = value; OnPropertyChanged(); } }
        }

        // ===== Heights editing =====
        private int _heightStep = 1; // positive, non-zero
        public int HeightStep
        {
            get => _heightStep;
            set
            {
                var v = value <= 0 ? 1 : value;
                if (_heightStep != v) { _heightStep = v; OnPropertyChanged(); }
            }
        }

        private int _brushSize = 1; // 1..10
        public int BrushSize
        {
            get => _brushSize;
            set
            {
                var v = value < 1 ? 1 : (value > 10 ? 10 : value);
                if (_brushSize != v) { _brushSize = v; OnPropertyChanged(); }
            }
        }

        // ===== Textures: world / set (beta) / selection =====
        private int _textureWorld;
        public int TextureWorld
        {
            get => _textureWorld;
            set
            {
                if (_textureWorld == value) return;
                _textureWorld = value;
                OnPropertyChanged();

                // write-through to bytes if a map is loaded, then refresh lists
                try
                {
                    if (MapDataService.Instance.IsLoaded)
                    {
                        var acc = new TexturesAccessor(MapDataService.Instance);
                        acc.WriteTextureWorld(_textureWorld);
                    }
                }
                catch { /* non-fatal */ }

                RefreshTextureLists();
                TexturesChangeBus.Instance.NotifyChanged();
            }
        }

        private bool _useBetaTextures; // false = release, true = beta
        public bool UseBetaTextures
        {
            get => _useBetaTextures;
            set
            {
                if (_useBetaTextures == value) return;
                _useBetaTextures = value;
                OnPropertyChanged();

                TextureCacheService.Instance.ActiveSet = _useBetaTextures ? "beta" : "release";
                RefreshTextureLists();
                TexturesChangeBus.Instance.NotifyChanged();
            }
        }

        // Selection used by PaintTexture tool
        public TextureGroup SelectedTextureGroup { get; set; } = TextureGroup.World;
        public int SelectedTextureNumber { get; set; } = 0;

        private int _selectedRotationIndex = 2; // 0:180, 1:90, 2:0, 3:270 (v1 scheme)
        public int SelectedRotationIndex
        {
            get => _selectedRotationIndex;
            set
            {
                var v = ((value % 4) + 4) % 4;
                if (_selectedRotationIndex != v) { _selectedRotationIndex = v; OnPropertyChanged(); }
            }
        }

        private PrimListItem? _selectedPrim;
        public PrimListItem? SelectedPrim
        {
            get => _selectedPrim;
            set
            {
                if (!ReferenceEquals(_selectedPrim, value))
                {
                    _selectedPrim = value;
                    OnPropertyChanged();
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested(); // <-- add this
                }
            }
        }

        private PrimListItem? _dragPreviewPrim;
        public PrimListItem? DragPreviewPrim
        {
            get => _dragPreviewPrim;
            set { _dragPreviewPrim = value; OnPropertyChanged(); }
        }
        // Selected prim number when placing (−1 = none)
        private int _newPrimNumber = -1;
        public int NewPrimNumber
        {
            get => _newPrimNumber;
            set { if (_newPrimNumber != value) { _newPrimNumber = value; OnPropertyChanged(); } }
        }

        // Show/Hide lights overlay
        private bool _showLights = true;
        public bool ShowLights
        {
            get => _showLights;
            set { if (_showLights != value) { _showLights = value; OnPropertyChanged(); } }
        }

        // Placement state (ghost + add)
        private bool _isPlacingLight;
        public bool IsPlacingLight
        {
            get => _isPlacingLight;
            set { if (_isPlacingLight != value) { _isPlacingLight = value; OnPropertyChanged(); } }
        }

        // Ghost cursor (UI px)
        private int? _lightGhostUiX;
        public int? LightGhostUiX
        {
            get => _lightGhostUiX;
            set { if (_lightGhostUiX != value) { _lightGhostUiX = value; OnPropertyChanged(); } }
        }

        private int? _lightGhostUiZ;
        public int? LightGhostUiZ
        {
            get => _lightGhostUiZ;
            set { if (_lightGhostUiZ != value) { _lightGhostUiZ = value; OnPropertyChanged(); } }
        }

        // Placement parameters (defaults)
        private byte _lightPlacementRange = 128;
        public byte LightPlacementRange
        {
            get => _lightPlacementRange;
            set { if (_lightPlacementRange != value) { _lightPlacementRange = value; OnPropertyChanged(); } }
        }

        private sbyte _lightPlacementRed = 0, _lightPlacementGreen = 0, _lightPlacementBlue = 0;
        public sbyte LightPlacementRed
        {
            get => _lightPlacementRed;
            set { if (_lightPlacementRed != value) { _lightPlacementRed = value; OnPropertyChanged(); } }
        }
        public sbyte LightPlacementGreen
        {
            get => _lightPlacementGreen;
            set { if (_lightPlacementGreen != value) { _lightPlacementGreen = value; OnPropertyChanged(); } }
        }
        public sbyte LightPlacementBlue
        {
            get => _lightPlacementBlue;
            set { if (_lightPlacementBlue != value) { _lightPlacementBlue = value; OnPropertyChanged(); } }
        }

        private int _lightPlacementYStoreys = 0;
        public int LightPlacementYStoreys
        {
            get => _lightPlacementYStoreys;
            set { if (_lightPlacementYStoreys != value) { _lightPlacementYStoreys = value; OnPropertyChanged(); } }
        }
        // ===== Lights selection =====
        private int _selectedLightIndex = -1;   // -1 = none
        public int SelectedLightIndex
        {
            get => _selectedLightIndex;
            set { if (_selectedLightIndex != value) { _selectedLightIndex = value; OnPropertyChanged(); } }
        }

        // ===== Texture palettes shown in the Textures tab =====
        public ObservableCollection<TextureThumb> WorldTextures { get; } = new();
        public ObservableCollection<TextureThumb> SharedTextures { get; } = new();
        public ObservableCollection<TextureThumb> PrimTextures { get; } = new();
        public ObservableCollection<PrimListItem> Prims { get; } = new();
        public ObservableCollection<LightListItem> Lights { get; } = new();

        // Palette buckets
        public ObservableCollection<PrimButton> CityAssets { get; } = new();
        public ObservableCollection<PrimButton> Nature { get; } = new();
        public ObservableCollection<PrimButton> Vehicles { get; } = new();
        public ObservableCollection<PrimButton> Weapons { get; } = new();
        public ObservableCollection<PrimButton> Structures { get; } = new();
        public ObservableCollection<PrimButton> Misc { get; } = new();

        public ObservableCollection<PrimButton> PrimButtons { get; } = new();

        // NEW: constructor — put the two lines here
        public MapViewModel()
        {
            System.Diagnostics.Debug.WriteLine("[MapVM] ctor: starting up…");

            // Build list once at startup
            System.Diagnostics.Debug.WriteLine("[MapVM] ctor: calling RefreshLightsList()");
            try { RefreshLightsList(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[MapVM] ctor: RefreshLightsList failed: " + ex.Message); }
            System.Diagnostics.Debug.WriteLine($"[MapVM] ctor: Lights after init = {Lights.Count}");

            // Watch the collection changing (verifies UI source is getting items)
            Lights.CollectionChanged += (_, e) =>
                System.Diagnostics.Debug.WriteLine($"[MapVM] Lights.CollectionChanged → action={e.Action}, total now={Lights.Count}");

            // Rebuild when the .lgt buffer changes
            LightsDataService.Instance.LightsBytesReset += (_, __) =>
            {
                System.Diagnostics.Debug.WriteLine("[MapVM] LightsBytesReset received → RefreshLightsList()");
                Application.Current?.Dispatcher.Invoke(RefreshLightsList);
            };
        }

        // Placement state
        private bool _isPlacingPrim;
        public bool IsPlacingPrim
        {
            get => _isPlacingPrim;
            set { if (_isPlacingPrim != value) { _isPlacingPrim = value; OnPropertyChanged(); } }
        }

        private int _primNumberToPlace;
        public int PrimNumberToPlace
        {
            get => _primNumberToPlace;
            set
            {
                if (_primNumberToPlace == value) return;
                _primNumberToPlace = value;
                OnPropertyChanged();
            }
        }


        // Called when you click a palette button
        public void BeginPlacePrim(int primNumber)
        {
            PrimNumberToPlace = primNumber;
            IsPlacingPrim = true;
            DragPreviewPrim = null;   // ghost will be driven by MapView mouse move
        }
        // Cancel placement (Esc / right-click)
        public void CancelPlacePrim()
        {
            PrimNumberToPlace = -1;
            IsPlacingPrim = false;
            DragPreviewPrim = null;
        }

        // Overload with offset (keep existing for compatibility)
        public void BeginPlaceLight(byte range, sbyte r, sbyte g, sbyte b, int heightPixels)
        {
            LightPlacementRange = range;
            LightPlacementRed = r;
            LightPlacementGreen = g;
            LightPlacementBlue = b;
            LightPlacementYPixels = heightPixels;   // <— PIXELS, not storeys
            IsPlacingLight = true;
            LightGhostUiX = LightGhostUiZ = null;
        }


        /// <summary>Rebuilds the three texture lists from the cache for the current world/set.</summary>
        public void RefreshTextureLists()
        {
            WorldTextures.Clear();
            SharedTextures.Clear();
            PrimTextures.Clear();

            var cache = TextureCacheService.Instance;

            // WORLD depends on current world number
            var worldFolder = $"world{_textureWorld}";
            foreach (var key in cache.EnumerateRelativeKeys(worldFolder))
            {
                if (!cache.TryGetRelative(key, out var img) || img is null) continue;
                var num = ParseNumber(key);
                WorldTextures.Add(new TextureThumb { RelativeKey = key, Image = img, Group = TextureGroup.World, Number = num });
            }

            // SHARED
            foreach (var key in cache.EnumerateRelativeKeys("shared"))
            {
                if (!cache.TryGetRelative(key, out var img) || img is null) continue;
                var num = ParseNumber(key);
                SharedTextures.Add(new TextureThumb { RelativeKey = key, Image = img, Group = TextureGroup.Shared, Number = num });
            }

            // PRIMS
            foreach (var key in cache.EnumerateRelativeKeys("shared_prims"))
            {
                if (!cache.TryGetRelative(key, out var img) || img is null) continue;
                var num = ParseNumber(key);
                PrimTextures.Add(new TextureThumb { RelativeKey = key, Image = img, Group = TextureGroup.Prims, Number = num });
            }
        }

        public void RefreshLightsList()
        {
            System.Diagnostics.Debug.WriteLine("[MapVM] RefreshLightsList START");
            Lights.Clear();

            var svc = LightsDataService.Instance;
            var buf = svc.GetBytesCopy();

            // Guard: only read when a full lights buffer is present
            if (!svc.IsLoaded || buf.Length < LightsAccessor.TotalSize)
                return;

            var acc = new LightsAccessor(svc);
            var entries = acc.ReadAllEntries();

            System.Diagnostics.Debug.WriteLine($"[MapVM] ReadAllEntries → total={entries.Count}");

            int added = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.Used != 1) continue;

                Lights.Add(new LightListItem
                {
                    Index = i,
                    Range = e.Range,
                    Red = e.Red,
                    Green = e.Green,
                    Blue = e.Blue,
                    X = LightsAccessor.WorldXToUiX(e.X),
                    Z = LightsAccessor.WorldZToUiZ(e.Z),
                    Y = e.Y
                });
                added++;
            }
            System.Diagnostics.Debug.WriteLine($"[MapVM] RefreshLightsList END → added={added}, Lights.Count={Lights.Count}");
        }

        public void RefreshPrimsList()
        {
            Prims.Clear();
            var acc = new ObjectsAccessor(MapDataService.Instance);
            var snap = acc.ReadSnapshot();
            if (snap.Prims.Length == 0 || snap.MapWho.Length != 1024) return;

            for (int i = 0; i < snap.Prims.Length; i++)
            {
                var p = snap.Prims[i];
                int cell = p.MapWhoIndex;
                if (cell < 0 || cell > 1023) continue;

                ObjectSpace.GameIndexToUiRowCol(cell, out int uiRow, out int uiCol);
                ObjectSpace.GamePrimToUiPixels(cell, p.X, p.Z, out int pixelX, out int pixelZ);

                Prims.Add(new PrimListItem
                {
                    Index = i,
                    MapWhoIndex = cell,
                    MapWhoRow = uiRow,        // store UI row/col for display (optional)
                    MapWhoCol = uiCol,
                    PrimNumber = p.PrimNumber,
                    Name = PrimCatalog.GetName(p.PrimNumber),
                    Y = p.Y,
                    X = p.X,
                    Z = p.Z,
                    Yaw = p.Yaw,
                    Flags = p.Flags,
                    InsideIndex = p.InsideIndex,
                    PixelX = pixelX,
                    PixelZ = pixelZ
                });
            }

            System.Diagnostics.Debug.WriteLine($"[MapVM] RefreshPrimsList: added {Prims.Count} items");
        }

        // Build the palette (load /Assets/Images/Buttons/###.png if present)
        public void BuildPrimPalette()
        {
            PrimButtons.Clear();

            for (int n = 1; n <= 255; n++)
            {
                var title = PrimCatalog.GetName(n);
                var cat = PrimCatalog.TryGetCategory(n, out var c)
                            ? c
                            : PrimButtonCategory.Misc;

                PrimButtons.Add(new PrimButton
                {
                    Number = n,
                    Title = title,                 // <-- was Name
                    Icon = TryLoadPrimButtonImage(n),
                    Category = cat
                });
            }
        }
        private void BuildPrimButtons()
        {
            PrimButtons.Clear();

            for (int num = 1; num <= 255; num++)
            {
                // Get category; fall back to Misc if not mapped
                var cat = PrimCatalog.TryGetCategory(num, out var c)
                          ? c
                          : PrimButtonCategory.Misc;

                PrimButtons.Add(new PrimButton
                {
                    Number = num,
                    Title = PrimCatalog.GetName(num),
                    Icon = TryLoadPrimButtonImage(num),
                    Category = cat                           // <- IMPORTANT
                });
            }
        }

        // helper used above
        private static ImageSource? TryLoadPrimButtonImage(int number)
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/Assets/Images/Buttons/{number:000}.png", UriKind.Absolute);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = uri;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        public void CancelPlaceLight()
        {
            IsPlacingLight = false;
            LightGhostUiX = LightGhostUiZ = null;
        }


        private static int ParseNumber(string relativeKey)
        {
            // e.g. "world20_208" -> 208, "shared_prims_081" -> 81
            var parts = relativeKey.Split('_');
            return int.TryParse(parts[^1], out var n) ? n : 0;
        }

        // ===== INotifyPropertyChanged =====
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
