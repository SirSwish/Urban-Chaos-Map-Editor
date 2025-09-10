using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.ViewModels;

namespace UrbanChaosMapEditor.Views.Tabs
{
    public partial class HeightsTab : UserControl
    {
        private static readonly Regex _digits = new(@"^\d+$");

        public HeightsTab()
        {
            InitializeComponent();
        }

        private void HeightsRaise_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm) vm.Map.SelectedTool = EditorTool.RaiseHeight;
        }

        private void HeightsLower_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm) vm.Map.SelectedTool = EditorTool.LowerHeight;
        }

        private void HeightsLevel_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm) vm.Map.SelectedTool = EditorTool.LevelHeight;
        }

        private void HeightsFlatten_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm) vm.Map.SelectedTool = EditorTool.FlattenHeight;
        }

        private void HeightsDitch_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm) vm.Map.SelectedTool = EditorTool.DitchTemplate;
        }

        private void Units_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !_digits.IsMatch(e.Text);
        }

        private void Units_OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(DataFormats.Text))
            {
                var text = e.DataObject.GetData(DataFormats.Text) as string ?? "";
                if (!_digits.IsMatch(text)) e.CancelCommand();
            }
            else e.CancelCommand();
        }
    }
}
