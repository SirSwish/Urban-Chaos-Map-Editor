using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using UrbanChaosMapEditor.Models;

namespace UrbanChaosMapEditor.Views.Dialogs.Buildings
{
    public partial class WalkablePreviewWindow : Window
    {
        private readonly int _walkableId1;
        private readonly DWalkableRec _walkable;
        private readonly RoofFace4Rec[] _roofFaces4;

        public WalkablePreviewWindow(int walkableId1, DWalkableRec walkable, RoofFace4Rec[] roofFaces4)
        {
            InitializeComponent();

            _walkableId1 = walkableId1;
            _walkable = walkable;
            _roofFaces4 = roofFaces4 ?? Array.Empty<RoofFace4Rec>();

            Title = $"Walkable Preview – Walkable #{walkableId1}";

            HeaderTextBlock.Text = $"Walkable #{walkableId1}";
            DetailsTextBlock.Text =
                $"Building={_walkable.Building}  Rect=({_walkable.X1},{_walkable.Z1})→({_walkable.X2},{_walkable.Z2})  " +
                $"Y={_walkable.Y}  StoreyY={_walkable.StoreyY}  " +
                $"Face4=[{_walkable.StartFace4}..{_walkable.EndFace4})";

            TxtBuilding.Text = _walkable.Building.ToString(CultureInfo.InvariantCulture);
            TxtRect.Text = $"({_walkable.X1},{_walkable.Z1}) → ({_walkable.X2},{_walkable.Z2})";
            TxtY.Text = _walkable.Y.ToString(CultureInfo.InvariantCulture);
            TxtStoreyY.Text = _walkable.StoreyY.ToString(CultureInfo.InvariantCulture);
            TxtStartFace4.Text = _walkable.StartFace4.ToString(CultureInfo.InvariantCulture);
            TxtEndFace4.Text = _walkable.EndFace4.ToString(CultureInfo.InvariantCulture);

            int faceCount = Math.Max(0, _walkable.EndFace4 - _walkable.StartFace4);
            TxtFace4Count.Text = faceCount.ToString(CultureInfo.InvariantCulture);

            TxtNext.Text = _walkable.Next.ToString(CultureInfo.InvariantCulture);

            TxtOldStuff.Text =
                $"StartPoint={_walkable.StartPoint}, EndPoint={_walkable.EndPoint}, " +
                $"StartFace3={_walkable.StartFace3}, EndFace3={_walkable.EndFace3}";

            SeedRoofFacesSpan();
        }

        private sealed class RoofFace4Row
        {
            public int Index1 { get; init; }
            public short Y { get; init; }
            public string DY { get; init; } = "";
            public string FlagsHex { get; init; } = "";
            public byte RX { get; init; }
            public string RZHex { get; init; } = "";
            public short Next { get; init; }
            public string Sloped { get; init; } = "";
        }

        private void SeedRoofFacesSpan()
        {
            var rows = new List<RoofFace4Row>();

            int start = _walkable.StartFace4;
            int end = _walkable.EndFace4;

            // Guard rails: roof_faces4 length may include sentinel; we just bounds-check.
            start = Math.Max(0, start);
            end = Math.Max(start, end);
            end = Math.Min(end, _roofFaces4.Length);

            for (int i = start; i < end; i++)
            {
                var rf = _roofFaces4[i];

                bool anyDy = rf.DY0 != 0 || rf.DY1 != 0 || rf.DY2 != 0;
                bool rzSlopeBit = (rf.RZ & 0x80) != 0;

                rows.Add(new RoofFace4Row
                {
                    Index1 = i, // index-as-stored; if you want 1-based, use i+1 (but keep consistent with StartFace4)
                    Y = rf.Y,
                    DY = $"{rf.DY0},{rf.DY1},{rf.DY2}",
                    FlagsHex = $"0x{rf.DrawFlags:X2}",
                    RX = rf.RX,
                    RZHex = $"0x{rf.RZ:X2}",
                    Next = rf.Next,
                    Sloped = (anyDy || rzSlopeBit) ? "Yes" : "No"
                });
            }

            RoofFacesList.ItemsSource = rows;
        }

        private void RoofFacesList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (RoofFacesList.SelectedItem is not RoofFace4Row row)
                return;

            int idx = row.Index1;
            if (idx < 0 || idx >= _roofFaces4.Length)
                return;

            var dlg = new RoofFace4PreviewWindow(idx, _roofFaces4[idx])
            {
                Owner = this
            };
            dlg.Show();
        }
    }
}
