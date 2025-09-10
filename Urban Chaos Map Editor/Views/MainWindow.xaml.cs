using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.Services;
using UrbanChaosMapEditor.ViewModels;
using static UrbanChaosMapEditor.Services.ObjectsAccessor;

namespace UrbanChaosMapEditor.Views
{
    public partial class MainWindow : Window
    {
        private bool _heightHotkeyLatched;
        private PrimListItem? _copiedPrim;
        private LightEntry? _copiedLight;
        private void DeleteLight_Click(object sender, RoutedEventArgs e) => DeleteSelectedLight();
        private void CopyLight_Click(object sender, RoutedEventArgs e) => CopySelectedLight();
        private void PasteLight_Click(object sender, RoutedEventArgs e) => PasteLightAtCursor();
        private const double MinExpandedEditorWidth = 285;  // <-- your minimum when expanded
        private const double CollapsedRailWidth = 28;       // width when collapsed
        private double _lastEditorWidth = MinExpandedEditorWidth;



        public MainWindow()
        {
            InitializeComponent();
      

            Loaded += OnLoaded;
            AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(MainWindow_PreviewKeyDown), handledEventsToo: true);
            AddHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler(MainWindow_PreviewKeyUp), handledEventsToo: true);
        }

        private static readonly Regex _digits = new(@"^\d+$");

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            TryEnableDarkTitleBar();
            var vm = DataContext as MainWindowViewModel;
            System.Diagnostics.Debug.WriteLine($"[Recent] VM? {(vm != null)}  Count={(vm?.RecentFiles?.Count ?? -1)}");
        }


        private void EditorExpander_Expanded(object sender, RoutedEventArgs e)
        {
            // Enforce the min once expanded and restore last width (clamped to min)
            EditorCol.MinWidth = MinExpandedEditorWidth;
            var target = Math.Max(_lastEditorWidth, MinExpandedEditorWidth);
            EditorCol.Width = new GridLength(target);
        }

        private void EditorExpander_Collapsed(object sender, RoutedEventArgs e)
        {
            // Remember last “usable” width before collapsing
            var w = EditorCol.ActualWidth;
            if (w > MinExpandedEditorWidth) _lastEditorWidth = w;

            // Allow the rail to shrink below the expanded min
            EditorCol.MinWidth = 0;
            EditorCol.Width = new GridLength(CollapsedRailWidth);
        }

        private void TryEnableDarkTitleBar()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            // DWMWA_USE_IMMERSIVE_DARK_MODE has been 19 or 20 depending on Windows build.
            const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
            const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

            int trueValue = 1;

            // Try newer attribute first, then fallback.
            _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref trueValue, sizeof(int));
            _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref trueValue, sizeof(int));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            ref int pvAttribute,
            int cbAttribute);

        private void TextureThumb_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel shell) return;
            if ((sender as FrameworkElement)?.Tag is not TextureThumb thumb) return;

            var map = shell.Map;

            // Enter texture paint tool and set the chosen texture
            map.SelectedTool = EditorTool.PaintTexture;
            map.SelectedTextureGroup = thumb.Group;
            map.SelectedTextureNumber = thumb.Number;

            // Optional: reset rotation for a fresh placement (keep or remove as you prefer)
            // In your v1 scheme, SelectedRotationIndex 2 == 0 degrees. If you want straight 0deg, set to 2:
            // map.SelectedRotationIndex = 2;

            shell.StatusMessage = $"Texture paint: {thumb.RelativeKey} (rot {map.SelectedRotationIndex}) — click a tile to apply";
        }

        private void RotateTexture_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel shell) return;
            shell.Map.SelectedRotationIndex = (shell.Map.SelectedRotationIndex + 1) % 4;
            shell.StatusMessage = $"Rotation: {shell.Map.SelectedRotationIndex}";
        }

        private void GoToCell_Click(object sender, RoutedEventArgs e)
        {
            // Optionally seed dialog with current cursor tile (0..127)
            int curTx = 0, curTy = 0;
            if (DataContext is MainWindowViewModel vm)
            {
                // Map.CursorX/Y are pixels in game coords; convert to tile indices
                curTx = System.Math.Clamp(vm.Map.CursorX / 64, 0, 127);
                curTy = System.Math.Clamp(vm.Map.CursorZ / 64, 0, 127);
            }

            var dlg = new GoToCellDialog(curTx, curTy) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                // Scroll to the tile center
                MapViewControl.GoToTileCenter(dlg.Tx, dlg.Ty);
                // Optional: status toast
                if (DataContext is MainWindowViewModel vm2)
                    vm2.StatusMessage = $"Jumped to cell [{dlg.Tx},{dlg.Ty}]";
            }
        }
        private void RotateLeft_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel shell) return;
            var map = shell.Map;
            map.SelectedRotationIndex = (map.SelectedRotationIndex + 3) % 4; // -1 mod 4
            shell.StatusMessage = $"Rotation: {map.SelectedRotationIndex}  (0→180°, 1→90°, 2→0°, 3→270°)";
        }

        private void RotateRight_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel shell) return;
            var map = shell.Map;
            map.SelectedRotationIndex = (map.SelectedRotationIndex + 1) % 4;
            shell.StatusMessage = $"Rotation: {map.SelectedRotationIndex}  (0→180°, 1→90°, 2→0°, 3→270°)";
        }

        private void PrimsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not MainWindowViewModel shell) return;
            if ((sender as ListView)?.SelectedItem is not PrimListItem p) return;

            shell.Map.SelectedPrim = p;

            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                // Ctrl+Double-click = properties (existing behavior)
                if (shell.ShowPrimPropertiesCommand.CanExecute(null))
                    shell.ShowPrimPropertiesCommand.Execute(null);
                e.Handled = true;
                return;
            }
            else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                // Shift+Double-click = height dialog
                PrimHeight_Click(sender!, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            // Default: center viewport
            MapViewControl.CenterOnPixel(p.PixelX, p.PixelZ);
            shell.StatusMessage = $"Centered on {p.Name} at ({p.X},{p.Z},{p.Y})";
        }

        private void PrimsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                DeleteSelectedPrim();
                e.Handled = true;
            }
            if ((e.Key == Key.LeftShift || e.Key == Key.RightShift))
            {
                // Avoid popping while editing text
                if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;

                if (DataContext is MainWindowViewModel vm && vm.Map.SelectedPrim is { } sel)
                {
                    OpenPrimHeightDialog(sel);
                    e.Handled = true;
                }
            }
        }

        private void DeletePrim_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedPrim();
        }

        private void DeleteSelectedPrim()
        {
            if (DataContext is not MainWindowViewModel shell) return;
            var sel = shell.Map.SelectedPrim;
            if (sel == null) return;

            try
            {
                // Remove from IAM
                var acc = new ObjectsAccessor(MapDataService.Instance);
                acc.DeletePrim(sel.Index);

                // Rebuild the list from disk and clear selection
                shell.Map.RefreshPrimsList();
                shell.Map.SelectedPrim = null;

                shell.StatusMessage = $"Deleted \"{sel.Name}\" (index {sel.Index}).";
            }
            catch (Exception ex)
            {
                shell.StatusMessage = "Error: failed to delete prim.";
                MessageBox.Show($"Failed to delete prim.\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void PrimProperties_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel shell) return;
            var sel = shell.Map.SelectedPrim;
            if (sel == null)
            {
                System.Diagnostics.Debug.WriteLine("[PrimProps] No selection.");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[PrimProps] Open for index={sel.Index}, flags=0x{sel.Flags:X2}, inside={sel.InsideIndex}");

            // Open the dialog with the current values
            var dlg = new Views.PrimPropertiesDialog(sel.Flags, sel.InsideIndex) { Owner = this };
            if (dlg.ShowDialog() != true)
            {
                System.Diagnostics.Debug.WriteLine("[PrimProps] Cancelled.");
                return;
            }

            byte newFlags = dlg.FlagsValue;
            byte newInside = dlg.InsideIndexValue;
            System.Diagnostics.Debug.WriteLine($"[PrimProps] OK -> flags=0x{newFlags:X2}, inside={newInside}");

            // Write back to IAM
            var acc = new ObjectsAccessor(MapDataService.Instance);
            acc.EditPrim(sel.Index, prim =>
            {
                prim.Flags = newFlags;
                prim.InsideIndex = newInside;
                return prim;
            });

            // Rebuild the VM list
            shell.Map.RefreshPrimsList();

            // Try to reselect by original index first
            PrimListItem? toSelect = null;
            if (sel.Index >= 0 && sel.Index < shell.Map.Prims.Count)
            {
                toSelect = shell.Map.Prims[sel.Index];
                // sanity: ensure it's "the same" prim (same cell & coords & prim number)
                if (toSelect.MapWhoIndex != sel.MapWhoIndex ||
                    toSelect.X != sel.X || toSelect.Z != sel.Z ||
                    toSelect.PrimNumber != sel.PrimNumber)
                {
                    toSelect = null;
                }
            }

            // If index mismatch (rare), find by tuple
            if (toSelect == null)
            {
                toSelect = shell.Map.Prims.FirstOrDefault(p =>
                    p.MapWhoIndex == sel.MapWhoIndex &&
                    p.X == sel.X && p.Z == sel.Z &&
                    p.PrimNumber == sel.PrimNumber);
            }

            shell.Map.SelectedPrim = toSelect;

            // Status
            var flagsPretty = UrbanChaosMapEditor.Models.PrimFlags.FromByte(newFlags);
            var insideLabel = newInside == 0 ? "Outside" : $"Inside={newInside}";
            shell.StatusMessage = $"Updated {sel.Name} | Flags: [{flagsPretty}] | {insideLabel}";

            System.Diagnostics.Debug.WriteLine($"[PrimProps] Applied. Reselected: {(toSelect != null ? "yes" : "no")}");
        }

        private void PrimPaletteButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel shell) return;
            if ((sender as FrameworkElement)?.Tag is not UrbanChaosMapEditor.ViewModels.PrimButton pb) return;

            shell.Map.BeginPlacePrim(pb.Number);
            shell.StatusMessage =
                $"Add Prim: {pb.Title} ({pb.Number:000}). Click on the map to place. Right-click/Esc to cancel.";

            // focus the map so the ghost shows immediately and Esc cancels placement
            MapViewControl.Focus();
        }

        // Called by the prim palette buttons in the Prims tab (Tag is a PrimButton)
        private void PrimPalette_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel shell) return;

            // PrimButton object is passed via Tag on the Button
            var primBtn = (sender as FrameworkElement)?.Tag as PrimButton;
            if (primBtn == null) return;

            // Enter placing mode with this prim number
            shell.Map.PrimNumberToPlace = primBtn.Number;
            shell.Map.IsPlacingPrim = true;

            // Clear any previous drag-ghost from move mode
            shell.Map.DragPreviewPrim = null;

            // Nice status hint
            shell.StatusMessage =
                $"Placing {primBtn.Number:D3} — {primBtn.Title}. Move mouse to choose location, click to place. Right-click to cancel.";
        }

        private void CopyPrim_Click(object? sender, RoutedEventArgs e)
        {
            CopySelectedPrim();
        }

        private void PastePrim_Click(object? sender, RoutedEventArgs e)
        {
            PastePrimAtCursor();
        }

        private void CopySelectedPrim()
        {
            if (DataContext is not MainWindowViewModel shell) return;
            var sel = shell.Map.SelectedPrim;
            if (sel == null)
            {
                shell.StatusMessage = "Nothing to copy.";
                return;
            }

            // Snapshot the fields we want to clone
            _copiedPrim = new PrimListItem
            {
                Index = sel.Index,
                MapWhoIndex = sel.MapWhoIndex,
                MapWhoRow = sel.MapWhoRow,
                MapWhoCol = sel.MapWhoCol,
                PrimNumber = sel.PrimNumber,
                Name = sel.Name,
                X = sel.X,
                Z = sel.Z,
                Y = sel.Y,
                Yaw = sel.Yaw,
                Flags = sel.Flags,
                InsideIndex = sel.InsideIndex,
                PixelX = sel.PixelX,
                PixelZ = sel.PixelZ
            };

            shell.StatusMessage = $"Copied “{sel.Name}” (#{sel.PrimNumber:000}).";
        }

        private void PastePrimAtCursor()
        {
            if (_copiedPrim == null) { if (DataContext is MainWindowViewModel s1) s1.StatusMessage = "Clipboard is empty."; return; }
            if (DataContext is not MainWindowViewModel shell) return;
            var map = shell.Map;

            // Use current cursor (game-space). Convert to UI pixels so we can reuse existing helpers.
            int uiX = MapConstants.MapPixels - map.CursorX;
            int uiY = MapConstants.MapPixels - map.CursorZ;

            // Convert to MapWho + cell-local 0..255 coords
            ObjectSpace.UiPixelsToGamePrim(uiX, uiY, out int mapWhoIndex, out byte gameX, out byte gameZ);

            // Build a new prim entry with the copied properties
            var clip = _copiedPrim;
            var newEntry = new PrimEntry
            {
                PrimNumber = (byte)clip.PrimNumber,
                MapWhoIndex = mapWhoIndex,
                X = gameX,
                Z = gameZ,
                Y = clip.Y,       // duplicate height
                Yaw = clip.Yaw,     // duplicate yaw
                Flags = clip.Flags,   // duplicate flags
                InsideIndex = clip.InsideIndex
            };

            try
            {
                var acc = new ObjectsAccessor(MapDataService.Instance);
                acc.AddPrim(newEntry);

                // Refresh and try select the newly-added instance
                map.RefreshPrimsList();

                var inserted = map.Prims.LastOrDefault(p =>
                    p.MapWhoIndex == mapWhoIndex &&
                    p.X == gameX && p.Z == gameZ &&
                    p.PrimNumber == clip.PrimNumber);

                map.SelectedPrim = inserted ?? map.SelectedPrim;

                shell.StatusMessage = $"Pasted “{clip.Name}” (#{clip.PrimNumber:000}) at cell {mapWhoIndex} (X:{gameX}, Z:{gameZ}).";
            }
            catch (System.Exception ex)
            {
                shell.StatusMessage = "Error: failed to paste object.";
                MessageBox.Show($"Failed to paste object.\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MainWindow_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            // Don’t trigger when typing in a TextBox
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
                return;

            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                if (e.Key == Key.C)
                {
                    if (DataContext is MainWindowViewModel vm &&
                        vm.Map.SelectedLightIndex >= 0)
                    {
                        CopySelectedLight();
                    }
                    else
                    {
                        CopySelectedPrim(); // existing behavior
                    }
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.V)
                {
                    if (_copiedLight != null)
                    {
                        PasteLightAtCursor();
                    }
                    else
                    {
                        PastePrimAtCursor(); // existing behavior
                    }
                    e.Handled = true;
                    return;
                }
            }

            // Keep your Delete handling exactly as-is
            if (e.Key == Key.Delete)
            {
                if (DataContext is MainWindowViewModel vm && vm.Map.SelectedLightIndex >= 0)
                {
                    DeleteSelectedLight();
                    e.Handled = true;
                    return;
                }

                // existing prim delete path
                if (DataContext is MainWindowViewModel vm2 && vm2.DeletePrimCommand.CanExecute(null))
                {
                    vm2.DeletePrimCommand.Execute(null);
                    e.Handled = true;
                }
                return;
            }

            // SHIFT → open Height dialog once per press if a prim is selected
            if ((e.Key == Key.LeftShift || e.Key == Key.RightShift) && !_heightHotkeyLatched)
            {
                // don't pop while typing in a TextBox
                if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;

                if (DataContext is MainWindowViewModel vm && vm.Map.SelectedPrim is { } sel)
                {
                    _heightHotkeyLatched = true;      // latch to avoid auto-repeat spam
                    OpenPrimHeightDialog(sel);        // opens the window once
                    e.Handled = true;
                }
            }
        }

        private void MainWindow_PreviewKeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
                _heightHotkeyLatched = false;        // release latch when SHIFT released
        }
        private void PrimHeight_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel shell) return;
            var sel = shell.Map.SelectedPrim;
            if (sel == null) return;

            // Open dialog with current height (Y)
            var dlg = new PrimHeightDialog(sel.Y) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                int newY = dlg.ResultHeight;

                var acc = new ObjectsAccessor(MapDataService.Instance);
                acc.EditPrim(sel.Index, prim =>
                {
                    prim.Y = (short)newY;
                    return prim;
                });

                // Refresh + try to keep the same prim selected
                shell.Map.RefreshPrimsList();

                PrimListItem? toSelect = null;
                if (sel.Index >= 0 && sel.Index < shell.Map.Prims.Count)
                {
                    toSelect = shell.Map.Prims[sel.Index];
                    if (toSelect.PrimNumber != sel.PrimNumber ||
                        toSelect.MapWhoIndex != sel.MapWhoIndex ||
                        toSelect.X != sel.X || toSelect.Z != sel.Z)
                    {
                        toSelect = null;
                    }
                }
                if (toSelect == null)
                {
                    toSelect = shell.Map.Prims.FirstOrDefault(p =>
                        p.MapWhoIndex == sel.MapWhoIndex &&
                        p.X == sel.X && p.Z == sel.Z &&
                        p.PrimNumber == sel.PrimNumber);
                }

                shell.Map.SelectedPrim = toSelect;
                shell.StatusMessage = $"Set height of {sel.Name} to {newY} px (storey={Math.Floor(newY / 256.0)}, offset={((newY % 256) + 256) % 256})";
            }
        }
        private void OpenPrimHeightDialog(PrimListItem sel)
        {
            // Show dialog seeded with current height
            var dlg = new Views.PrimHeightDialog(sel.Y) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            int newY = dlg.ResultHeight;

            // Write back to IAM
            var acc = new ObjectsAccessor(MapDataService.Instance);
            acc.EditPrim(sel.Index, prim =>
            {
                prim.Y = (short)newY;
                return prim;
            });

            // Refresh list and try to reselect the same prim by index
            if (DataContext is MainWindowViewModel vm)
            {
                vm.Map.RefreshPrimsList();
                if (sel.Index >= 0 && sel.Index < vm.Map.Prims.Count)
                    vm.Map.SelectedPrim = vm.Map.Prims[sel.Index];

                vm.StatusMessage = $"Set height of {sel.Name} to {newY} px";
            }
        }
        private void AddLight_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel shell) return;

            // Ensure there is a free slot before we ask for details
            var acc = new LightsAccessor(LightsDataService.Instance);
            var entries = acc.ReadAllEntries();
            int freeIdx = entries.FindIndex(le => le.Used != 1);
            if (freeIdx < 0)
            {
                shell.StatusMessage = "No free light slots (255 used).";
                MessageBox.Show("All 255 light slots are used. Delete one before adding.",
                                "Lights Full", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Show dialog
            var dlg = new AddEditLightDialog(
          initialHeight: 0,
          initialRange: 128,
          initialRed: 0,
          initialGreen: 0,
          initialBlue: 0)
            {
                Owner = Application.Current.MainWindow
            };

            if (dlg.ShowDialog() == true)
            {
                // Enter placement mode with chosen params; we place X/Z on next click
                shell.Map.BeginPlaceLight(dlg.ResultRange, dlg.ResultRed, dlg.ResultGreen, dlg.ResultBlue, dlg.ResultHeight);
                shell.StatusMessage = "Placing light… Click on the map to set X/Z. Right-click/Esc to cancel.";
                // (Optional) set focus to map so Esc works immediately
                MapViewControl.Focus();
            }
        }
        private void DeleteSelectedLight()
        {
            if (DataContext is not MainWindowViewModel shell) return;
            var map = shell.Map;
            int idx = map.SelectedLightIndex;
            if (idx < 0) return;

            try
            {
                var acc = new LightsAccessor(LightsDataService.Instance);
                var e = acc.ReadEntry(idx);
                if (e.Used == 1)
                {
                    e.Used = 0;
                    // (optional) clear fields so it looks empty in the list
                    e.Range = 0; e.Red = 0; e.Green = 0; e.Blue = 0;
                    e.X = 0; e.Y = 0; e.Z = 0;

                    acc.WriteEntry(idx, e);    // will trigger LightsBytesReset → Redraw + VM refresh
                }

                map.SelectedLightIndex = -1;
                map.RefreshLightsList();
                shell.StatusMessage = $"Deleted light at index {idx} (slot freed).";
            }
            catch (Exception ex)
            {
                shell.StatusMessage = "Error: failed to delete light.";
                MessageBox.Show($"Failed to delete light.\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void CopySelectedLight()
        {
            if (DataContext is not MainWindowViewModel shell) return;
            var vm = shell.Map;

            if (vm.SelectedLightIndex < 0)
            {
                shell.StatusMessage = "No light selected to copy.";
                return;
            }

            try
            {
                var acc = new LightsAccessor(LightsDataService.Instance);
                var e = acc.ReadEntry(vm.SelectedLightIndex);
                if (e.Used != 1)
                {
                    shell.StatusMessage = "Selected light slot is not in use.";
                    return;
                }

                // Snapshot template (we’ll override X/Z on paste)
                _copiedLight = new LightEntry
                {
                    Range = e.Range,
                    Red = e.Red,
                    Green = e.Green,
                    Blue = e.Blue,
                    Next = 0,
                    Used = 1,           // template assumes “in use”; we’ll keep it that way on paste
                    Flags = e.Flags,
                    Padding = 0,
                    X = e.X,  // not used on paste
                    Y = e.Y,  // height is kept
                    Z = e.Z   // not used on paste
                };

                shell.StatusMessage = $"Copied light  (Y={e.Y}, Range={e.Range}, RGB=({e.Red},{e.Green},{e.Blue})).";
            }
            catch (Exception ex)
            {
                shell.StatusMessage = "Error: failed to copy light.";
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        private void PasteLightAtCursor()
        {
            if (_copiedLight == null)
            {
                if (DataContext is MainWindowViewModel s0) s0.StatusMessage = "Light clipboard is empty.";
                return;
            }
            if (DataContext is not MainWindowViewModel shell) return;

            var vm = shell.Map;

            try
            {
                var acc = new LightsAccessor(LightsDataService.Instance);
                var all = acc.ReadAllEntries();

                // find a free slot
                int free = -1;
                for (int i = 0; i < all.Count; i++)
                    if (all[i].Used != 1) { free = i; break; }

                if (free < 0) { shell.StatusMessage = "No free light slots (255 max)."; return; }

                // IMPORTANT: convert cursor (game) -> UI -> world (lights)
                int uiX = MapConstants.MapPixels - vm.CursorX;
                int uiZ = MapConstants.MapPixels - vm.CursorZ;
                int worldX = LightsAccessor.UiXToWorldX(uiX);
                int worldZ = LightsAccessor.UiZToWorldZ(uiZ);

                var tpl = _copiedLight;
                var entry = new LightEntry
                {
                    Range = tpl.Range,
                    Red = tpl.Red,
                    Green = tpl.Green,
                    Blue = tpl.Blue,
                    Next = 0,
                    Used = 1,
                    Flags = tpl.Flags,
                    Padding = 0,
                    X = worldX,
                    Y = tpl.Y,      // keep copied height
                    Z = worldZ
                };

                acc.WriteEntry(free, entry);
                vm.SelectedLightIndex = free;

                shell.StatusMessage = $"Pasted light at X={worldX}, Z={worldZ} (Y={entry.Y}, Range={entry.Range}, RGB=({entry.Red},{entry.Green},{entry.Blue})).";
            }
            catch (Exception ex)
            {
                shell.StatusMessage = "Error: failed to paste light.";
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }
        private void OpenRecent_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;

            var mi = sender as MenuItem;
            // primary: Tag (as you had); fallback: DataContext (string)
            var path = (mi?.Tag as string) ?? (mi?.DataContext as string);
            if (string.IsNullOrWhiteSpace(path)) return;

            var ext = System.IO.Path.GetExtension(path);
            if (ext.Equals(".lgt", StringComparison.OrdinalIgnoreCase))
                vm.OpenLightsFromPath(path);
            else
                vm.OpenMapFromPath(path);

            // keep MRU fresh
            UrbanChaosMapEditor.Services.RecentFilesService.Instance.Add(path);
        }

        private void ClearRecent_Click(object sender, RoutedEventArgs e)
        {
            RecentFilesService.Instance.Clear();

            // keep your existing refresh
            if (DataContext is MainWindowViewModel vm)
                vm.GetType()
                  .GetMethod("SyncRecentFiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                  ?.Invoke(vm, null);
        }
        private void Recent_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;

            var m = (MenuItem)sender;
            m.Items.Clear();

            foreach (var path in vm.RecentFiles)
            {
                var mi = new MenuItem
                {
                    Header = path,      // <-- show FULL path
                    Tag = path,
                    ToolTip = path
                };
                mi.Click += OpenRecent_Click;
                m.Items.Add(mi);
            }

            m.Items.Add(new Separator());

            var clear = new MenuItem { Header = "_Clear Recent" };
            clear.Click += ClearRecent_Click;
            m.Items.Add(clear);
        }
        private void LightsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Only if a row is actually selected (double-click whitespace should do nothing)
            if (DataContext is MainWindowViewModel vm && vm.Map.SelectedLightIndex >= 0)
                EditSelectedLight();
        }

        private void EditLight_Click(object sender, RoutedEventArgs e)
        {
            EditSelectedLight();
        }

        private void EditSelectedLight()
        {
            if (DataContext is not MainWindowViewModel shell) return;
            var map = shell.Map;
            int idx = map.SelectedLightIndex;
            if (idx < 0) return;

            try
            {
                var acc = new LightsAccessor(LightsDataService.Instance);
                var entry = acc.ReadEntry(idx);

                if (entry.Used != 1)
                {
                    shell.StatusMessage = "Selected slot is empty.";
                    return;
                }

                // Seed dialog with the current values (pixels for Y)
                var dlg = new Views.AddEditLightDialog(
                    initialHeight: entry.Y,
                    initialRange: entry.Range,
                    initialRed: entry.Red,
                    initialGreen: entry.Green,
                    initialBlue: entry.Blue)
                {
                    Owner = this
                };

                if (dlg.ShowDialog() == true)
                {
                    // Apply changes; keep X/Z as-is, update Y (pixels), Range and RGB
                    entry.Range = (byte)dlg.ResultRange;
                    entry.Red = (sbyte)dlg.ResultRed;
                    entry.Green = (sbyte)dlg.ResultGreen;
                    entry.Blue = (sbyte)dlg.ResultBlue;
                    entry.Y = dlg.ResultHeight; // pixels, not storeys

                    acc.WriteEntry(idx, entry);      // triggers LightsBytesReset → UI refresh
                    map.SelectedLightIndex = idx;    // keep selection
                    map.RefreshLightsList();

                    shell.StatusMessage = $"Edited light #{idx}  Y={entry.Y}  Range={entry.Range}  RGB=({entry.Red},{entry.Green},{entry.Blue}).";
                }
            }
            catch (Exception ex)
            {
                if (DataContext is MainWindowViewModel vm2)
                    vm2.StatusMessage = "Error: failed to edit light.";
                MessageBox.Show($"Failed to edit light.\n\n{ex.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void MapLightProps_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new LightPropertiesDialog { Owner = this };
            dlg.ShowDialog();
            // No extra refresh needed: LightsAccessor.Write* already triggers LightsBytesReset.
        }

    }
}
