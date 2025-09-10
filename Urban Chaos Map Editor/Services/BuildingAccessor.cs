using System;
using System.Collections.Generic;
using UrbanChaosMapEditor.Models;

namespace UrbanChaosMapEditor.Services
{
    /// <summary>
    /// Minimal, file-layout-driven reader for the building (“super map”) section.
    /// NOTE: This is a SKELETON that uses the V1 heuristics you provided.
    /// Tweak the per-record field offsets where marked “TODO: verify” after you
    /// dump a BuildingData.bin and sanity-check a few walls in a hex editor.
    /// </summary>
    public sealed class BuildingsAccessor
    {
        private readonly MapDataService _svc;

        public BuildingsAccessor(MapDataService svc) => _svc = svc;

        // Heuristic, from your V1 notes
        private const int HeaderSize = 48;           // building header bytes
        private const int DBuildingSize = 24;        // bytes per DBuilding
        private const int WallRecordSize = 26;       // bytes per “wall” record (V1)
        private const int AfterBuildingsPad = 14;    // small pad before walls block (V1)

        /// <summary>
        /// Returns wall line segments (already in UI pixels) and a guessed "kind".
        /// </summary>
        public List<WallSegment> ReadWallSegments(int buildingHeaderOffset)
        {
            var list = new List<WallSegment>();
            var s = _svc.GetBytesCopy();
            if (buildingHeaderOffset < 0 || buildingHeaderOffset + HeaderSize > s.Length)
                return list;

            // Header: next_buildings, next_facets/walls (1-based) at +2, +4 (little-endian)
            int totalBuildings = ReadUInt16(s, buildingHeaderOffset + 2) - 1;
            int totalWalls = ReadUInt16(s, buildingHeaderOffset + 4) - 1;
            if (totalBuildings < 0) totalBuildings = 0;
            if (totalWalls < 0) totalWalls = 0;

            int wallsOffset = buildingHeaderOffset + HeaderSize + totalBuildings * DBuildingSize + AfterBuildingsPad;
            int wallsEnd = wallsOffset + totalWalls * WallRecordSize;

            if (wallsOffset < 0 || wallsEnd > s.Length) return list;

            for (int i = 0; i < totalWalls; i++)
            {
                int off = wallsOffset + i * WallRecordSize;

                // >>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>
                // TODO: VERIFY FIELD OFFSETS IN YOUR BUILD
                // The following byte positions (x1,z1,x2,z2,type) are placeholders
                // based on common dumps of the V1 26-byte wall record.
                // Adjust these once you open a known wall in the hex.
                // <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<

                byte facetType = s[off + 0];  // often the first byte acts like a “type”
                byte x1 = s[off + 8];
                byte z1 = s[off + 10];
                byte x2 = s[off + 12];
                byte z2 = s[off + 14];

                var kind = GuessKind(facetType);

                // tile(0..128) → UI pixels (8192 map, 64 px/tile, flipped like your V1)
                (int uiX1, int uiZ1) = TileToUi(x1, z1);
                (int uiX2, int uiZ2) = TileToUi(x2, z2);

                list.Add(new WallSegment(uiX1, uiZ1, uiX2, uiZ2, kind));
            }

            return list;
        }

        // If you already know the header offset, pass it in. Otherwise, add your finder here.
        // For now, this is left to the caller (MapView / ViewModel) to supply.

        private static (int xUi, int zUi) TileToUi(byte tx, byte tz)
        {
            // Map is 128*64 = 8192 px; V1 used (128 - x) * 64
            int uiX = MapConstants.MapPixels - tx * 64;
            int uiZ = MapConstants.MapPixels - tz * 64;
            return (uiX, uiZ);
        }

        private static WallKind GuessKind(byte facetType) => facetType switch
        {
            9 => WallKind.Cable,
            12 => WallKind.Ladder,
            10 => WallKind.Fence,
            11 => WallKind.FenceBrick,
            13 => WallKind.FenceFlat,
            3 => WallKind.Wall,
            18 => WallKind.Door,
            21 => WallKind.OutsideDoor,
            19 => WallKind.InsideDoor,
            8 => WallKind.Skylight,
            14 => WallKind.Trench,
            16 => WallKind.Partition,
            17 => WallKind.Inside,
            2 => WallKind.Roof,
            _ => WallKind.Unknown
        };

        private static ushort ReadUInt16(byte[] b, int off)
            => (ushort)(b[off + 0] | (b[off + 1] << 8));
    }
}
