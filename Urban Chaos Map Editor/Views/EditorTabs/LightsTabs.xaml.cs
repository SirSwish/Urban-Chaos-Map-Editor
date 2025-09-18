using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.Services;
using UrbanChaosMapEditor.Services.DataServices;
using UrbanChaosMapEditor.ViewModels;

namespace UrbanChaosMapEditor.Views.EditorTabs
{
    public partial class LightsTab : UserControl
    {
        private LightEntry? _copiedLight;   // local clipboard

        public LightsTab()
        {
            InitializeComponent();
        }

        private void LightsList_Loaded(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainWindowViewModel;
            var mapVm = vm?.Map;
            System.Diagnostics.Debug.WriteLine(
                $"[UI] LightsList_Loaded: DC={vm?.GetType().Name ?? "null"}, MapVm?={(mapVm != null)}, " +
                $"ItemsSource?={(LightsList.ItemsSource != null)}, Items={LightsList.Items.Count}, " +
                $"BoundCount={(mapVm?.Lights?.Count ?? -1)}");

            // Fallback if binding didn’t stick for some reason
            if (LightsList.ItemsSource == null && mapVm?.Lights != null)
            {
                LightsList.ItemsSource = mapVm.Lights;
                System.Diagnostics.Debug.WriteLine($"[UI] Fallback set ItemsSource -> Items={LightsList.Items.Count}");
            }
        }

        private void LightsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                if (e.Key == Key.C) { CopyLight_Click(sender, new RoutedEventArgs()); e.Handled = true; return; }
                if (e.Key == Key.V) { PasteLight_Click(sender, new RoutedEventArgs()); e.Handled = true; return; }
            }
            if (e.Key == Key.Delete) { DeleteLight_Click(sender, new RoutedEventArgs()); e.Handled = true; }
        }

        private void LightsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Only if a row is actually selected
            if (DataContext is MainWindowViewModel vm && vm.Map.SelectedLightIndex >= 0)
                EditLight_Click(sender, new RoutedEventArgs());
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

            // Show dialog (seed with some defaults)
            var dlg = new Views.AddEditLightDialog(
                initialHeight: 0,
                initialRange: 128,
                initialRed: 0,
                initialGreen: 0,
                initialBlue: 0)
            {
                Owner = Window.GetWindow(this)
            };

            if (dlg.ShowDialog() == true)
            {
                // Enter placement mode; place X/Z on next map click
                shell.Map.BeginPlaceLight(dlg.ResultRange, dlg.ResultRed, dlg.ResultGreen, dlg.ResultBlue, dlg.ResultHeight);
                shell.StatusMessage = "Placing light… Click on the map to set X/Z. Right-click/Esc to cancel.";

                // focus the map so Esc works immediately
                var win = Window.GetWindow(this);
                var mapView = win != null ? LogicalTreeHelper.FindLogicalNode(win, "MapViewControl") as IInputElement : null;
                mapView?.Focus();
            }
        }

        private void EditLight_Click(object sender, RoutedEventArgs e)
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
                    Owner = Window.GetWindow(this)
                };

                if (dlg.ShowDialog() == true)
                {
                    entry.Range = (byte)dlg.ResultRange;
                    entry.Red = (sbyte)dlg.ResultRed;
                    entry.Green = (sbyte)dlg.ResultGreen;
                    entry.Blue = (sbyte)dlg.ResultBlue;
                    entry.Y = dlg.ResultHeight; // pixels

                    var acc2 = new LightsAccessor(LightsDataService.Instance);
                    acc2.WriteEntry(idx, entry);      // triggers LightsBytesReset → UI refresh
                    map.SelectedLightIndex = idx;     // keep selection
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

        private void DeleteLight_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel shell) return;
            var map = shell.Map;
            int idx = map.SelectedLightIndex;
            if (idx < 0) return;

            try
            {
                var acc = new LightsAccessor(LightsDataService.Instance);
                var e1 = acc.ReadEntry(idx);
                if (e1.Used == 1)
                {
                    e1.Used = 0;
                    e1.Range = 0; e1.Red = 0; e1.Green = 0; e1.Blue = 0;
                    e1.X = 0; e1.Y = 0; e1.Z = 0;
                    acc.WriteEntry(idx, e1);    // triggers LightsBytesReset
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

        private void CopyLight_Click(object? sender, RoutedEventArgs e)
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
                var e1 = acc.ReadEntry(vm.SelectedLightIndex);
                if (e1.Used != 1)
                {
                    shell.StatusMessage = "Selected light slot is not in use.";
                    return;
                }

                _copiedLight = new LightEntry
                {
                    Range = e1.Range,
                    Red = e1.Red,
                    Green = e1.Green,
                    Blue = e1.Blue,
                    Next = 0,
                    Used = 1,
                    Flags = e1.Flags,
                    Padding = 0,
                    X = e1.X,  // not used on paste
                    Y = e1.Y,
                    Z = e1.Z
                };

                shell.StatusMessage = $"Copied light  (Y={e1.Y}, Range={e1.Range}, RGB=({e1.Red},{e1.Green},{e1.Blue})).";
            }
            catch (Exception ex)
            {
                shell.StatusMessage = "Error: failed to copy light.";
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        private void PasteLight_Click(object? sender, RoutedEventArgs e)
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

                // Find a free slot
                int free = all.FindIndex(le => le.Used != 1);
                if (free < 0)
                {
                    shell.StatusMessage = "No free light slots (255 max).";
                    return;
                }

                // Convert cursor (game) -> UI -> world (lights)
                int uiX = MapConstants.MapPixels - vm.CursorX;
                int uiZ = MapConstants.MapPixels - vm.CursorZ;
                int worldX = LightsAccessor.UiXToWorldX(uiX);
                int worldZ = LightsAccessor.UiZToWorldZ(uiZ);

                var tpl = _copiedLight; // guaranteed non-null due to guard above

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
                    Y = tpl.Y,   // keep copied height
                    Z = worldZ
                };

                acc.WriteEntry(free, entry);
                vm.SelectedLightIndex = free;
                vm.RefreshLightsList();

                shell.StatusMessage =
                    $"Pasted light at X={worldX}, Z={worldZ} (Y={entry.Y}, Range={entry.Range}, RGB=({entry.Red},{entry.Green},{entry.Blue})).";
            }
            catch (Exception ex)
            {
                shell.StatusMessage = "Error: failed to paste light.";
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        private void MapLightProps_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Views.LightPropertiesDialog { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
            // LightsAccessor.Write* will raise LightsBytesReset → VM refresh if anything changes.
        }
    }
}
