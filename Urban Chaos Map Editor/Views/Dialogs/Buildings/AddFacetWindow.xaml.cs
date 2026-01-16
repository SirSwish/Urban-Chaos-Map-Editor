// /Views/Dialogs/Buildings/AddFacetWindow.xaml.cs
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.ViewModels;

namespace UrbanChaosMapEditor.Views.Dialogs.Buildings
{
    public partial class AddFacetWindow : Window
    {
        private static readonly Regex _digitsOnly = new Regex(@"^[0-9]+$");
        private static readonly Regex _signedDigitsOnly = new Regex(@"^-?[0-9]*$");

        private readonly int _buildingId1;

        public bool WasCancelled { get; private set; } = true;

        // Properties for the facet template
        public FacetType SelectedFacetType { get; private set; } = FacetType.Normal;
        public byte Height { get; private set; } = 4;
        public byte FHeight { get; private set; } = 0;
        public byte BlockHeight { get; private set; } = 16;
        public short Y0 { get; private set; } = 0;
        public short Y1 { get; private set; } = 0;
        public ushort StyleIndex { get; private set; } = 1;
        public FacetFlags Flags { get; private set; } = FacetFlags.Unclimbable;

        public AddFacetWindow(int buildingId1)
        {
            InitializeComponent();
            _buildingId1 = buildingId1;

            TxtBuildingInfo.Text = $"Building #{buildingId1}";
            CmbFacetType.SelectedIndex = 0; // Normal

            // Hook up flag checkbox events
            ChkInvisible.Checked += OnFlagChanged;
            ChkInvisible.Unchecked += OnFlagChanged;
            ChkInside.Checked += OnFlagChanged;
            ChkInside.Unchecked += OnFlagChanged;
            ChkDlit.Checked += OnFlagChanged;
            ChkDlit.Unchecked += OnFlagChanged;
            ChkHugFloor.Checked += OnFlagChanged;
            ChkHugFloor.Unchecked += OnFlagChanged;
            ChkElectrified.Checked += OnFlagChanged;
            ChkElectrified.Unchecked += OnFlagChanged;
            ChkTwoSided.Checked += OnFlagChanged;
            ChkTwoSided.Unchecked += OnFlagChanged;
            ChkUnclimbable.Checked += OnFlagChanged;
            ChkUnclimbable.Unchecked += OnFlagChanged;
            ChkOnBuilding.Checked += OnFlagChanged;
            ChkOnBuilding.Unchecked += OnFlagChanged;
            ChkBarbTop.Checked += OnFlagChanged;
            ChkBarbTop.Unchecked += OnFlagChanged;
            ChkSeeThrough.Checked += OnFlagChanged;
            ChkSeeThrough.Unchecked += OnFlagChanged;
            ChkOpen.Checked += OnFlagChanged;
            ChkOpen.Unchecked += OnFlagChanged;
            ChkDeg90.Checked += OnFlagChanged;
            ChkDeg90.Unchecked += OnFlagChanged;
            ChkTwoTextured.Checked += OnFlagChanged;
            ChkTwoTextured.Unchecked += OnFlagChanged;
            ChkFenceCut.Checked += OnFlagChanged;
            ChkFenceCut.Unchecked += OnFlagChanged;

            UpdateFlagsDisplay();
        }

        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !_digitsOnly.IsMatch(e.Text);
        }

