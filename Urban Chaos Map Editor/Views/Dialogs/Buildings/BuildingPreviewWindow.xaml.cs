using System;
using System.Windows;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.Services;
using UrbanChaosMapEditor.Services.DataServices;

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

        // ---- NEW: raw bytes (hex) ----
        public static readonly DependencyProperty RawBytesHexProperty =
            DependencyProperty.Register(
                nameof(RawBytesHex),
                typeof(string),
                typeof(BuildingPreviewWindow),
                new PropertyMetadata(string.Empty));

        public string RawBytesHex
        {
            get => (string)GetValue(RawBytesHexProperty);
            set => SetValue(RawBytesHexProperty, value);
        }

        public static readonly DependencyProperty RawBytesOffsetHexProperty =
            DependencyProperty.Register(
                nameof(RawBytesOffsetHex),
                typeof(string),
                typeof(BuildingPreviewWindow),
                new PropertyMetadata(string.Empty));

        public string RawBytesOffsetHex
        {
            get => (string)GetValue(RawBytesOffsetHexProperty);
            set => SetValue(RawBytesOffsetHexProperty, value);
        }

        public static readonly DependencyProperty RawBytesLengthProperty =
            DependencyProperty.Register(
                nameof(RawBytesLength),
                typeof(int),
                typeof(BuildingPreviewWindow),
                new PropertyMetadata(0));

        public int RawBytesLength
        {
            get => (int)GetValue(RawBytesLengthProperty);
            set => SetValue(RawBytesLengthProperty, value);
        }


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

            DataContext = this; // IMPORTANT for your {Binding ...} in XAML

            // push data into the DPs before the bindings activate
            Building = building;
            BuildingId1 = buildingId1;

            LoadRawBytes();
        }

        private void LoadRawBytes()
        {
            RawBytesHex = string.Empty;
            RawBytesOffsetHex = string.Empty;
            RawBytesLength = 0;

            var svc = MapDataService.Instance;
            if (!svc.IsLoaded)
            {
                RawBytesHex = "<no map loaded>";
                return;
            }

            var acc = new BuildingsAccessor(svc);

            if (!acc.TryGetBuildingBytes(BuildingId1, out var raw, out int off) || raw == null)
            {
                RawBytesHex = "<unavailable>";
                return;
            }

            RawBytesOffsetHex = $"0x{off:X}";
            RawBytesLength = raw.Length;
            RawBytesHex = BitConverter.ToString(raw).Replace("-", " ");
        }


    }
}
