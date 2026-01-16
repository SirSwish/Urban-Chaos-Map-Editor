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

        #region Buildings List Handlers

        private void BuildingsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not BuildingsTabViewModel vm)
                return;

            if (sender is ListBox lb && lb.SelectedItem is BuildingsTabViewModel.BuildingVM building)
            {
                vm.HandleBuildingTreeSelection(building);

                // Auto-select first facet type if available
                if (vm.SelectedBuildingFacetGroups?.Count > 0)
                {
                    vm.SelectedFacetTypeGroup = vm.SelectedBuildingFacetGroups[0];
                }
            }

            // Clear walkable selection on building change
            if (Application.Current.MainWindow?.DataContext is MainWindowViewModel shell)
            {
                shell.Map.SelectedWalkableId1 = 0;
            }
        }

        private void BuildingsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not BuildingsTabViewModel vm)
                return;

            if (BuildingsList.SelectedItem is BuildingsTabViewModel.BuildingVM building)
            {
                OpenBuildingPreview(building, vm);
                e.Handled = true;
            }
        }

        private void BuildingsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Deselect building on right-click
            if (DataContext is BuildingsTabViewModel vm)
            {
                vm.SelectedBuilding = null;
                vm.SelectedBuildingId = 0;
                vm.SelectedFacetTypeGroup = null;
                vm.SelectedFacet = null;
            }

            // Clear the ListBox selection
            if (sender is ListBox lb)
            {
                lb.SelectedItem = null;
            }

            e.Handled = true;
        }

        #endregion

        #region Facet Types List Handlers

        private void FacetTypesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not BuildingsTabViewModel vm)
                return;

            if (sender is ListBox lb && lb.SelectedItem is BuildingsTabViewModel.FacetTypeGroupVM typeGroup)
            {
                // Auto-select first facet in the group if available
                if (typeGroup.Facets?.Count > 0)
                {
                    vm.SelectedFacet = typeGroup.Facets[0];
                }
            }
        }

        #endregion

        #region Facets List Handlers

        private void FacetsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not BuildingsTabViewModel vm)
                return;

            if (sender is ListBox lb && lb.SelectedItem is BuildingsTabViewModel.FacetVM facet)
            {
                vm.HandleTreeSelection(facet);
            }
        }

        private void FacetsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FacetsList.SelectedItem is BuildingsTabViewModel.FacetVM fvm)
            {
                OpenFacetPreview(fvm);
                e.Handled = true;
            }
        }

        #endregion

        #region Cable List Handlers

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

        #endregion

        #region Walkables and RoofFace4 Handlers

        private void WalkablesList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not BuildingsTabViewModel vm)
                return;

            if (sender is not ListView lv)
                return;

            vm.HandleWalkableSelection(lv.SelectedItem);

            // Bridge to the actual map VM the overlay is listening to
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
            if (id1 < 1 || id1 >= walkables.Length)
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

            int idx = rrow.FaceId;
            if (idx < 0 || idx >= roofFaces4.Length)
                return;

            var dlg = new RoofFace4PreviewWindow(idx, roofFaces4[idx])
            {
                Owner = Application.Current.MainWindow
            };
            dlg.Show();

            e.Handled = true;
        }

        #endregion

        #region Preview Window Helpers

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
            DFacetRec df = fvm.Raw;
            int facetId = fvm.FacetId1;

            var dlg = new CableFacetPreviewWindow(df, facetId)
            {
                Owner = Application.Current.MainWindow
            };
            dlg.Show();
        }

        #endregion

        #region Action Button Handlers

        private void BtnAddBuilding_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddBuildingDialog
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true && dialog.WasConfirmed)
            {
                var adder = new BuildingAdder(MapDataService.Instance);
                int newBuildingId = adder.TryAddBuilding(dialog.SelectedBuildingType);

                if (newBuildingId > 0)
                {
                    if (DataContext is BuildingsTabViewModel vm)
                    {
                        vm.Refresh();
                        vm.SelectedBuildingId = newBuildingId;
                    }

                    if (Application.Current.MainWindow?.DataContext is MainWindowViewModel mainVm)
                    {
                        mainVm.StatusMessage = $"Added Building #{newBuildingId} ({dialog.SelectedBuildingType}). " +
                                              "Use 'Add Facets' to draw walls and fences.";
                    }

                    MessageBox.Show($"Building #{newBuildingId} created successfully.\n\n" +
                                   $"Type: {dialog.SelectedBuildingType}\n\n" +
                                   "The building is empty. Use 'Add Facets' to draw walls and fences.",
                        "Building Added", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Failed to add building. See debug output for details.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnAddFacet_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not BuildingsTabViewModel vm)
                return;

            int buildingId = vm.SelectedBuildingId;
            if (buildingId <= 0)
            {
                MessageBox.Show("Please select a building first.",
                    "Add Facets", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var window = new AddFacetWindow(buildingId)
            {
                Owner = Window.GetWindow(this)
            };

            window.ShowDialog();

            if (!window.WasCancelled)
            {
                vm.Refresh();
            }
        }

        private void BtnAddLadder_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not BuildingsTabViewModel vm)
                return;

            int? buildingId = vm.SelectedBuildingId;
            if (buildingId == null || buildingId <= 0)
            {
                MessageBox.Show("Please select a building first.", "Add Ladder",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var addLadderWindow = new AddLadderWindow(buildingId.Value)
            {
                Owner = Window.GetWindow(this)
            };

            addLadderWindow.Show();
        }

        private void BtnAddDoor_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not BuildingsTabViewModel vm)
                return;

            int? buildingId = vm.SelectedBuildingId;
            if (buildingId == null || buildingId <= 0)
            {
                MessageBox.Show("Please select a building first.", "Add Door",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var addDoorWindow = new AddDoorWindow(buildingId.Value)
            {
                Owner = Window.GetWindow(this)
            };

            addDoorWindow.Show();
        }

        private void BtnDeleteFacet_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not BuildingsTabViewModel vm)
                return;

            var selectedFacet = vm.SelectedFacet;
            if (selectedFacet == null)
            {
                MessageBox.Show("No facet selected.", "Delete Facet",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int facetId = selectedFacet.FacetId1;
            int buildingId = selectedFacet.BuildingId;

            var deleter = new FacetDeleter(MapDataService.Instance);
            var result = deleter.TryDeleteFacet(facetId);

            if (result.IsSuccess)
            {
                if (Application.Current.MainWindow?.DataContext is MainWindowViewModel mainVm)
                {
                    mainVm.StatusMessage = $"Deleted facet #{facetId} from building #{buildingId}.";
                }

                vm.Refresh();
                SelectNextFacetInBuilding(vm, buildingId);
            }
            else
            {
                MessageBox.Show($"Failed to delete facet:\n\n{result.ErrorMessage}",
                    "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDeleteBuilding_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not BuildingsTabViewModel vm)
                return;

            int buildingId = vm.SelectedBuildingId;
            if (buildingId <= 0)
            {
                MessageBox.Show("No building selected.", "Delete Building",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var acc = new BuildingsAccessor(MapDataService.Instance);
            var snap = acc.ReadSnapshot();

            int facetCount = 0;
            if (snap.Facets != null)
            {
                foreach (var f in snap.Facets)
                {
                    if (f.Building == buildingId)
                        facetCount++;
                }
            }

            int walkableCount = 0;
            if (snap.Walkables != null)
            {
                for (int i = 1; i < snap.Walkables.Length; i++)
                {
                    if (snap.Walkables[i].Building == buildingId)
                        walkableCount++;
                }
            }

            string message = $"Delete Building #{buildingId}?\n\n" +
                            $"This will remove:\n" +
                            $"  • {facetCount} facet(s)\n" +
                            $"  • {walkableCount} walkable(s)\n" +
                            $"  • Associated roof faces\n\n" +
                            $"This action cannot be undone.";

            var confirmResult = MessageBox.Show(message, "Confirm Delete Building",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirmResult != MessageBoxResult.Yes)
                return;

            var deleter = new BuildingDeleter(MapDataService.Instance);
            var deleteResult = deleter.TryDeleteBuilding(buildingId);

            if (deleteResult.IsSuccess)
            {
                vm.SelectedBuildingId = 0;
                vm.SelectedBuilding = null;
                vm.SelectedFacet = null;
                vm.SelectedFacetTypeGroup = null;

                vm.Refresh();

                if (Application.Current.MainWindow?.DataContext is MainWindowViewModel mainVm)
                {
                    mainVm.StatusMessage = $"Deleted building #{buildingId}: " +
                                          $"{deleteResult.FacetsDeleted} facets, " +
                                          $"{deleteResult.WalkablesDeleted} walkables, " +
                                          $"{deleteResult.RoofFacesDeleted} roof faces removed.";
                }

                MessageBox.Show($"Building #{buildingId} deleted successfully.\n\n" +
                               $"Removed {deleteResult.FacetsDeleted} facet(s), " +
                               $"{deleteResult.WalkablesDeleted} walkable(s), and " +
                               $"{deleteResult.RoofFacesDeleted} roof face(s).",
                    "Building Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Failed to delete building:\n\n{deleteResult.ErrorMessage}",
                    "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Helpers

        private void SelectNextFacetInBuilding(BuildingsTabViewModel vm, int buildingId)
        {
            var building = vm.Buildings.FirstOrDefault(b => b.Id == buildingId);
            if (building == null)
            {
                vm.SelectedFacet = null;
                return;
            }

            // Find the first facet in this building via the facet groups
            var firstGroup = vm.SelectedBuildingFacetGroups?.FirstOrDefault();
            var firstFacet = firstGroup?.Facets?.FirstOrDefault();

            if (firstFacet != null)
            {
                vm.SelectedFacetTypeGroup = firstGroup;
                vm.SelectedFacet = firstFacet;
                vm.SelectedBuildingId = buildingId;
            }
            else
            {
                vm.SelectedFacet = null;
                vm.SelectedFacetTypeGroup = null;
                vm.SelectedBuildingId = buildingId;
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

        #endregion
    }
}