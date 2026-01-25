// /Services/Buildings/WalkableAdder.cs
using System;
using System.Diagnostics;
using System.IO;
using UrbanChaosMapEditor.Services.DataServices;

namespace UrbanChaosMapEditor.Services
{
    /// <summary>
    /// Result of adding a walkable.
    /// </summary>
    public sealed class AddWalkableResult
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
        public int WalkableId1 { get; init; }

        public static AddWalkableResult Ok(int walkableId1) => new() { Success = true, WalkableId1 = walkableId1 };
        public static AddWalkableResult Fail(string error) => new() { Success = false, Error = error };
    }

    /// <summary>
    /// Parameters for creating a walkable.
    /// </summary>
    public sealed class WalkableTemplate
    {
        public int BuildingId1 { get; init; }
        public byte X1 { get; init; }
        public byte Z1 { get; init; }
        public byte X2 { get; init; }
        public byte Z2 { get; init; }
        public int WorldY { get; init; }
        public byte StoreyY { get; init; }
    }

    /// <summary>
    /// Adds walkable regions (DWalkable entries) to buildings.
    /// Walkables define roof surfaces that can be walked on and grabbed.
    /// </summary>
    public sealed class WalkableAdder
    {
        private const int HeaderSize = 48;     // Buildings region header
        private const int DWalkableSize = 22;
        private const int RoofFace4Size = 10;
        private const int DBuildingSize = 24;

        private readonly MapDataService _svc;

        public WalkableAdder(MapDataService svc)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));
        }

        /// <summary>
        /// Add a new walkable to a building.
        /// </summary>
        public AddWalkableResult TryAddWalkable(WalkableTemplate template)
        {
            if (!_svc.IsLoaded)
                return AddWalkableResult.Fail("No map loaded.");

            var acc = new BuildingsAccessor(_svc);
            var snap = acc.ReadSnapshot();

            if (snap.Buildings == null)
                return AddWalkableResult.Fail("Failed to read buildings.");

            if (snap.WalkablesStart < 0)
                return AddWalkableResult.Fail("Walkables section not found.");

            if (template.BuildingId1 < 1 || template.BuildingId1 >= snap.NextDBuilding)
                return AddWalkableResult.Fail($"Invalid building ID: {template.BuildingId1}");

            var bytes = _svc.GetBytesCopy();

            // Read walkables header
            int walkablesHeaderOff = snap.WalkablesStart;
            ushort oldNextWalkable = ReadU16(bytes, walkablesHeaderOff);
            ushort oldNextRoofFace4 = ReadU16(bytes, walkablesHeaderOff + 2);

            // Calculate offsets
            int walkablesDataOff = walkablesHeaderOff + 4;
            int roofFacesDataOff = walkablesDataOff + oldNextWalkable * DWalkableSize;
            int walkablesSectionEnd = roofFacesDataOff + oldNextRoofFace4 * RoofFace4Size;

            // New walkable ID
            int newWalkableId1 = oldNextWalkable;

            // Get the building's current walkable head (for chaining)
            int buildingOff = snap.StartOffset + HeaderSize + (template.BuildingId1 - 1) * DBuildingSize;
            ushort oldWalkableHead = ReadU16(bytes, buildingOff + 16);

            // Calculate Y value: worldY >> 5
            byte walkableY = (byte)Math.Clamp(template.WorldY >> 5, 0, 255);

            Debug.WriteLine($"[WalkableAdder] Adding walkable: ID={newWalkableId1}, Building={template.BuildingId1}, " +
                           $"Rect=({template.X1},{template.Z1})->({template.X2},{template.Z2}), " +
                           $"WorldY={template.WorldY}, Y={walkableY}, Next={oldWalkableHead}");

            // Create new DWalkable (22 bytes)
            var newWalkable = new byte[DWalkableSize];

            // Offsets in DWalkable:
            // 0-1:  StartPoint (unused)
            // 2-3:  EndPoint (unused)
            // 4-5:  StartFace3 (unused)
            // 6-7:  EndFace3 (unused)
            // 8-9:  StartFace4
            // 10-11: EndFace4
            // 12:   X1
            // 13:   Z1
            // 14:   X2
            // 15:   Z2
            // 16:   Y
            // 17:   StoreyY
            // 18-19: Next
            // 20-21: Building

            WriteU16(newWalkable, 0, 0);  // StartPoint
            WriteU16(newWalkable, 2, 0);  // EndPoint
            WriteU16(newWalkable, 4, 0);  // StartFace3
            WriteU16(newWalkable, 6, 0);  // EndFace3
            WriteU16(newWalkable, 8, oldNextRoofFace4);   // StartFace4 (empty range)
            WriteU16(newWalkable, 10, oldNextRoofFace4); // EndFace4 (same = empty)

            newWalkable[12] = template.X1;
            newWalkable[13] = template.Z1;
            newWalkable[14] = template.X2;
            newWalkable[15] = template.Z2;
            newWalkable[16] = walkableY;
            newWalkable[17] = template.StoreyY;

            WriteU16(newWalkable, 18, oldWalkableHead); // Next = old head
            WriteU16(newWalkable, 20, (ushort)template.BuildingId1);

            // Build new file
            using var ms = new MemoryStream();

            // 1. Copy everything up to walkables data
            ms.Write(bytes, 0, walkablesDataOff);

            // 2. Write existing walkables
            if (oldNextWalkable > 0)
                ms.Write(bytes, walkablesDataOff, oldNextWalkable * DWalkableSize);

            // 3. Write new walkable
            ms.Write(newWalkable, 0, DWalkableSize);

            // 4. Write existing RoofFace4 entries and everything after
            int afterWalkablesLen = bytes.Length - roofFacesDataOff;
            ms.Write(bytes, roofFacesDataOff, afterWalkablesLen);

            var newBytes = ms.ToArray();

            // Update walkables header
            WriteU16(newBytes, walkablesHeaderOff, (ushort)(oldNextWalkable + 1));

            // Update building's Walkable pointer to new walkable
            WriteU16(newBytes, buildingOff + 16, (ushort)newWalkableId1);

            // Apply changes
            _svc.ReplaceBytes(newBytes);
            _svc.MarkDirty();

            Debug.WriteLine($"[WalkableAdder] Successfully added walkable {newWalkableId1}");

            return AddWalkableResult.Ok(newWalkableId1);
        }

        /// <summary>
        /// Convert world Y to walkable Y field (worldY >> 5).
        /// </summary>
        public static byte WorldYToWalkableY(int worldY)
            => (byte)Math.Clamp(worldY >> 5, 0, 255);

        /// <summary>
        /// Convert walkable Y to world Y (Y << 5).
        /// </summary>
        public static int WalkableYToWorldY(byte y)
            => y << 5;

        private static ushort ReadU16(byte[] b, int off)
            => (ushort)(b[off] | (b[off + 1] << 8));

        private static void WriteU16(byte[] b, int off, ushort v)
        {
            b[off] = (byte)(v & 0xFF);
            b[off + 1] = (byte)((v >> 8) & 0xFF);
        }
    }
}