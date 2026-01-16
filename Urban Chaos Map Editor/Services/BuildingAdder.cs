// /Services/BuildingAdder.cs
// Helper class for adding buildings and facets to the map
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.Services.DataServices;
using UrbanChaosMapEditor.Views.Dialogs.Buildings;

namespace UrbanChaosMapEditor.Services
{
    /// <summary>
    /// Handles adding new buildings and facets to the map.
    /// </summary>
    public sealed class BuildingAdder
    {
        private const int HeaderSize = 48;
        private const int DBuildingSize = 24;
        private const int AfterBuildingsPad = 14;
        private const int DFacetSize = 26;

        private readonly MapDataService _svc;

        public BuildingAdder(MapDataService svc)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));
        }

        /// <summary>
        /// Adds a new empty building to the map.
        /// Returns the new building's 1-based ID, or -1 on failure.
        /// </summary>
        public int TryAddBuilding(BuildingType type)
        {
            if (!_svc.IsLoaded)
                return -1;

            var acc = new BuildingsAccessor(_svc);
            var snap = acc.ReadSnapshot();

            if (snap.Buildings == null)
                return -1;

            var bytes = _svc.GetBytesCopy();
            int blockStart = snap.StartOffset;
            int saveType = BitConverter.ToInt32(bytes, 0);

            // Read current header counters
            ushort oldNextBuilding = ReadU16(bytes, blockStart + 2);
            ushort oldNextFacet = ReadU16(bytes, blockStart + 4);

            // Calculate offsets
            int buildingsOff = blockStart + HeaderSize;
            int oldBuildingsEnd = buildingsOff + (oldNextBuilding - 1) * DBuildingSize;
            int padOff = oldBuildingsEnd;
            int facetsOff = padOff + AfterBuildingsPad;

            // The new building ID (1-based)
            int newBuildingId1 = oldNextBuilding;

            // Create new building record (24 bytes)
            // StartFacet = EndFacet = NextDFacet (empty range)
            var newBuilding = new byte[DBuildingSize];
            WriteU16(newBuilding, 0, oldNextFacet);  // StartFacet
            WriteU16(newBuilding, 2, oldNextFacet);  // EndFacet (same = empty)
            // WorldX/Y/Z at offsets 4-15 = 0 (already zero)
            WriteU16(newBuilding, 16, 0);            // Walkable = none
            newBuilding[18] = 0;                      // Counter0
            newBuilding[19] = 0;                      // Counter1
            newBuilding[20] = 0;                      // Ware
            newBuilding[21] = (byte)type;             // Type
            // Bytes 22-23 padding

            // Everything from the pad onwards stays the same, just shifted
            int afterBuildingsLen = bytes.Length - padOff;

            // Build new file
            using var ms = new System.IO.MemoryStream();

            // 1. Copy file header + tiles (everything up to building block header)
            ms.Write(bytes, 0, blockStart);

            // 2. Write building block header with updated NextDBuilding
            var header = new byte[HeaderSize];
            Buffer.BlockCopy(bytes, blockStart, header, 0, HeaderSize);
            WriteU16(header, 2, (ushort)(oldNextBuilding + 1)); // Increment NextDBuilding
            ms.Write(header, 0, HeaderSize);

            // 3. Write existing buildings
            if (oldNextBuilding > 1)
                ms.Write(bytes, buildingsOff, (oldNextBuilding - 1) * DBuildingSize);

            // 4. Write new building
            ms.Write(newBuilding, 0, DBuildingSize);

            // 5. Copy everything from pad onwards (pad, facets, styles, paint, storeys, indoors, walkables, objects, tail)
            ms.Write(bytes, padOff, afterBuildingsLen);

            var newBytes = ms.ToArray();

            Debug.WriteLine($"[BuildingAdder] Added building #{newBuildingId1} type={type}. " +
                           $"Old file: {bytes.Length} bytes, new file: {newBytes.Length} bytes " +
                           $"(should be +{DBuildingSize} bytes)");

            if (newBytes.Length != bytes.Length + DBuildingSize)
            {
                Debug.WriteLine($"[BuildingAdder] WARNING: Size mismatch! Expected {bytes.Length + DBuildingSize}, got {newBytes.Length}");
            }

            _svc.ReplaceBytes(newBytes);

            BuildingsChangeBus.Instance.NotifyBuildingChanged(newBuildingId1, BuildingChangeType.Added);
            BuildingsChangeBus.Instance.NotifyChanged();

            return newBuildingId1;
        }

        /// <summary>
        /// Adds multiple facets to a building.
        /// </summary>
        public AddFacetsResult TryAddFacets(int buildingId1, List<(byte x0, byte z0, byte x1, byte z1)> coords, FacetTemplate template)
        {
            if (!_svc.IsLoaded)
                return AddFacetsResult.Fail("No map loaded.");

            if (coords == null || coords.Count == 0)
                return AddFacetsResult.Fail("No facets to add.");

            var acc = new BuildingsAccessor(_svc);
            var snap = acc.ReadSnapshot();

            if (snap.Buildings == null || buildingId1 < 1 || buildingId1 > snap.Buildings.Length)
                return AddFacetsResult.Fail($"Building #{buildingId1} not found.");

            var bytes = _svc.GetBytesCopy();
            int blockStart = snap.StartOffset;
            int saveType = BitConverter.ToInt32(bytes, 0);

            // Read current header counters
            ushort oldNextBuilding = ReadU16(bytes, blockStart + 2);
            ushort oldNextFacet = ReadU16(bytes, blockStart + 4);
            ushort oldNextStyle = ReadU16(bytes, blockStart + 6);
            ushort oldNextPaintMem = (saveType >= 17) ? ReadU16(bytes, blockStart + 8) : (ushort)0;
            ushort oldNextStorey = (saveType >= 17) ? ReadU16(bytes, blockStart + 10) : (ushort)0;

            // Calculate offsets
            int buildingsOff = blockStart + HeaderSize;
            int facetsOff = buildingsOff + (oldNextBuilding - 1) * DBuildingSize + AfterBuildingsPad;
            int stylesOff = facetsOff + (oldNextFacet - 1) * DFacetSize;

            // Get the target building's current facet range
            int buildingRecOff = buildingsOff + (buildingId1 - 1) * DBuildingSize;
            ushort oldStartFacet = ReadU16(bytes, buildingRecOff);
            ushort oldEndFacet = ReadU16(bytes, buildingRecOff + 2);

            // New facets will be inserted at the end of this building's range
            // We need to shift facets of buildings that come after
            int insertPosition = oldEndFacet; // 1-based position where new facets start
            int facetCount = coords.Count;

            // Create new facet records
            var newFacets = new List<byte[]>();
            for (int i = 0; i < coords.Count; i++)
            {
                var (x0, z0, x1, z1) = coords[i];
                var facetBytes = new byte[DFacetSize];

                facetBytes[0] = (byte)template.Type;     // FacetType
                facetBytes[1] = template.Height;          // Height
                facetBytes[2] = x0;                       // X0
                facetBytes[3] = x1;                       // X1
                WriteS16(facetBytes, 4, template.Y0);     // Y0
                WriteS16(facetBytes, 6, template.Y1);     // Y1
                facetBytes[8] = z0;                       // Z0
                facetBytes[9] = z1;                       // Z1
                WriteU16(facetBytes, 10, (ushort)template.Flags); // Flags
                WriteU16(facetBytes, 12, template.StyleIndex);    // StyleIndex
                WriteU16(facetBytes, 14, (ushort)buildingId1);    // Building
                WriteU16(facetBytes, 16, (ushort)template.Storey); // DStorey
                facetBytes[18] = template.FHeight;        // FHeight
                facetBytes[19] = template.BlockHeight;    // BlockHeight
                facetBytes[20] = 0;                       // Open
                facetBytes[21] = 0;                       // Dfcache
                facetBytes[22] = 0;                       // Shake
                facetBytes[23] = 0;                       // CutHole
                facetBytes[24] = 0;                       // Counter0
                facetBytes[25] = 0;                       // Counter1

                newFacets.Add(facetBytes);
            }

            // Build new file with inserted facets
            using var ms = new System.IO.MemoryStream();

            // 1. Copy file header + tiles + building block header
            ms.Write(bytes, 0, blockStart + HeaderSize);

            // Update header: NextDFacet increases by facetCount
            ms.Position = blockStart + 4;
            WriteU16ToStream(ms, (ushort)(oldNextFacet + facetCount));
            ms.Position = ms.Length;

            // 2. Write buildings with updated facet ranges
            for (int bldIdx = 0; bldIdx < oldNextBuilding - 1; bldIdx++)
            {
                int srcOff = buildingsOff + bldIdx * DBuildingSize;
                var bldBytes = new byte[DBuildingSize];
                Buffer.BlockCopy(bytes, srcOff, bldBytes, 0, DBuildingSize);

                int bldId1 = bldIdx + 1;
                ushort start = ReadU16(bldBytes, 0);
                ushort end = ReadU16(bldBytes, 2);

                if (bldId1 == buildingId1)
                {
                    // This is our target building - expand its range
                    WriteU16(bldBytes, 2, (ushort)(end + facetCount));
                }
                else if (start >= insertPosition)
                {
                    // Building comes after insertion point - shift its range
                    WriteU16(bldBytes, 0, (ushort)(start + facetCount));
                    WriteU16(bldBytes, 2, (ushort)(end + facetCount));
                }

                ms.Write(bldBytes, 0, DBuildingSize);
            }

            // 3. Write pad
            ms.Write(new byte[AfterBuildingsPad], 0, AfterBuildingsPad);

            // 4. Write facets with insertions
            // Facets before insertion point
            int facetsBefore = insertPosition - 1; // Number of facets before our insertion
            if (facetsBefore > 0)
                ms.Write(bytes, facetsOff, facetsBefore * DFacetSize);

            // New facets
            foreach (var fb in newFacets)
                ms.Write(fb, 0, DFacetSize);

            // Facets after insertion point (with updated Building refs if needed)
            int facetsAfter = oldNextFacet - 1 - facetsBefore;
            if (facetsAfter > 0)
            {
                int afterSrcOff = facetsOff + facetsBefore * DFacetSize;
                ms.Write(bytes, afterSrcOff, facetsAfter * DFacetSize);
            }

            // 5. Copy everything after facets (styles, paint, storeys, indoors, walkables, objects, tail)
            int afterFacetsLen = bytes.Length - stylesOff;
            if (afterFacetsLen > 0)
                ms.Write(bytes, stylesOff, afterFacetsLen);

            var newBytes = ms.ToArray();

            Debug.WriteLine($"[BuildingAdder] Added {facetCount} facets to building #{buildingId1}. " +
                           $"Old file: {bytes.Length} bytes, new file: {newBytes.Length} bytes");

            _svc.ReplaceBytes(newBytes);

            BuildingsChangeBus.Instance.NotifyBuildingChanged(buildingId1, BuildingChangeType.Modified);
            BuildingsChangeBus.Instance.NotifyChanged();

            return AddFacetsResult.Success(facetCount);
        }

        private static ushort ReadU16(byte[] b, int off) =>
            (ushort)(b[off] | (b[off + 1] << 8));

        private static void WriteU16(byte[] b, int off, ushort val)
        {
            b[off] = (byte)(val & 0xFF);
            b[off + 1] = (byte)((val >> 8) & 0xFF);
        }

        private static void WriteS16(byte[] b, int off, short val)
        {
            b[off] = (byte)(val & 0xFF);
            b[off + 1] = (byte)((val >> 8) & 0xFF);
        }

        private static void WriteU16ToStream(System.IO.Stream s, ushort val)
        {
            s.WriteByte((byte)(val & 0xFF));
            s.WriteByte((byte)((val >> 8) & 0xFF));
        }
    }

    public sealed class AddFacetsResult
    {
        public bool IsSuccess { get; }
        public string? ErrorMessage { get; }
        public int FacetsAdded { get; }

        private AddFacetsResult(bool success, string? error, int count)
        {
            IsSuccess = success;
            ErrorMessage = error;
            FacetsAdded = count;
        }

        public static AddFacetsResult Success(int count) => new(true, null, count);
        public static AddFacetsResult Fail(string error) => new(false, error, 0);
    }
}