// Views/EditorTabs/BuildingsTab.xaml.cs
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.Services;
using UrbanChaosMapEditor.Services.DataServices;
using UrbanChaosMapEditor.ViewModels;
using UrbanChaosMapEditor.Views.Dialogs.Buildings;

namespace UrbanChaosMapEditor.Views.EditorTabs
{
    public partial class BuildingsTab : UserControl
    {
        public BuildingsTab()
        {
            InitializeComponent();
            Loaded += (_, __) => (DataContext as BuildingsTabViewModel)?.Refresh();
        }

        private void TreeView_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
            => (DataContext as BuildingsTabViewModel)?.HandleTreeSelection(e.NewValue);

        // NEW: open Facet Preview window on double-click a facet row
        // Open Facet or Building preview window on double-click in the tree
        // Open Facet or Building preview window on double-click in the tree
        private void TreeView_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject dep)
                return;

            var tvi = FindAncestor<TreeViewItem>(dep);
            if (tvi == null)
                return;

            var vm = DataContext as BuildingsTabViewModel;
            if (vm == null)
                return;

            var data = tvi.DataContext;

            switch (data)
            {
                // Double-click on a facet row → open facet preview window (unchanged)
                case BuildingsTabViewModel.FacetVM fvm:
                    OpenFacetPreview(fvm);
                    e.Handled = true;
                    break;
            }
        }

        private void BuildingTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is BuildingsTabViewModel vm)
            {
                vm.HandleBuildingTreeSelection(e.NewValue);
            }

            // >>> ADD THIS (clear walkable selection on building change)
            if (Application.Current.MainWindow?.DataContext is MainWindowViewModel shell)
            {
                shell.Map.SelectedWalkableId1 = 0;
            }
        }


        private static T? FindAncestor<T>(DependencyObject current)
            where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T wanted)
                    return wanted;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void OpenFacetPreview(BuildingsTabViewModel.FacetVM fvm)
        {
            var snap = new BuildingsAccessor(MapDataService.Instance).ReadSnapshot();
            int idx0 = fvm.FacetId1 - 1;
            if (idx0 < 0 || idx0 >= snap.Facets.Length) return;

            var df = snap.Facets[idx0];

            var dlg = new FacetPreviewWindow(df, fvm.FacetId1)
            {
                Owner = Application.Current.MainWindow
            };
            dlg.Show();
        }

        private void OpenBuildingPreview(BuildingsTabViewModel.BuildingVM bvm, BuildingsTabViewModel vm)
        {
            // Determine the 0-based index of this building in the VM
            int idx0 = vm.Buildings.IndexOf(bvm);
            if (idx0 < 0)
                return;

            int buildingId1 = idx0 + 1;

            var snap = new BuildingsAccessor(MapDataService.Instance).ReadSnapshot();
            if (buildingId1 < 1 || buildingId1 > snap.Buildings.Length)
                return;

            DBuildingRec building = snap.Buildings[buildingId1 - 1];

            var dlg = new BuildingPreviewWindow(building, buildingId1)
            {
                Owner = Application.Current.MainWindow
            };
            dlg.Show();
        }
        private void OpenCablePreview(BuildingsTabViewModel.FacetVM fvm)
        {
            // Prefer the raw facet stored on the VM; fall back to re-reading if needed.
            DFacetRec df = fvm.Raw;

            // If Raw was ever defaulted, you could re-resolve here using facet id,
            // but for now we assume Raw is correctly populated in the VM.
            int facetId = fvm.FacetId1;

            var dlg = new CableFacetPreviewWindow(df, facetId)
            {
                Owner = Application.Current.MainWindow
            };
            dlg.Show();
        }

        private void CableList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not BuildingsTabViewModel vm)
                return;

            if (sender is ListView lv)
            {
                var selected = lv.SelectedItem;
                vm.HandleTreeSelection(selected);
            }
        }

        private void CableList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListView lv)
                return;

            if (lv.SelectedItem is BuildingsTabViewModel.FacetVM fvm)
            {
                if (fvm.Type == FacetType.Cable)
                    OpenCablePreview(fvm);
                else
                    OpenFacetPreview(fvm);

                e.Handled = true;
            }
        }

        private void WalkablesList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not BuildingsTabViewModel vm)
                return;

            if (sender is not ListView lv)
                return;

            vm.HandleWalkableSelection(lv.SelectedItem);

            // >>> ADD THIS (bridge to the actual map VM the overlay is listening to)
            if (Application.Current.MainWindow?.DataContext is MainWindowViewModel shell)
            {
                shell.Map.SelectedWalkableId1 =
                    (lv.SelectedItem as BuildingsTabViewModel.WalkableVM)?.WalkableId1 ?? 0;
            }
        }

        private void WalkablesList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not BuildingsTabViewModel vm)
                return;

            if (WalkablesList.SelectedItem is not BuildingsTabViewModel.WalkableVM wrow)
                return;

            // Get raw arrays
            if (!MapDataService.Instance.TryGetWalkables(out var walkables, out var roofFaces4))
                return;

            int id1 = wrow.WalkableId1;
            if (id1 < 1 || id1 >= walkables.Length) // 0 is sentinel
                return;

            var dlg = new WalkablePreviewWindow(id1, walkables[id1], roofFaces4)
            {
                Owner = Application.Current.MainWindow
            };
            dlg.Show();

            e.Handled = true;
        }

        private void RoofFaces4List_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not BuildingsTabViewModel vm)
                return;

            if (RoofFaces4List.SelectedItem is not BuildingsTabViewModel.RoofFace4VM rrow)
                return;

            if (!MapDataService.Instance.TryGetWalkables(out _, out var roofFaces4))
                return;

            int idx = rrow.FaceId; // keep consistent with your VM’s FaceId meaning
            if (idx < 0 || idx >= roofFaces4.Length)
                return;

            var dlg = new RoofFace4PreviewWindow(idx, roofFaces4[idx])
            {
                Owner = Application.Current.MainWindow
            };
            dlg.Show();

            e.Handled = true;
        }


    }
}
