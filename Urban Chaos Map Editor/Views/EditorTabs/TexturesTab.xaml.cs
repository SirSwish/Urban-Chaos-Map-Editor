﻿using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.ViewModels;

namespace UrbanChaosMapEditor.Views.EditorTabs
{
    public partial class TexturesTab : UserControl
    {
        private static readonly Regex _digits = new(@"^\d+$");

        public TexturesTab()
        {
            InitializeComponent();
            // DataContext will flow from MainWindow; no need to set it here.
        }

        private void TextureThumb_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel shell) return;
            if ((sender as FrameworkElement)?.Tag is not TextureThumb thumb) return;

            var map = shell.Map;

            // Enter texture paint tool and set the chosen texture
            map.SelectedTool = EditorTool.PaintTexture;
            map.SelectedTextureGroup = thumb.Group;
            map.SelectedTextureNumber = thumb.Number;

            shell.StatusMessage = $"Texture paint: {thumb.RelativeKey} (rot {map.SelectedRotationIndex}) — click a tile to apply";
        }

        private void RotateLeft_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel shell) return;
            shell.Map.SelectedRotationIndex = (shell.Map.SelectedRotationIndex + 3) % 4; // -90°
            shell.StatusMessage = $"Rotation: {shell.Map.SelectedRotationIndex}";
        }

        private void RotateRight_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel shell) return;
            shell.Map.SelectedRotationIndex = (shell.Map.SelectedRotationIndex + 1) % 4; // +90°
            shell.StatusMessage = $"Rotation: {shell.Map.SelectedRotationIndex}";
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
