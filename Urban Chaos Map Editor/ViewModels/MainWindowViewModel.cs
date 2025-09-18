using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.Services;
using UrbanChaosMapEditor.Services.DataServices;

namespace UrbanChaosMapEditor.ViewModels
{
    public sealed class MainWindowViewModel : BaseViewModel
    {
        #region ====== Constants / Services ======
        private const string AppName = "Urban Chaos Map Editor";
        private readonly IUiDialogService _dialogs;
        #endregion

        #region ====== Title & File Labels ======
        // Window title (computed via UpdateTitle())
        private string _title = AppName;
        public string Title
        {
            get => _title;
            private set { _title = value; RaisePropertyChanged(); }
        }

        // Currently opened map path
        private string? _currentMapPath;
        public string? CurrentMapPath
        {
            get => _currentMapPath;
            private set { _currentMapPath = value; RaisePropertyChanged(); UpdateTitle(); }
        }

        // ---- NEW: basic "lights file" tracking (local placeholders until service is ready) ----

        public string MapFileDisplay
            => !string.IsNullOrWhiteSpace(MapDataService.Instance.CurrentPath)
               ? MapDataService.Instance.CurrentPath!
               : "{default.iam}";

        public string LightsFileDisplay
            => !string.IsNullOrWhiteSpace(LightsDataService.Instance.CurrentFilePath)
               ? LightsDataService.Instance.CurrentFilePath!
               : "(default.lgt)";

        public string StatusFiles => $"Map: {MapFileDisplay}    Light: {LightsFileDisplay}";
        #endregion

        #region ====== Status / UI State ======
        private string _editorActionStatus = "Action: None";
        public string EditorActionStatus
        {
            get => _editorActionStatus;
            set { if (_editorActionStatus != value) { _editorActionStatus = value; RaisePropertyChanged(); } }
        }

