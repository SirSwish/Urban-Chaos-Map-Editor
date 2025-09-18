// Views/EditorTabs/BuildingsTab.xaml.cs
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        private void TreeView_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if ((DataContext as BuildingsTabViewModel)?.SelectedFacet is not BuildingsTabViewModel.FacetVM fvm)
                return;

            // Resolve the *actual* DFacetRec from the latest snapshot using the 1-based id
            var snap = new BuildingsAccessor(MapDataService.Instance).ReadSnapshot();
            int idx0 = fvm.FacetId1 - 1;
            if (idx0 < 0 || idx0 >= snap.Facets.Length) return;

            var df = snap.Facets[idx0];
            var dlg = new FacetPreviewWindow(df)
            {
                Owner = Application.Current.MainWindow
            };
            dlg.Show();
        }
    }
}
