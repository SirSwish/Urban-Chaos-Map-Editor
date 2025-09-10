using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UrbanChaosMapEditor.Services;

namespace UrbanChaosMapEditor.Views
{
    public partial class AddEditLightDialog : Window, INotifyPropertyChanged
    {
        // ----- In/out -----
        public int InitialHeight { get; }
        public int ResultHeight { get; private set; }
        public byte ResultRange { get; private set; }
        public sbyte ResultRed { get; private set; }
        public sbyte ResultGreen { get; private set; }
        public sbyte ResultBlue { get; private set; }
        public int ResultOffset { get; private set; }

        // ----- Editable properties (bound to sliders) -----
        private int _range;           // 0..255
        public int Range { get => _range; set { if (Set(ref _range, Clamp(value, 0, 255))) UpdatePreview(); } }

        private int _red;             // -127..127
        public int Red { get => _red; set { if (Set(ref _red, Clamp(value, -127, 127))) UpdatePreview(); } }

        private int _green;
        public int Green { get => _green; set { if (Set(ref _green, Clamp(value, -127, 127))) UpdatePreview(); } }

        private int _blue;
        public int Blue { get => _blue; set { if (Set(ref _blue, Clamp(value, -127, 127))) UpdatePreview(); } }

        // Height state
        private int _storey; // floor division by 256 (can be negative)
        private int _offset; // 0..255
        private bool _dragging;
        private double _lastY;

        // Preview brush + info
        private Brush _previewBrush = new SolidColorBrush(Color.FromArgb(160, 128, 128, 128));
        public Brush PreviewBrush { get => _previewBrush; private set { _previewBrush = value; OnPropertyChanged(); } }

        private string _previewInfo = "";
        public string PreviewInfo { get => _previewInfo; private set { _previewInfo = value; OnPropertyChanged(); } }

        public AddEditLightDialog(
            int initialHeight,
            int initialRange = 128,
            int initialRed = 0,
            int initialGreen = 0,
            int initialBlue = 0)
        {
            InitializeComponent();
            DataContext = this;

            // Height
            InitialHeight = initialHeight;
            FromHeight(InitialHeight);
            UpdateHeightUi();

            // Sliders init
            _range = Clamp(initialRange, 0, 255);
            _red = Clamp(initialRed, -127, 127);
            _green = Clamp(initialGreen, -127, 127);
            _blue = Clamp(initialBlue, -127, 127);
            UpdatePreview();

            // Optional: tile the wall using shared_386 like PrimHeightDialog
            if (TextureCacheService.Instance.TryGetRelative("shared_386", out var img) && img != null)
            {
                Wall.Background = new ImageBrush(img)
                {
                    Stretch = Stretch.Fill,
                    TileMode = TileMode.Tile,
                    ViewportUnits = BrushMappingMode.Absolute,
                    Viewport = new Rect(0, 0, 64, 64)
                };
            }
        }

        // ----- Height helpers (same behavior as PrimHeightDialog) -----
        private void FromHeight(int h)
        {
            _storey = FloorDiv(h, 256);
            _offset = FloorMod(h, 256);
        }
        private static int FloorDiv(int a, int b)
        {
            int q = a / b; int r = a % b;
            if (r != 0 && ((r > 0) != (b > 0))) q--;
            return q;
        }
        private static int FloorMod(int a, int b)
        {
            int r = a % b; if (r < 0) r += Math.Abs(b);
            return r;
        }
        private void UpdateHeightUi()
        {
            // dot position: offset measured up from bottom; Canvas Y grows down
            double yFromTop = 256 - (_offset + Dot.Height / 2.0);
            double xCenter = (Wall.Width - Dot.Width) / 2.0;
            Canvas.SetLeft(Dot, xCenter);
            Canvas.SetTop(Dot, yFromTop);

            int total = _storey * 256 + _offset;
            LblStorey.Text = _storey.ToString();
            LblOffset.Text = _offset.ToString();
            LblTotal.Text = total.ToString();
        }

        private void Wall_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragging = true;
            _lastY = e.GetPosition(Wall).Y;
            Wall.CaptureMouse();
        }

        private void Wall_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var y = e.GetPosition(Wall).Y;
            double dy = y - _lastY; // + down, - up
            _lastY = y;

            int delta = (int)Math.Round(-dy);
            if (delta != 0)
            {
                _offset += delta;
                while (_offset >= 256) { _offset -= 256; _storey++; }
                while (_offset < 0) { _offset += 256; _storey--; }
                UpdateHeightUi();
            }
        }

        private void Wall_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            if (Wall.IsMouseCaptured) Wall.ReleaseMouseCapture();
        }

        // ----- Preview color -----
        private void UpdatePreview()
        {
            // Same mapping used in LightLayer: add 128 to signed RGB for preview
            byte r = unchecked((byte)(Red + 128));
            byte g = unchecked((byte)(Green + 128));
            byte b = unchecked((byte)(Blue + 128));
            byte a = 160; // semi-transparent for UI

            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            PreviewBrush = brush;

            PreviewInfo = $"ARGB({a},{r},{g},{b})";
        }

        // ----- OK -----
        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ResultHeight = _storey * 256 + _offset;
            ResultRange = (byte)Clamp(Range, 0, 255);
            ResultRed = (sbyte)Clamp(Red, -127, 127);
            ResultGreen = (sbyte)Clamp(Green, -127, 127);
            ResultBlue = (sbyte)Clamp(Blue, -127, 127);
            ResultOffset = _offset;
            DialogResult = true;
        }

        // ----- Utilities / INotifyPropertyChanged -----
        private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