        // Status bar message
        private string _statusMessage = "Waiting for Map";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; RaisePropertyChanged(); }
        }

        // Busy flag (disables commands while true)
        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy == value) return;
                _isBusy = value;
                RaisePropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }
        #endregion

        #region ====== Child VMs / Collections ======
        public MapViewModel Map { get; } = new();
        public LightEditorViewModel LightEditor { get; } = new();

        // Recent files (for File -> Recent)
        public ObservableCollection<string> RecentFiles { get; } = new();
        #endregion

        #region ====== Commands (declarations) ======


        public ICommand NewMapCommand { get; }
        public ICommand LoadMapCommand { get; }
        public ICommand OpenRecentCommand { get; }
        public ICommand ClearRecentCommand { get; }
        public ICommand SaveMapCommand { get; }
        public ICommand SaveAsMapCommand { get; }
        public ICommand ToggleGridLinesCommand { get; }
        public ICommand ToggleMapWhoCommand { get; }
        public ICommand ResetZoomCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand ToggleHeightsCommand { get; }
        public ICommand RandomizeHeightsCommand { get; }
        public ICommand ToggleTexturesCommand { get; }
        public ICommand ToggleObjectsCommand { get; }
        public ICommand DeletePrimCommand { get; }
        public ICommand ShowPrimPropertiesCommand { get; }

        // Lights
        public ICommand LoadLightsCommand { get; }
        public ICommand SaveLightsCommand { get; }
        public ICommand SaveAsLightsCommand { get; }
        public ICommand ToggleLightsCommand { get; }
        public ICommand RevertLightsFromDiskCommand { get; }
        public ICommand SaveAllCommand { get; }
        public ICommand SaveAllAsCommand { get; }
        public ICommand NewLightsCommand { get; }
        #endregion

        #region ====== Constructor ======
        public MainWindowViewModel()
        {
            // ----- Command wiring -----
            RecentFilesService.Instance.RecentChanged += (_, __) => SyncRecentFiles();
            SyncRecentFiles();
            NewMapCommand = new RelayCommand(async _ => await NewMapAsync(), _ => !IsBusy);
            LoadMapCommand = new RelayCommand(async _ => await LoadMapAsync(), _ => !IsBusy);
            OpenRecentCommand = new RelayCommand(async p => await OpenRecentAsync(p as string),
                                                 p => !IsBusy && p is string s && System.IO.File.Exists(s));
            ClearRecentCommand = new RelayCommand(_ =>
            {
                RecentFiles.Clear();
                RecentFilesService.Instance.Clear();
            }, _ => RecentFiles.Count > 0);

            RevertLightsFromDiskCommand = new RelayCommand(async _ => await RevertLightsFromDiskAsync(),
                                               _ => !IsBusy && LightsDataService.Instance.IsLoaded
                                                 && !string.IsNullOrWhiteSpace(LightsDataService.Instance.CurrentPath));

            SaveMapCommand = new RelayCommand(async _ => await SaveMapAsync(),
                                                 _ => !IsBusy && MapDataService.Instance.IsLoaded && MapDataService.Instance.HasChanges);

            SaveAsMapCommand = new RelayCommand(async _ => await SaveAsMapAsync(),
                                                 _ => !IsBusy && MapDataService.Instance.IsLoaded);

            // Lights commands — make Save available only when lights are dirty.
            LoadLightsCommand = new RelayCommand(async _ => await LoadLightsAsync(), _ => !IsBusy);
            SaveLightsCommand = new RelayCommand(async _ => await SaveLightsAsync(),
                                                 _ => !IsBusy && LightsDataService.Instance.IsLoaded && LightsDataService.Instance.HasChanges);
            SaveAsLightsCommand = new RelayCommand(async _ => await SaveAsLightsAsync(),
                                                   _ => !IsBusy && LightsDataService.Instance.IsLoaded);

            // Commands (add these where you wire the others)
            NewLightsCommand = new RelayCommand(_ => NewLights(), _ => !IsBusy);

            // Save All / Save All As
            SaveAllCommand = new RelayCommand(async _ => await SaveAllAsync(),
                                                  _ => !IsBusy && (MapDataService.Instance.HasChanges || LightsDataService.Instance.HasChanges));

            SaveAllAsCommand = new RelayCommand(async _ => await SaveAllAsAsync(),
                                                  _ => !IsBusy && (MapDataService.Instance.IsLoaded || LightsDataService.Instance.IsLoaded));

            // ----- MRU load & mirror -----
            RecentFilesService.Instance.Load();
            SyncRecentFromService();

            RecentFilesService.Instance.RecentChanged += (_, __) =>
                Application.Current.Dispatcher.Invoke(SyncRecentFromService);

            // ----- Map lifecycle events -----
            MapDataService.Instance.MapLoaded += (_, e) =>
                Application.Current.Dispatcher.Invoke(() =>
                {
                    RecentFilesService.Instance.Add(e.Path);
                    StatusMessage = $"Loaded Map – {e.Path}";
                    CurrentMapPath = e.Path;

                    // Seed world number, refresh
                    try
                    {
                        var acc = new TexturesAccessor(MapDataService.Instance);
                        Map.TextureWorld = acc.ReadTextureWorld();
                        Map.RefreshTextureLists();
                    }
                    catch { /* ignore */ }

                    CommandManager.InvalidateRequerySuggested();
                    Map.RefreshPrimsList();

                    // Keep the files strip up to date
                    RaisePropertyChanged(nameof(MapFileDisplay));
                    RaisePropertyChanged(nameof(LightsFileDisplay));
                    RaisePropertyChanged(nameof(StatusFiles));

                    // ensure title reflects the newly-set path
                    UpdateTitle();

                    // >>> Prompt to load lights (runs async; no await needed here)
                    _ = PromptLoadLightsAfterMapLoad();

                    try
                    {
                        var svc = MapDataService.Instance;
                        var bytes = svc.GetBytesCopy();

                        // Snapshot (has SaveType, ObjectSectionSize, ObjectOffset)
                        var objAcc = new ObjectsAccessor(svc);
                        var snap = objAcc.ReadSnapshot();

                        // Compute & cache [start,len] of the building block once per load
                        // (uses fileLength - 12 - (saveType>=25?2000:0) - objectBytes, then -8 for the header start)
                        svc.ComputeAndCacheBuildingRegion();

                        // Optional: diagnostic — find the header by pattern/heuristic and log it ONLY
                       // int buildingHeaderOffset = BuildingOffsetFinder.FindHeader(snap, bytes);
                   //     System.Diagnostics.Debug.WriteLine($"[Buildings] Header @ 0x{buildingHeaderOffset:X}");
                       // System.Diagnostics.Debug.WriteLine(
                       //     $"[Buildings] saveType={snap.SaveType}, objSize={snap.ObjectSectionSize}, objOff=0x{snap.ObjectOffset:X}");

                        // Kick a repaint; the layer will pull the region from MapDataService
                        var view = System.Windows.Application.Current.MainWindow as Views.MainWindow;
                        var mapView = view?.MapViewControl;
                        var bLayer = mapView?.FindName("BuildingLayer") as UrbanChaosMapEditor.Views.MapOverlays.BuildingLayer;
                        bLayer?.InvalidateVisual();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Buildings] Header locate failed: {ex}");
                    }
                });

            MapDataService.Instance.MapCleared += (_, __) =>
               Application.Current.Dispatcher.Invoke(() =>
               {
                   Map.Prims.Clear();
                   StatusMessage = "Map cleared";
                   CurrentMapPath = null;
                   CommandManager.InvalidateRequerySuggested();

                   // Refresh status bar filenames/paths (map shows default)
                   RaisePropertyChanged(nameof(MapFileDisplay));
                   RaisePropertyChanged(nameof(LightsFileDisplay));
                   RaisePropertyChanged(nameof(StatusFiles));
               });

            MapDataService.Instance.MapSaved += (_, e) =>
                Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"Saved Map – {e.Path}";
                    // path may change after Save As
                    CurrentMapPath = e.Path;
                    CommandManager.InvalidateRequerySuggested();

                    // Refresh status bar filenames/paths
                    RaisePropertyChanged(nameof(MapFileDisplay));
                    RaisePropertyChanged(nameof(LightsFileDisplay));
                    RaisePropertyChanged(nameof(StatusFiles));
                });

            MapDataService.Instance.DirtyStateChanged += (_, __) =>
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // enable/disable Save and update title star
                    UpdateTitle();
                    RaisePropertyChanged(nameof(StatusFiles));
                    CommandManager.InvalidateRequerySuggested();
                });

            MapDataService.Instance.MapBytesReset += (_, __) =>
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var acc = new TexturesAccessor(MapDataService.Instance);
                        Map.TextureWorld = acc.ReadTextureWorld(); // update the Textures tab field
                        Map.RefreshTextureLists();
                        Map.RefreshPrimsList();
                    }
                    catch { /* ignore */ }
                });

            // ----- Lights lifecycle events -----
            // Keep title & status in sync with .lgt lifecycle
            LightsDataService.Instance.LightsLoaded += (_, e) =>
                Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"Loaded Lights – {e.Path}";
                    RaisePropertyChanged(nameof(LightsFileDisplay));
                    RaisePropertyChanged(nameof(StatusFiles));
                    UpdateTitle();
                });

            LightsDataService.Instance.LightsSaved += (_, e) =>
                Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"Saved Lights – {e.Path}";
                    RaisePropertyChanged(nameof(LightsFileDisplay));
                    RaisePropertyChanged(nameof(StatusFiles));
                    UpdateTitle();
                });

            LightsDataService.Instance.LightsCleared += (_, __) =>
                Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = "Lights cleared";
                    RaisePropertyChanged(nameof(LightsFileDisplay));
                    RaisePropertyChanged(nameof(StatusFiles));
                    UpdateTitle();
                });

            LightsDataService.Instance.DirtyStateChanged += (_, __) =>
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // enable Save Lights, and refresh title “*” marker for lights
                    UpdateTitle();
                    RaisePropertyChanged(nameof(StatusFiles));
                    CommandManager.InvalidateRequerySuggested();
                });

            LightsDataService.Instance.LightsBytesReset += (_, __) =>
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // If you need to poke the overlay to redraw, do it here.
                    RaisePropertyChanged(nameof(LightsFileDisplay));
                    RaisePropertyChanged(nameof(StatusFiles));
                });

            // Duplicate DirtyStateChanged hook retained
            LightsDataService.Instance.DirtyStateChanged += (_, __) =>
                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateTitle();
                    RaisePropertyChanged(nameof(StatusFiles));
                    CommandManager.InvalidateRequerySuggested();
                });

            // ----- View/overlay toggle commands -----
            ToggleGridLinesCommand = new RelayCommand(p =>
            {
                if (p is bool b) Map.ShowTextures = b;    // menu click passes new checked state
                else Map.ShowGridLines = !Map.ShowGridLines; // keyboard shortcut toggles
            }, _ => true);

            ToggleMapWhoCommand = new RelayCommand(p =>
            {
                if (p is bool b) Map.ShowTextures = b;    // menu click passes new checked state
                else Map.ShowMapWho = !Map.ShowMapWho; // keyboard shortcut toggles
            }, _ => true);

            ResetZoomCommand = new RelayCommand(_ =>
            {
                Map.Zoom = 1.0;              // reset zoom to 100%
                                             // (optional) if you later want to also recenter, we can signal the view to scroll to (0,0)
            });

            ExitCommand = new RelayCommand(_ =>
            {
                System.Windows.Application.Current.Shutdown();
            });

            ToggleHeightsCommand = new RelayCommand(p =>
            {
                if (p is bool b) Map.ShowTextures = b;    // menu click passes new checked state
                else Map.ShowHeights = !Map.ShowHeights; // keyboard shortcut toggles
            }, _ => true);

            ToggleTexturesCommand = new RelayCommand(p =>
            {
                if (p is bool b) Map.ShowTextures = b;    // menu click passes new checked state
                else Map.ShowTextures = !Map.ShowTextures; // keyboard shortcut toggles
            }, _ => true);

            ToggleObjectsCommand = new RelayCommand(p =>
            {
                if (p is bool b) Map.ShowObjects = b;
                else Map.ShowObjects = !Map.ShowObjects;
            }, _ => true);

            ToggleLightsCommand = new RelayCommand(p =>
            {
                if (p is bool b) Map.ShowLights = b;
                else Map.ShowLights = !Map.ShowLights;
            }, _ => true);

            // ----- Editing commands (heights/objects) -----
            RandomizeHeightsCommand = new RelayCommand(async _ => await RandomizeHeightsAsync(),
                                           _ => !IsBusy && MapDataService.Instance.IsLoaded);

            ShowPrimPropertiesCommand = new RelayCommand(_ => ShowPrimProperties(), _ => Map.SelectedPrim != null);

            DeletePrimCommand = new RelayCommand(_ =>
            {
                var sel = Map.SelectedPrim;
                if (sel is null) return;

                try
                {
                    var acc = new ObjectsAccessor(MapDataService.Instance);
                    acc.DeletePrim(sel.Index);          // remove from IAM (object array + MapWho rebuilt)
                    Map.RefreshPrimsList();             // refresh UI list and overlay points
                    StatusMessage = $"Deleted “{sel.Name}” (index {sel.Index}).";
                }
                catch (Exception ex)
                {
                    StatusMessage = "Error: Failed to delete prim.";
                    MessageBox.Show($"Failed to delete prim.\n\n{ex.Message}", "Error",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                }
            },
_ => MapDataService.Instance.IsLoaded && Map.SelectedPrim != null);

            // ----- Track tool/step/texture changes and update status -----
            Map.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Map.SelectedTool) ||
                    e.PropertyName == nameof(Map.HeightStep) ||
                    e.PropertyName == nameof(Map.BrushSize) ||
                    e.PropertyName == nameof(Map.SelectedTextureGroup) ||
                    e.PropertyName == nameof(Map.SelectedTextureNumber) ||
                    e.PropertyName == nameof(Map.SelectedRotationIndex))
                {
                    UpdateEditorActionStatus();
                }
                else if (e.PropertyName == nameof(Map.UseBetaTextures))
                {
                    StatusMessage = Map.UseBetaTextures ? "Using Beta textures" : "Using Release textures";
                }
                else if (e.PropertyName == nameof(Map.TextureWorld))
                {
                    StatusMessage = $"Texture world set to {Map.TextureWorld}";
                }
                else if (e.PropertyName == nameof(Map.SelectedPrim))
                {
                    // update command CanExecute (e.g., Delete button / menu)
                    CommandManager.InvalidateRequerySuggested();

                    var p = Map.SelectedPrim;
                    if (p != null)
                    {
                        // require: using UrbanChaosMapEditor.Models;  (for PrimFlags)
                        var flags = PrimFlags.FromByte(p.Flags);
                        var insideLabel = p.InsideIndex == 0 ? "Outside" : $"Inside={p.InsideIndex}";
                        StatusMessage = $"Selected {p.Name} @ ({p.X},{p.Z},{p.Y}) | Flags: [{flags}] | {insideLabel}";
                    }
                    else
                    {
                        StatusMessage = "No prim selected";
                    }
                }
            };

            // ----- Initialize -----
            UpdateEditorActionStatus();  // Initial action text
            UpdateTitle();               // Initial title
            Map.BuildPrimPalette();      // Build prim palette at startup
        }
        #endregion

        #region ====== Title / MRU Helpers ======
        private void UpdateTitle()
        {
            string mapLabel = string.IsNullOrWhiteSpace(MapDataService.Instance.CurrentPath)
                                ? "Untitled"
                                : System.IO.Path.GetFileName(MapDataService.Instance.CurrentPath);
            string mapStar = MapDataService.Instance.HasChanges ? "*" : "";

            // Lights label: default if no path
            string lightsLabel = string.IsNullOrWhiteSpace(LightsDataService.Instance.CurrentPath)
                                 ? "default.lgt"
                                 : System.IO.Path.GetFileName(LightsDataService.Instance.CurrentPath);
            string lightsStar = LightsDataService.Instance.HasChanges ? "*" : "";

            Title = $"Urban Chaos Map Editor — {mapLabel}{mapStar} ({lightsLabel}{lightsStar})";
        }

        private void SyncRecentFromService()
        {
            RecentFiles.Clear();
            foreach (var p in RecentFilesService.Instance.Items)
                RecentFiles.Add(p);
            RaisePropertyChanged(nameof(RecentFiles));
            CommandManager.InvalidateRequerySuggested();
        }
        #endregion

        #region ====== Map: New / Load / Save ======
        private async Task NewMapAsync()
        {
            // Prompt to save if needed
            if (MapDataService.Instance.IsLoaded && MapDataService.Instance.HasChanges)
            {
                var choice = MessageBox.Show(
                    "Save changes to the current map before creating a new one?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (choice == MessageBoxResult.Cancel) return;

                if (choice == MessageBoxResult.Yes)
                {
                    if (string.IsNullOrWhiteSpace(MapDataService.Instance.CurrentPath))
                    {
                        var sfd = new SaveFileDialog
                        {
                            Title = "Save Map As",
                            Filter = "Urban Chaos Map (*.iam)|*.iam|All Files (*.*)|*.*",
                            FileName = "map.iam"
                        };
                        if (sfd.ShowDialog() != true) return;

                        try
                        {
                            IsBusy = true;
                            StatusMessage = "Saving map...";
                            await MapDataService.Instance.SaveAsAsync(sfd.FileName);
                        }
                        catch (Exception ex)
                        {
                            StatusMessage = "Error: Failed to save map.";
                            MessageBox.Show($"Failed to save map.\n\n{ex.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                        finally { IsBusy = false; }
                    }
                    else
                    {
                        try
                        {
                            IsBusy = true;
                            StatusMessage = "Saving map...";
                            await MapDataService.Instance.SaveAsync();
                        }
                        catch (Exception ex)
                        {
                            StatusMessage = "Error: Failed to save map.";
                            MessageBox.Show($"Failed to save map.\n\n{ex.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                        finally { IsBusy = false; }
                    }
                }
            }

            // Create a new unsaved map from default template
            try
            {
                IsBusy = true;
                StatusMessage = "Creating new map...";

                var bytes = LoadDefaultTemplateBytes();
                MapDataService.Instance.NewFromTemplate(bytes);

                // NEW: seed world number in VM
                try
                {
                    var acc = new TexturesAccessor(MapDataService.Instance);
                    Map.TextureWorld = acc.ReadTextureWorld();
                    Map.RefreshTextureLists();
                }
                catch { /* ignore */ }

                CurrentMapPath = null; // unsaved
                StatusMessage = "New Map created (unsaved)";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error: Failed to create new map.";
                MessageBox.Show($"Failed to create new map.\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { IsBusy = false; }
        }

        private static byte[] LoadDefaultTemplateBytes()
        {
            var uri = new Uri("pack://application:,,,/Assets/Defaults/default.iam", UriKind.Absolute);
            var sri = Application.GetResourceStream(uri)
                      ?? throw new FileNotFoundException("default.iam not found at Assets/Defaults/default.iam (Resource).");

            using var s = sri.Stream;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        private async Task LoadMapAsync()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Load Map (.iam)",
                Filter = "Urban Chaos Map (*.iam)|*.iam|All Files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Loading map...";
                await MapDataService.Instance.LoadAsync(dlg.FileName);

                UpdateTitle();
            }
            catch (Exception ex)
            {
                StatusMessage = "Error: Failed to load map.";
                MessageBox.Show($"Failed to load map.\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { IsBusy = false; }
        }

        private async Task OpenRecentAsync(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Loading map...";
                await MapDataService.Instance.LoadAsync(path);
            }
            catch (Exception ex)
            {
                StatusMessage = "Error: Failed to load map.";
                MessageBox.Show($"Failed to load map.\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { IsBusy = false; }
        }

        private async Task SaveMapAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Saving map...";

                if (string.IsNullOrWhiteSpace(MapDataService.Instance.CurrentPath))
                {
                    var dlg = new SaveFileDialog
                    {
                        Title = "Save Map As",
                        Filter = "Urban Chaos Map (*.iam)|*.iam|All Files (*.*)|*.*",
                        FileName = "map.iam"
                    };
                    if (dlg.ShowDialog() != true) return;

                    await MapDataService.Instance.SaveAsAsync(dlg.FileName);
                }
                else
                {
                    await MapDataService.Instance.SaveAsync();
                }

                // Cascade: save lights too (prompt if it's still the default/unsaved)
                await SaveAssociatedLightsAfterMapAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = "Error: Failed to save map.";
                MessageBox.Show($"Failed to save map.\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { IsBusy = false; }
        }

        private async Task SaveAsMapAsync()
        {
            if (!MapDataService.Instance.IsLoaded) return;

            var dlg = new SaveFileDialog
            {
                Title = "Save Map As",
                Filter = "Urban Chaos Map (*.iam)|*.iam|All Files (*.*)|*.*",
                FileName = string.IsNullOrWhiteSpace(MapDataService.Instance.CurrentPath)
                           ? "map.iam"
                           : Path.GetFileName(MapDataService.Instance.CurrentPath)
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Saving map...";
                await MapDataService.Instance.SaveAsAsync(dlg.FileName);

                // Cascade: save lights too (prompt if it's still the default/unsaved)
                await SaveAssociatedLightsAfterMapAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = "Error: Failed to save map.";
                MessageBox.Show($"Failed to save map.\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { IsBusy = false; }
        }
        #endregion

        #region ====== Lights: Load / Save / New / Save-with-Map ======
        /// <summary>
        /// Save the currently loaded .lgt when a map is saved.
        /// If the .lgt has no path (default/template), prompt for a location,
        /// defaulting to the map’s folder and name.
        /// </summary>
        private async Task SaveAssociatedLightsAfterMapAsync()
        {
            var lsvc = LightsDataService.Instance;
            if (!lsvc.IsLoaded) return;

            try
            {
                // If no path yet, prompt. Default name is <mapName>.lgt next to the map.
                if (string.IsNullOrWhiteSpace(lsvc.CurrentPath))
                {
                    string? mapPath = MapDataService.Instance.CurrentPath;
                    string initialDir =
                        !string.IsNullOrWhiteSpace(mapPath)
                            ? Path.GetDirectoryName(mapPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                    string suggestedName =
                        !string.IsNullOrWhiteSpace(mapPath)
                            ? Path.GetFileNameWithoutExtension(mapPath) + ".lgt"
                            : "lights.lgt";

                    var dlg = new SaveFileDialog
                    {
                        Title = "Save Lights As",
                        Filter = "Urban Chaos Lights (*.lgt)|*.lgt|All Files (*.*)|*.*",
                        InitialDirectory = initialDir,
                        FileName = suggestedName
                    };

                    if (dlg.ShowDialog() == true)
                    {
                        StatusMessage = "Saving lights...";
                        await lsvc.SaveAsAsync(dlg.FileName);
                        StatusMessage = $"Saved Lights — {dlg.FileName}";
                    }
                    else
                    {
                        // User cancelled; keep in-memory default
                    }
                }
                else
                {
                    // Only hit disk if there are changes
                    if (lsvc.HasChanges)
                    {
                        StatusMessage = "Saving lights...";
                        await lsvc.SaveAsync();
                        StatusMessage = $"Saved Lights — {lsvc.CurrentPath}";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Error: Failed to save lights.";
                MessageBox.Show($"Failed to save lights.\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // keep status strip up-to-date
                RaisePropertyChanged(nameof(LightsFileDisplay));
                RaisePropertyChanged(nameof(StatusFiles));
                UpdateTitle();
            }
        }

        // Optional: modernize the explicit Lights commands to use LightsDataService

        private async Task SaveLightsAsync()
        {
            var lsvc = LightsDataService.Instance;
            if (!lsvc.IsLoaded)
            {
                MessageBox.Show("No lights are loaded.", "Save Lights", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(lsvc.CurrentPath))
            {
                await SaveAsLightsAsync();
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Saving lights...";
                await lsvc.SaveAsync();
                StatusMessage = $"Saved Lights — {lsvc.CurrentPath}";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error: Failed to save lights.";
                MessageBox.Show($"Failed to save lights.\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                RaisePropertyChanged(nameof(LightsFileDisplay));
                RaisePropertyChanged(nameof(StatusFiles));
                UpdateTitle();
            }
        }

        private async Task SaveAsLightsAsync()
        {
            var lsvc = LightsDataService.Instance;
            if (!lsvc.IsLoaded)
            {
                MessageBox.Show("No lights are loaded.", "Save Lights As", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string? mapPath = MapDataService.Instance.CurrentPath;
            string initialDir =
                !string.IsNullOrWhiteSpace(mapPath)
                    ? Path.GetDirectoryName(mapPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            string suggestedName =
                !string.IsNullOrWhiteSpace(mapPath)
                    ? Path.GetFileNameWithoutExtension(mapPath) + ".lgt"
                    : (Path.GetFileName(lsvc.CurrentPath) ?? "lights.lgt");

            var dlg = new SaveFileDialog
            {
                Title = "Save Lights As",
                Filter = "Urban Chaos Lights (*.lgt)|*.lgt|All Files (*.*)|*.*",
                InitialDirectory = initialDir,
                FileName = suggestedName
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Saving lights...";
                await lsvc.SaveAsAsync(dlg.FileName);
                StatusMessage = $"Saved Lights — {dlg.FileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error: Failed to save lights.";
                MessageBox.Show($"Failed to save lights.\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                RaisePropertyChanged(nameof(LightsFileDisplay));
                RaisePropertyChanged(nameof(StatusFiles));
                UpdateTitle();
            }
        }

        // ---------- Lights: Load / Save (stubs until LightsDataService API is available) ----------
        private async Task LoadLightsAsync()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Load Lights (.lgt)",
                Filter = "Urban Chaos Lights (*.lgt)|*.lgt|All Files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Loading lights...";
                await LightsDataService.Instance.LoadAsync(dlg.FileName);   // <-- the missing bit
                StatusMessage = $"Loaded Lights — {dlg.FileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error: Failed to load lights.";
                MessageBox.Show($"Failed to load lights.\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                // keep UI in sync
                RaisePropertyChanged(nameof(LightsFileDisplay));
                RaisePropertyChanged(nameof(StatusFiles));
                UpdateTitle();
            }
        }

        // Prompt after a map load to also load a .lgt or use the built-in default (resource)
        private async Task PromptLoadLightsAfterMapLoad()
        {
            try
            {
                // Ask once, right after the map loads
                var choice = MessageBox.Show(
                    "Load a .lgt lighting file for this map?",
                    "Load Lights",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (choice == MessageBoxResult.Yes)
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Load Lights (.lgt)",
                        Filter = "Urban Chaos Lights (*.lgt)|*.lgt|All Files (*.*)|*.*",
                        CheckFileExists = true,
                        Multiselect = false
                    };

                    if (dlg.ShowDialog() == true)
                    {
                        IsBusy = true;
                        StatusMessage = "Loading lights...";
                        await LightsDataService.Instance.LoadAsync(dlg.FileName);
                        StatusMessage = $"Loaded Lights – {dlg.FileName}";
                    }
                    else
                    {
                        // user cancelled the picker → fall back to default
                        UseDefaultLights();
                    }
                }
                else
                {
                    // No → start a fresh/default lights buffer
                    UseDefaultLights();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Error: Failed to load lights.";
                MessageBox.Show($"Failed to load lights.\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                // Update the status strip text
                RaisePropertyChanged(nameof(LightsFileDisplay));
                RaisePropertyChanged(nameof(StatusFiles));
            }
        }

        private void UseDefaultLights()
        {
            var bytes = LoadDefaultLightsBytes();
            LightsDataService.Instance.NewFromTemplate(bytes);
            StatusMessage = "Using default.lgt lighting.";
        }

        private static byte[] LoadDefaultLightsBytes()
        {
            var uri = new Uri("pack://application:,,,/Assets/Defaults/default.lgt", UriKind.Absolute);
            var sri = Application.GetResourceStream(uri)
                      ?? throw new FileNotFoundException("default.lgt not found at Assets/Defaults/default.lgt (Resource).");

            using var s = sri.Stream;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        private void NewLights()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Creating new lights...";
                var bytes = LightsDataService.LoadDefaultResourceBytes();
                LightsDataService.Instance.NewFromTemplate(bytes);   // unsaved default; not dirty
                StatusMessage = "New lights created (unsaved default).";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error: Failed to create lights.";
                MessageBox.Show($"Failed to create new lights.\n\n{ex.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                RaisePropertyChanged(nameof(LightsFileDisplay));
                RaisePropertyChanged(nameof(StatusFiles));
                UpdateTitle();
            }
        }
        #endregion

        #region ====== Save All / Save All As ======
        private async Task SaveAllAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Saving all…";

                // Map
                if (MapDataService.Instance.IsLoaded && MapDataService.Instance.HasChanges)
                {
                    if (string.IsNullOrWhiteSpace(MapDataService.Instance.CurrentPath))
                    {
                        var dlg = new Microsoft.Win32.SaveFileDialog
                        {
                            Title = "Save Map As",
                            Filter = "Urban Chaos Map (*.iam)|*.iam|All Files (*.*)|*.*",
                            FileName = "map.iam"
                        };
                        if (dlg.ShowDialog() == true)
                            await MapDataService.Instance.SaveAsAsync(dlg.FileName);
                    }
                    else
                    {
                        await MapDataService.Instance.SaveAsync();
                    }
                }

                // Lights
                if (LightsDataService.Instance.IsLoaded && LightsDataService.Instance.HasChanges)
                {
                    if (string.IsNullOrWhiteSpace(LightsDataService.Instance.CurrentPath))
                    {
                        var dlg = new Microsoft.Win32.SaveFileDialog
                        {
                            Title = "Save Lights As",
                            Filter = "Urban Chaos Lights (*.lgt)|*.lgt|All Files (*.*)|*.*",
                            FileName = "lights.lgt"
                        };
                        if (dlg.ShowDialog() == true)
                            await LightsDataService.Instance.SaveAsAsync(dlg.FileName);
                    }
                    else
                    {
                        await LightsDataService.Instance.SaveAsync();
                    }
                }

                StatusMessage = "Save all complete.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error: Save all failed.";
                MessageBox.Show($"Save all failed.\n\n{ex.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                UpdateTitle();
                RaisePropertyChanged(nameof(MapFileDisplay));
                RaisePropertyChanged(nameof(LightsFileDisplay));
                RaisePropertyChanged(nameof(StatusFiles));
            }
        }

        private async Task SaveAllAsAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Save All As…";

                // Map (if loaded)
                if (MapDataService.Instance.IsLoaded)
                {
                    var mapDlg = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Save Map As",
                        Filter = "Urban Chaos Map (*.iam)|*.iam|All Files (*.*)|*.*",
                        FileName = string.IsNullOrWhiteSpace(MapDataService.Instance.CurrentPath)
                                   ? "map.iam"
                                   : System.IO.Path.GetFileName(MapDataService.Instance.CurrentPath)
                    };
                    if (mapDlg.ShowDialog() == true)
                        await MapDataService.Instance.SaveAsAsync(mapDlg.FileName);
                }

                // Lights (if loaded)
                if (LightsDataService.Instance.IsLoaded)
                {
                    var lgtDlg = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Save Lights As",
                        Filter = "Urban Chaos Lights (*.lgt)|*.lgt|All Files (*.*)|*.*",
                        FileName = string.IsNullOrWhiteSpace(LightsDataService.Instance.CurrentPath)
                                   ? "lights.lgt"
                                   : System.IO.Path.GetFileName(LightsDataService.Instance.CurrentPath)
                    };
                    if (lgtDlg.ShowDialog() == true)
                        await LightsDataService.Instance.SaveAsAsync(lgtDlg.FileName);
                }

                StatusMessage = "Save All As complete.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error: Save All As failed.";
                MessageBox.Show($"Save All As failed.\n\n{ex.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                UpdateTitle();
                RaisePropertyChanged(nameof(MapFileDisplay));
                RaisePropertyChanged(nameof(LightsFileDisplay));
                RaisePropertyChanged(nameof(StatusFiles));
            }
        }
        #endregion

        #region ====== Tools / Editing ======
        private async Task RandomizeHeightsAsync()
        {
            if (!MapDataService.Instance.IsLoaded)
            {
                StatusMessage = "Load a map first.";
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Generating terrain…";

                // Generate in background
                var seed = Environment.TickCount;
                const double roughness = 0.60;  // tweak 0.45..0.75
                const int blurPasses = 1;
                sbyte[,] heights = await Task.Run(() =>
                    TerrainGenerator.GenerateHeights128(seed, roughness, blurPasses));

                // Apply to IAM
                var accessor = new HeightsAccessor(MapDataService.Instance);

                // Edit all tiles
                Stopwatch sw = Stopwatch.StartNew();
                for (int ty = 0; ty < 128; ty++)
                {
                    for (int tx = 0; tx < 128; tx++)
                    {
                        accessor.WriteHeight(tx, ty, heights[tx, ty]);
                    }
                }
                sw.Stop();

                HeightsChangeBus.Instance.NotifyRegion(0, 0, 127, 127);

                StatusMessage = $"Terrain randomized (seed {seed}, t={sw.ElapsedMilliseconds} ms).";
                // trigger overlay repaint
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Ask the heights overlay to redraw (MapView will find it)
                    // If needed we can raise a global event; for now the layer listens to Save/Load, so:
                    // force a small nudge by toggling dirty state:
                    MapDataService.Instance.MarkDirty(); // also updates title (*)
                });
            }
            catch (Exception ex)
            {
                StatusMessage = "Error: Randomize failed.";
                System.Windows.MessageBox.Show($"Randomize heights failed.\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void UpdateEditorActionStatus()
        {
            string text = Map.SelectedTool switch
            {
                EditorTool.RaiseHeight => $"Action: Height ↑ Raise (step {Map.HeightStep})",
                EditorTool.LowerHeight => $"Action: Height ↓ Lower (step {Map.HeightStep})",
                EditorTool.LevelHeight => "Action: Height → Level (click-drag)",
                EditorTool.FlattenHeight => "Action: Height → Flatten (0)",
                EditorTool.DitchTemplate => "Action: Height → Template: Ditch",
                EditorTool.PaintTexture => $"Action: Texture → Paint ({Map.SelectedTextureGroup} #{Map.SelectedTextureNumber}, rot {Map.SelectedRotationIndex})",
                _ => "Action: None"
            };
            EditorActionStatus = text;
        }

        private async Task RevertLightsFromDiskAsync()
        {
            var path = LightsDataService.Instance.CurrentPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                // if unsaved, just reload default resource
                try
                {
                    IsBusy = true;
                    StatusMessage = "Reverting lights to default...";
                    var def = LoadDefaultLightsBytes(); // your existing helper that reads pack:// default.lgt
                    LightsDataService.Instance.NewFromTemplate(def);
                    StatusMessage = "Lights reverted to default.";
                }
                catch (Exception ex)
                {
                    StatusMessage = "Error: Failed to revert lights.";
                    MessageBox.Show($"Failed to revert lights.\n\n{ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { IsBusy = false; }
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Reverting lights from disk...";
                await LightsDataService.Instance.LoadAsync(path);
                StatusMessage = $"Lights reloaded — {path}";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error: Failed to revert lights.";
                MessageBox.Show($"Failed to revert lights.\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { IsBusy = false; }
        }

        private void SyncRecentFiles()
        {
            RecentFiles.Clear();
            foreach (var p in RecentFilesService.Instance.Items ?? Enumerable.Empty<string>())
                RecentFiles.Add(p);
        }

        // Open-by-path helpers (used by the Recent menu)
        public void OpenMapFromPath(string path)
        {
            try
            {
                MapDataService.Instance.LoadAsync(path);   // adjust if your API differs
                StatusMessage = $"Loaded map: {path}";
                RecentFilesService.Instance.Add(path);
                SyncRecentFiles();
            }
            catch (Exception ex)
            {
                StatusMessage = "Error loading map.";
                System.Windows.MessageBox.Show($"Failed to open map:\n\n{ex.Message}",
                    "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        public void OpenLightsFromPath(string path)
        {
            try
            {
                LightsDataService.Instance.LoadAsync(path); // adjust if your API differs
                StatusMessage = $"Loaded lights: {path}";
                RecentFilesService.Instance.Add(path);
                SyncRecentFiles();
            }
            catch (Exception ex)
            {
                StatusMessage = "Error loading lights.";
                System.Windows.MessageBox.Show($"Failed to open lights:\n\n{ex.Message}",
                    "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void ShowPrimProperties()
        {
            if (Map.SelectedPrim is null) return;

            var p = Map.SelectedPrim;
            // index in prim array is p.Index (we already store it when building the list)
            var vm = new PrimPropertiesViewModel(
                primArrayIndex: p.Index,
                flags: p.Flags,
                insideIndex: p.InsideIndex);

            var dlg = new Views.PrimPropertiesDialog(p.Flags, p.InsideIndex) { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() == true)
            {
                // Apply changes to the file
                var acc = new Services.ObjectsAccessor(MapDataService.Instance);
                var snap = acc.ReadSnapshot();

                if (p.Index >= 0 && p.Index < snap.Prims.Length)
                {
                    var prims = snap.Prims.ToArray();
                    var pe = prims[p.Index];
                    pe.Flags = vm.EncodeFlags();
                    pe.InsideIndex = (byte)vm.InsideIndex;
                    prims[p.Index] = pe;

                    acc.ReplaceAllPrims(prims);

                    // Refresh UI: list + selection
                    Map.RefreshPrimsList();
                    // re-select the same index (bounds check)
                    if (p.Index >= 0 && p.Index < Map.Prims.Count)
                        Map.SelectedPrim = Map.Prims[p.Index];

                    // Update status text
                    var flagsStr = Models.PrimFlags.FromByte(pe.Flags).ToString();
                    var insideLabel = pe.InsideIndex == 0 ? "Outside" : $"Inside={pe.InsideIndex}";
                    StatusMessage = $"Updated {p.Name} | Flags: [{flagsStr}] | {insideLabel}";
                }
            }
        }
        #endregion
    }
}
