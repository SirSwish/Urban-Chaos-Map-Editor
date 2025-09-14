// Views/EditorTabs/BuildingsTab.xaml.cs
using System.Windows;
using System.Windows.Controls;
using UrbanChaosMapEditor.ViewModels;

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
    }
}