        private void SignedNumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = sender as TextBox;
            string newText = textBox?.Text.Insert(textBox.SelectionStart, e.Text) ?? e.Text;
            // Allow empty, "-", or valid signed number
            e.Handled = !string.IsNullOrEmpty(newText) &&
                        newText != "-" &&
                        !_signedDigitsOnly.IsMatch(newText);
        }

        private void OnFlagChanged(object sender, RoutedEventArgs e)
        {
            UpdateFlagsDisplay();
        }

        private void UpdateFlagsDisplay()
        {
            // Build flags from checkboxes
            FacetFlags flags = 0;

            if (ChkInvisible?.IsChecked == true) flags |= FacetFlags.Invisible;
            if (ChkInside?.IsChecked == true) flags |= FacetFlags.Inside;
            if (ChkDlit?.IsChecked == true) flags |= FacetFlags.Dlit;
            if (ChkHugFloor?.IsChecked == true) flags |= FacetFlags.HugFloor;
            if (ChkElectrified?.IsChecked == true) flags |= FacetFlags.Electrified;
            if (ChkTwoSided?.IsChecked == true) flags |= FacetFlags.TwoSided;
            if (ChkUnclimbable?.IsChecked == true) flags |= FacetFlags.Unclimbable;
            if (ChkOnBuilding?.IsChecked == true) flags |= FacetFlags.OnBuilding;
            if (ChkBarbTop?.IsChecked == true) flags |= FacetFlags.BarbTop;
            if (ChkSeeThrough?.IsChecked == true) flags |= FacetFlags.SeeThrough;
            if (ChkOpen?.IsChecked == true) flags |= FacetFlags.Open;
            if (ChkDeg90?.IsChecked == true) flags |= FacetFlags.Deg90;
            if (ChkTwoTextured?.IsChecked == true) flags |= FacetFlags.TwoTextured;
            if (ChkFenceCut?.IsChecked == true) flags |= FacetFlags.FenceCut;

            Flags = flags;

            if (TxtFlagsHex != null)
                TxtFlagsHex.Text = $"0x{(ushort)flags:X4}";
        }

        private bool ParseAndValidate()
        {
            // Parse facet type
            if (CmbFacetType.SelectedItem is ComboBoxItem item && item.Tag is string tagStr)
            {
                if (byte.TryParse(tagStr, out byte typeVal))
                    SelectedFacetType = (FacetType)typeVal;
            }

            // Parse height fields
            if (!byte.TryParse(TxtHeight.Text, out byte height)) height = 4;
            Height = height;

            if (!byte.TryParse(TxtFHeight.Text, out byte fheight)) fheight = 0;
            FHeight = fheight;

            if (!byte.TryParse(TxtBlockHeight.Text, out byte blockHeight)) blockHeight = 16;
            BlockHeight = blockHeight;

            if (!short.TryParse(TxtY0.Text, out short y0)) y0 = 0;
            Y0 = y0;

            if (!short.TryParse(TxtY1.Text, out short y1)) y1 = 0;
            Y1 = y1;

            if (!ushort.TryParse(TxtStyleIndex.Text, out ushort styleIndex)) styleIndex = 1;
            StyleIndex = styleIndex;

            // Flags already updated via checkboxes
            return true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            WasCancelled = true;
            Close();
        }

        private void BtnDraw_Click(object sender, RoutedEventArgs e)
        {
            if (!ParseAndValidate())
                return;

            // Start multi-draw mode
            if (Application.Current.MainWindow?.DataContext is MainWindowViewModel mainVm)
            {
                // Create the facet template
                var template = new FacetTemplate
                {
                    Type = SelectedFacetType,
                    Height = Height,
                    FHeight = FHeight,
                    BlockHeight = BlockHeight,
                    Y0 = Y0,
                    Y1 = Y1,
                    StyleIndex = StyleIndex,
                    Flags = Flags,
                    BuildingId1 = _buildingId1,
                    Storey = 0
                };

                // Begin drawing mode
                mainVm.Map.BeginFacetMultiDraw(this, template);

                // Hide this window
                Hide();

                mainVm.StatusMessage = $"Drawing facets for Building #{_buildingId1}. Click start then end point. Right-click to finish.";
            }

            WasCancelled = false;
        }

        /// <summary>
        /// Called when drawing is cancelled (right-click with no facets drawn).
        /// </summary>
        public void OnDrawCancelled()
        {
            WasCancelled = true;
            Close();
        }

        /// <summary>
        /// Called when drawing is completed (right-click after drawing facets).
        /// </summary>
        public void OnDrawCompleted(int facetsAdded)
        {
            WasCancelled = false;
            Close();
        }
    }

    /// <summary>
    /// Template for creating new facets during multi-draw mode.
    /// </summary>
    public sealed class FacetTemplate
    {
        public FacetType Type { get; init; }
        public byte Height { get; init; }
        public byte FHeight { get; init; }
        public byte BlockHeight { get; init; }
        public short Y0 { get; init; }
        public short Y1 { get; init; }
        public ushort StyleIndex { get; init; }
        public FacetFlags Flags { get; init; }
        public int BuildingId1 { get; init; }
        public int Storey { get; init; }
    }
}