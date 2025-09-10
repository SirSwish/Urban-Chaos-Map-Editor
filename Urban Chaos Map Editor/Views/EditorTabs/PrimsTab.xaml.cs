using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.Services;
using UrbanChaosMapEditor.ViewModels;

namespace UrbanChaosMapEditor.Views.Tabs
{
    public partial class PrimsTab : UserControl
    {
        public PrimsTab()
        {
            InitializeComponent();
        }

        // Double-click a row:
        // - Ctrl + Double-click => open Properties dialog
        // - Shift + Double-click => open Height dialog
        // - Plain Double-click => try to center map on prim
        private void PrimsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not MainWindowViewModel shell) return;
            if ((sender as ListView)?.SelectedItem is not PrimListItem p) return;

            shell.Map.SelectedPrim = p;

            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                PrimProperties_Click(sender!, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                PrimHeight_Click(sender!, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            // Center the map if the MapView control is available in the Window's namescope.
            var win = Window.GetWindow(this);
            if (win != null)
            {
                var mapView = LogicalTreeHelper.FindLogicalNode(win, "MapViewControl");
                // Use dynamic invoke to avoid tight coupling with the control type.
                if (mapView != null)
                {
                    try
                    {
                        mapView.GetType().GetMethod("CenterOnPixel")?
                               .Invoke(mapView, new object[] { p.PixelX, p.PixelZ });
                        shell.StatusMessage = $"Centered on {p.Name} at ({p.X},{p.Z},{p.Y})";
                    }
                    catch { /* swallow if not available */ }
                }
            }
        }

        // Only handle Shift for height dialog here; Delete is already bound to the VM command via InputBindings.
        private void PrimsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                if (Keyboard.FocusedElement is TextBox) return;
                if (DataContext is MainWindowViewModel vm && vm.Map.SelectedPrim is { } sel)
                {
                    OpenPrimHeightDialog(sel);
                    e.Handled = true;
                }
            }
        }

        private void DeletePrim_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.DeletePrimCommand.CanExecute(null))
                vm.DeletePrimCommand.Execute(null);
        }

        private void PrimProperties_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel shell) return;
            var sel = shell.Map.SelectedPrim;
            if (sel == null) return;

            var dlg = new Views.PrimPropertiesDialog(sel.Flags, sel.InsideIndex) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            byte newFlags = dlg.FlagsValue;
            byte newInside = dlg.InsideIndexValue;

            var acc = new ObjectsAccessor(MapDataService.Instance);
            acc.EditPrim(sel.Index, prim => { prim.Flags = newFlags; prim.InsideIndex = newInside; return prim; });

            shell.Map.RefreshPrimsList();

            // Try to reselect similar prim (by index first, then tuple)
            PrimListItem? toSelect = null;
            if (sel.Index >= 0 && sel.Index < shell.Map.Prims.Count)
            {
                toSelect = shell.Map.Prims[sel.Index];
                if (toSelect.MapWhoIndex != sel.MapWhoIndex ||
                    toSelect.X != sel.X || toSelect.Z != sel.Z ||
                    toSelect.PrimNumber != sel.PrimNumber)
                {
                    toSelect = null;
                }
            }
            toSelect ??= shell.Map.Prims.FirstOrDefault(p =>
                p.MapWhoIndex == sel.MapWhoIndex &&
                p.X == sel.X && p.Z == sel.Z &&
                p.PrimNumber == sel.PrimNumber);

            shell.Map.SelectedPrim = toSelect;

            var flagsPretty = PrimFlags.FromByte(newFlags);
            var insideLabel = newInside == 0 ? "Outside" : $"Inside={newInside}";
            shell.StatusMessage = $"Updated {sel.Name} | Flags: [{flagsPretty}] | {insideLabel}";
        }

        private void PrimHeight_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel shell) return;
            var sel = shell.Map.SelectedPrim;
            if (sel == null) return;

            OpenPrimHeightDialog(sel);
        }

        private void OpenPrimHeightDialog(PrimListItem sel)
        {
            var owner = Window.GetWindow(this);
            var dlg = new Views.PrimHeightDialog(sel.Y) { Owner = owner };
            if (dlg.ShowDialog() != true) return;

            int newY = dlg.ResultHeight;

            var acc = new ObjectsAccessor(MapDataService.Instance);
            acc.EditPrim(sel.Index, prim => { prim.Y = (short)newY; return prim; });

            if (DataContext is MainWindowViewModel vm)
            {
                vm.Map.RefreshPrimsList();

                // Reselect by index if possible
                if (sel.Index >= 0 && sel.Index < vm.Map.Prims.Count)
                    vm.Map.SelectedPrim = vm.Map.Prims[sel.Index];

                vm.StatusMessage = $"Set height of {sel.Name} to {newY} px";
            }
        }

        // Prim palette button click (Tag is a ViewModels.PrimButton)
        private void PrimPalette_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel shell) return;
            var primBtn = (sender as FrameworkElement)?.Tag as ViewModels.PrimButton;
            if (primBtn == null) return;

            shell.Map.PrimNumberToPlace = primBtn.Number;
            shell.Map.IsPlacingPrim = true;
            shell.Map.DragPreviewPrim = null;

            shell.StatusMessage =
                $"Placing {primBtn.Number:D3} — {primBtn.Title}. Move mouse to choose location, click to place. Right-click to cancel.";

            // focus the map so the ghost shows immediately and Esc cancels placement
            var win = Window.GetWindow(this);
            var mapView = win != null ? LogicalTreeHelper.FindLogicalNode(win, "MapViewControl") as IInputElement : null;
            mapView?.Focus();
        }
    }
}
