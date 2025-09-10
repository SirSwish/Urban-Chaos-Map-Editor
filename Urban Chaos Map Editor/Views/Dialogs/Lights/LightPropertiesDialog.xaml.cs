using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.Services;
using UrbanChaosMapEditor.ViewModels;

namespace UrbanChaosMapEditor.Views
{
    public partial class LightPropertiesDialog : Window
    {
        private readonly LightsAccessor _acc = new(LightsDataService.Instance);

        private LightProperties _props;
        private LightNightColour _sky;

        // one-line debug summary bound into XAML
        public string FlagsDebug { get; private set; } = "";

        // --- NightFlag <-> checkboxes mapping (bits 0..2). Change indices if your format differs.
        private const int BIT_LAMPS_ON = 0;
        private const int BIT_DARKEN_WALLS = 1;
        private const int BIT_DAY = 2;

        public bool Night_LampsOn { get; set; }
        public bool Night_DarkenWalls { get; set; }
        public bool Night_Day { get; set; }



        public LightPropertiesDialog()
        {
            InitializeComponent();
            LoadFromAccessor();

            // read once
            var acc = new LightsAccessor(LightsDataService.Instance);
            _props = acc.ReadProperties();

            // decode flags into checkboxes (ensure your prop names match XAML)
            const uint NF_LampsOn = 1u << 0;
            const uint NF_DarkenWalls = 1u << 1;
            const uint NF_Day = 1u << 2;

            Night_LampsOn = (_props.NightFlag & NF_LampsOn) != 0;
            Night_DarkenWalls = (_props.NightFlag & NF_DarkenWalls) != 0;
            Night_Day = (_props.NightFlag & NF_Day) != 0;

            // build a readable debug line
            FlagsDebug =
                $"NightFlag=0x{_props.NightFlag:X8}  →  LampsOn={(Night_LampsOn ? "1" : "0")}, " +
                $"DarkenWalls={(Night_DarkenWalls ? "1" : "0")}, Day={(Night_Day ? "1" : "0")}";

            // dump to Output window
            Debug.WriteLine("[LightProps] " + FlagsDebug);
            Debug.WriteLine($"[LightProps] D3D=0x{_props.NightAmbD3DColour:X8}  Spec=0x{_props.NightAmbD3DSpecular:X8}  " +
                            $"AmbRGB=({_props.NightAmbRed},{_props.NightAmbGreen},{_props.NightAmbBlue})  " +
                            $"LampRGB=({_props.NightLampostRed},{_props.NightLampostGreen},{_props.NightLampostBlue})  " +
                            $"LampRadius={_props.NightLampostRadius}");

            // (optional) mirror into the main status bar
            if (Owner is MainWindow mw && mw.DataContext is MainWindowViewModel vm)
                vm.StatusMessage = FlagsDebug;

            // now expose everything to bindings
            DataContext = this;
        }

        // ------- load current values into the sliders / checkboxes --------
        private void LoadFromAccessor()
        {
            _props = _acc.ReadProperties();
            _sky = _acc.ReadNightColour();

            // D3D (uint ARGB)
            SldD3DAlpha.Value = (_props.NightAmbD3DColour >> 24) & 0xFF;
            SldD3DR.Value = (_props.NightAmbD3DColour >> 16) & 0xFF;
            SldD3DG.Value = (_props.NightAmbD3DColour >> 8) & 0xFF;
            SldD3DB.Value = _props.NightAmbD3DColour & 0xFF;

            // Specular (uint ARGB)
            SldSpecAlpha.Value = (_props.NightAmbD3DSpecular >> 24) & 0xFF;
            SldSpecR.Value = (_props.NightAmbD3DSpecular >> 16) & 0xFF;
            SldSpecG.Value = (_props.NightAmbD3DSpecular >> 8) & 0xFF;
            SldSpecB.Value = _props.NightAmbD3DSpecular & 0xFF;

            // Ambient signed -127..127
            SldAmbR.Value = _props.NightAmbRed;
            SldAmbG.Value = _props.NightAmbGreen;
            SldAmbB.Value = _props.NightAmbBlue;

            // Lamp post signed -127..127 + radius 0..255
            SldLampR.Value = _props.NightLampostRed;
            SldLampG.Value = _props.NightLampostGreen;
            SldLampB.Value = _props.NightLampostBlue;
            SldRadius.Value = _props.NightLampostRadius;

            // Sky (0..255)
            SldSkyR.Value = _sky.Red;
            SldSkyG.Value = _sky.Green;
            SldSkyB.Value = _sky.Blue;

            // Initial previews
            UpdateD3DPreview();
            UpdateAmbientPreview();
            UpdateLampPreview();
            UpdateSkyPreview();
        }



