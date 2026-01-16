// /Views/Dialogs/Buildings/AddDoorWindow.xaml.cs
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.ViewModels;

namespace UrbanChaosMapEditor.Views.Dialogs.Buildings
{
    public partial class AddDoorWindow : Window
    {
        private static readonly Regex _digitsOnly = new Regex(@"^[0-9]+$");
        private static readonly Regex _signedDigitsOnly = new Regex(@"^-?[0-9]*$");

        private readonly int _buildingId1;

        public bool WasCancelled { get; private set; } = true;

        // Properties for the door template
        public short Y0 { get; private set; } = 0;
        public short Y1 { get; private set; } = 0;
        public byte BlockHeight { get; private set; } = 16;

        public AddDoorWindow(int buildingId1)
        {
            InitializeComponent();
            _buildingId1 = buildingId1;

            TxtBuildingInfo.Text = $"Building #{buildingId1}";
        }

        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !_digitsOnly.IsMatch(e.Text);
        }

        private void SignedNumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = sender as System.Windows.Controls.TextBox;
            string newText = textBox?.Text.Insert(textBox.SelectionStart, e.Text) ?? e.Text;
            e.Handled = !string.IsNullOrEmpty(newText) &&
                        newText != "-" &&
                        !_signedDigitsOnly.IsMatch(newText);
        }

        private bool ParseAndValidate()
        {
            if (!short.TryParse(TxtY0.Text, out short y0)) y0 = 0;
            Y0 = y0;

            if (!short.TryParse(TxtY1.Text, out short y1)) y1 = 0;
            Y1 = y1;

            if (!byte.TryParse(TxtBlockHeight.Text, out byte blockHeight)) blockHeight = 16;
            BlockHeight = blockHeight;

            return true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            WasCancelled = true;
            Close();
        }

        private void BtnPlace_Click(object sender, RoutedEventArgs e)
        {
            if (!ParseAndValidate())
                return;

            // Start door placement mode
            if (Application.Current.MainWindow?.DataContext is MainWindowViewModel mainVm)
            {
                // Create the door template
                var template = new DoorTemplate
                {
                    Y0 = Y0,
                    Y1 = Y1,
                    BlockHeight = BlockHeight,
                    BuildingId1 = _buildingId1,
                    Storey = 0
                };

                // Begin door placement mode
                mainVm.Map.BeginDoorPlacement(this, template);

                // Hide this window
                Hide();

                mainVm.StatusMessage = $"Click to draw door for Building #{_buildingId1}. First click = start, Second click = end (max 1 cell). Right-click to cancel.";
            }

            WasCancelled = false;
        }

        /// <summary>
        /// Called when placement is cancelled (right-click).
        /// </summary>
        public void OnPlacementCancelled()
        {
            WasCancelled = true;
            Close();
        }

        /// <summary>
        /// Called when door is placed successfully.
        /// </summary>
        public void OnPlacementCompleted()
        {
            WasCancelled = false;
            Close();
        }
    }

    /// <summary>
    /// Template for creating a new door.
    /// </summary>
    public sealed class DoorTemplate
    {
        public short Y0 { get; init; }
        public short Y1 { get; init; }
        public byte BlockHeight { get; init; }
        public int BuildingId1 { get; init; }
        public int Storey { get; init; }
    }
}