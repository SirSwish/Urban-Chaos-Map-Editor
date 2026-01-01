using System.Windows;
using UrbanChaosMapEditor.Models;

namespace UrbanChaosMapEditor.Views.Dialogs.Buildings
{
    public partial class BuildingPreviewWindow : Window
    {
        // BuildingId1 = 1-based building index
        public static readonly DependencyProperty BuildingId1Property =
            DependencyProperty.Register(
                nameof(BuildingId1),
                typeof(int),
                typeof(BuildingPreviewWindow),
                new PropertyMetadata(0));

        public int BuildingId1
        {
            get => (int)GetValue(BuildingId1Property);
            set => SetValue(BuildingId1Property, value);
        }

        // Raw DBuildingRec snapshot
        public static readonly DependencyProperty BuildingProperty =
            DependencyProperty.Register(
                nameof(Building),
                typeof(DBuildingRec),
                typeof(BuildingPreviewWindow),
                new PropertyMetadata(default(DBuildingRec)));

        public DBuildingRec Building
        {
            get => (DBuildingRec)GetValue(BuildingProperty);
            set => SetValue(BuildingProperty, value);
        }

        public BuildingPreviewWindow(DBuildingRec building, int buildingId1)
        {
            InitializeComponent();

            // push data into the DPs before the bindings activate
            Building = building;
            BuildingId1 = buildingId1;
        }
    }
}
