using System.Windows;
using System.Windows.Controls;
using UrbanChaosMapEditor.ViewModels;

namespace UrbanChaosMapEditor.Views.Tabs
{
    public partial class BuildingsTab : UserControl
    {
        public BuildingsTab()
        {
            InitializeComponent();
            Loaded += (_, __) => (DataContext as BuildingsTabViewModel)?.Refresh();
        }

        private void TreeView_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is BuildingsTabViewModel vm)
                vm.HandleTreeSelection(e.NewValue);
        }
    }
}