        // ------- compose helpers -------
        private static uint ComposeArgb(byte a, byte r, byte g, byte b)
            => (uint)((a << 24) | (r << 16) | (g << 8) | b);

        private static byte SignedToByte(int s127) => unchecked((byte)(s127 + 128));

        private void UpdateD3DPreview()
        {
            var a = (byte)SldD3DAlpha.Value;
            var r = (byte)SldD3DR.Value;
            var g = (byte)SldD3DG.Value;
            var b = (byte)SldD3DB.Value;

            RectD3DPreview.Fill = new SolidColorBrush(Color.FromArgb(a, r, g, b));

            var sa = (byte)SldSpecAlpha.Value;
            var sr = (byte)SldSpecR.Value;
            var sg = (byte)SldSpecG.Value;
            var sb = (byte)SldSpecB.Value;

            RectSpecPreview.Fill = new SolidColorBrush(Color.FromArgb(sa, sr, sg, sb));
        }

        private void UpdateAmbientPreview()
        {
            var r = SignedToByte((int)SldAmbR.Value);
            var g = SignedToByte((int)SldAmbG.Value);
            var b = SignedToByte((int)SldAmbB.Value);
            RectAmbPreview.Fill = new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        private void UpdateLampPreview()
        {
            var r = SignedToByte((int)SldLampR.Value);
            var g = SignedToByte((int)SldLampG.Value);
            var b = SignedToByte((int)SldLampB.Value);
            RectLampPreview.Fill = new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        private void UpdateSkyPreview()
        {
            RectSkyPreview.Fill = new SolidColorBrush(Color.FromRgb(
                (byte)SldSkyR.Value, (byte)SldSkyG.Value, (byte)SldSkyB.Value));
        }

        // ------- XAML event handlers (names match XAML) -------
        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        // IMPORTANT: overload with string so calls like OnPropertyChanged(nameof(...)) compile
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        private void OnD3DChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateD3DPreview();
        }

        private void OnAmbientChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateAmbientPreview();
        }

        private void OnLampChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateLampPreview();
        }

        private void OnSkyChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSkyPreview();
        }
        public bool FlagLampsOn
        {
            get => HasBit(_props.NightFlag, BIT_LAMPS_ON);
            set { _props.NightFlag = SetBit(_props.NightFlag, BIT_LAMPS_ON, value); OnPropertyChanged(nameof(FlagLampsOn)); }
        }
        public bool FlagDarkenWalls
        {
            get => HasBit(_props.NightFlag, BIT_DARKEN_WALLS);
            set { _props.NightFlag = SetBit(_props.NightFlag, BIT_DARKEN_WALLS, value); OnPropertyChanged(nameof(FlagDarkenWalls)); }
        }
        public bool FlagDay
        {
            get => HasBit(_props.NightFlag, BIT_DAY);
            set { _props.NightFlag = SetBit(_props.NightFlag, BIT_DAY, value); OnPropertyChanged(nameof(FlagDay)); }
        }

        private static bool HasBit(uint v, int bit) => ((v >> bit) & 1u) != 0;
        private static uint SetBit(uint v, int bit, bool on)
            => on ? (v | (1u << bit)) : (v & ~(1u << bit));
        // ------- OK / Cancel -------
        // Add this in LightPropertiesDialog.xaml.cs (anywhere inside the class)
        private void OK_Click(object sender, RoutedEventArgs e) => Ok_Click(sender, e);
        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // Push current UI values back into structures
            _props.NightAmbD3DColour = ComposeArgb(
                (byte)SldD3DAlpha.Value, (byte)SldD3DR.Value, (byte)SldD3DG.Value, (byte)SldD3DB.Value);
            _props.NightAmbD3DSpecular = ComposeArgb(
                (byte)SldSpecAlpha.Value, (byte)SldSpecR.Value, (byte)SldSpecG.Value, (byte)SldSpecB.Value);

            _props.NightAmbRed = (int)SldAmbR.Value;
            _props.NightAmbGreen = (int)SldAmbG.Value;
            _props.NightAmbBlue = (int)SldAmbB.Value;

            _props.NightLampostRed = (sbyte)SldLampR.Value;
            _props.NightLampostGreen = (sbyte)SldLampG.Value;
            _props.NightLampostBlue = (sbyte)SldLampB.Value;
            _props.NightLampostRadius = (int)SldRadius.Value;

            _sky.Red = (byte)SldSkyR.Value;
            _sky.Green = (byte)SldSkyG.Value;
            _sky.Blue = (byte)SldSkyB.Value;

            // Write once to the buffer
            _acc.WriteProperties(_props);
            _acc.WriteNightColour(_sky);

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
