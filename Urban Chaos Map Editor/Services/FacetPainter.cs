// /Services/FacetPainter.cs
// Service for applying paint data to facets
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.Services.DataServices;

namespace UrbanChaosMapEditor.Services
{
    /// <summary>
    /// Handles applying paint data to facets by updating dstyles[], DStorey[], and paint_mem[].
    /// </summary>
    public sealed class FacetPainter
    {
        private const int HeaderSize = 48;
        private const int DBuildingSize = 24;
        private const int AfterBuildingsPad = 14;
        private const int DFacetSize = 26;
        private const int DStyleSize = 2;      // short
        private const int DStoreySize = 6;     // U16 Style; U16 PaintIndex; SBYTE Count; UBYTE pad

        private readonly MapDataService _svc;

        public FacetPainter(MapDataService svc)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));
        }

        /// <summary>
        /// Applies paint data to a facet.
        /// </summary>
        /// <param name="facetIndex1">1-based facet index</param>
        /// <param name="columnsCount">Number of columns in the facet</param>
        /// <param name="bandsCount">Number of vertical bands</param>
        /// <param name="paintData">Paint bytes per band (key=band index 0=bottom, value=byte[] per column)</param>
        /// <param name="baseStyles">Base style per band (fallback when paint byte is 0)</param>
        public FacetPaintResult ApplyPaint(
            int facetIndex1,
            int columnsCount,
            int bandsCount,
            Dictionary<int, byte[]> paintData,
            Dictionary<int, short> baseStyles)
        {
            if (!_svc.IsLoaded)
                return FacetPaintResult.Fail("No map loaded.");

            var acc = new BuildingsAccessor(_svc);
            var snap = acc.ReadSnapshot();

            if (snap.Facets == null || facetIndex1 < 1 || facetIndex1 > snap.Facets.Length)
                return FacetPaintResult.Fail($"Facet #{facetIndex1} not found.");

            var facet = snap.Facets[facetIndex1 - 1];

            Debug.WriteLine($"[FacetPainter] ===== ApplyPaint START =====");
            Debug.WriteLine($"[FacetPainter] Facet #{facetIndex1}: StyleIndex={facet.StyleIndex}, Flags=0x{(ushort)facet.Flags:X4}");
            Debug.WriteLine($"[FacetPainter] Grid: {columnsCount} columns × {bandsCount} bands");

            var bytes = _svc.GetBytesCopy();
            int blockStart = snap.StartOffset;
            int saveType = BitConverter.ToInt32(bytes, 0);

            if (saveType < 17)
                return FacetPaintResult.Fail("Map version does not support paint data (saveType < 17).");

            // Read current header counters
            ushort nextBuilding = ReadU16(bytes, blockStart + 2);
            ushort nextFacet = ReadU16(bytes, blockStart + 4);
            ushort nextStyle = ReadU16(bytes, blockStart + 6);
            ushort nextPaintMem = ReadU16(bytes, blockStart + 8);
            ushort nextStorey = ReadU16(bytes, blockStart + 10);

            Debug.WriteLine($"[FacetPainter] Header counters BEFORE:");
            Debug.WriteLine($"[FacetPainter]   NextBuilding={nextBuilding}, NextFacet={nextFacet}");
            Debug.WriteLine($"[FacetPainter]   NextStyle={nextStyle}, NextPaintMem={nextPaintMem}, NextStorey={nextStorey}");

            // Calculate offsets in the file
            // File layout: Header → Buildings → Pad → Facets → dstyles → paint_mem → dstoreys → indoors → ...
            // NOTE: dstyles uses indices 0..(nextStyle-1), so size is nextStyle * 2 (file includes all entries)
            // NOTE: paint_mem includes slot 0 (unused), so size is nextPaintMem bytes
            // NOTE: dstoreys includes slot 0 (unused), so size is nextStorey * 6 bytes
            int buildingsOff = blockStart + HeaderSize;
            int padOff = buildingsOff + (nextBuilding - 1) * DBuildingSize;
            int facetsOff = padOff + AfterBuildingsPad;
            int stylesOff = facetsOff + (nextFacet - 1) * DFacetSize;
            int paintMemOff = stylesOff + nextStyle * DStyleSize;               // dstyles has nextStyle entries
            int storeysOff = paintMemOff + nextPaintMem;                        // storeys is AFTER paint_mem (full size including slot 0)

            Debug.WriteLine($"[FacetPainter] File offsets:");
            Debug.WriteLine($"[FacetPainter]   stylesOff=0x{stylesOff:X}, paintMemOff=0x{paintMemOff:X}, storeysOff=0x{storeysOff:X}");

            // Dump current dstyles for this facet
            Debug.WriteLine($"[FacetPainter] Current dstyles values for facet:");
            bool twoTextured = (facet.Flags & FacetFlags.TwoTextured) != 0;
            bool twoSided = (facet.Flags & FacetFlags.TwoSided) != 0;
            bool hugFloor = (facet.Flags & FacetFlags.HugFloor) != 0;
            int styleIndexStep = (!hugFloor && (twoTextured || twoSided)) ? 2 : 1;
            int facetStyleStart = facet.StyleIndex;
            if (twoTextured) facetStyleStart--;

            Debug.WriteLine($"[FacetPainter]   twoTextured={twoTextured}, twoSided={twoSided}, hugFloor={hugFloor}");
            Debug.WriteLine($"[FacetPainter]   styleIndexStep={styleIndexStep}, facetStyleStart={facetStyleStart}");

            for (int band = 0; band < bandsCount; band++)
            {
                int dstyleIdx = facetStyleStart + band * styleIndexStep;
                if (dstyleIdx >= 0 && dstyleIdx < nextStyle - 1)
                {
                    int fileOff = stylesOff + dstyleIdx * DStyleSize;
                    short val = (short)(bytes[fileOff] | (bytes[fileOff + 1] << 8));
                    Debug.WriteLine($"[FacetPainter]   Band {band}: dstyles[{dstyleIdx}] = {val} (at file offset 0x{fileOff:X})");
                }
                else
                {
                    Debug.WriteLine($"[FacetPainter]   Band {band}: dstyles[{dstyleIdx}] = OUT OF RANGE (nextStyle-1={nextStyle - 1})");
                }
            }

            // Determine which bands need painting
            var bandsToPaint = new List<int>();
            for (int band = 0; band < bandsCount; band++)
            {
                if (paintData.ContainsKey(band) && paintData[band].Any(b => (b & 0x7F) != 0))
                {
                    bandsToPaint.Add(band);
                    Debug.WriteLine($"[FacetPainter] Band {band} needs painting: [{string.Join(",", paintData[band].Select(b => $"0x{b:X2}"))}]");
                }
            }

            if (bandsToPaint.Count == 0)
            {
                Debug.WriteLine("[FacetPainter] No bands need painting.");
                return FacetPaintResult.Success(0, 0);
            }

            // Calculate how much new data we need
            int newStoreysCount = bandsToPaint.Count;
            int newPaintBytesCount = bandsToPaint.Count * columnsCount;

            Debug.WriteLine($"[FacetPainter] Will allocate {newStoreysCount} DStoreys, {newPaintBytesCount} paint bytes");

            // We need to update the dstyles entries for painted bands to point to DStorey
            // And write new DStorey entries + paint_mem bytes

            // Build new data structures
            var newStoreys = new List<byte[]>();
            var newPaintBytes = new List<byte>();
            var dstylesUpdates = new Dictionary<int, short>(); // dstyles index -> new value

            ushort currentStoreyId = nextStorey; // 1-based
            ushort currentPaintMemIndex = nextPaintMem; // 1-based (but used as 0-based offset)

            foreach (int band in bandsToPaint)
            {
                // Calculate which dstyles index this band uses
                int dstyleIndex = facetStyleStart + band * styleIndexStep;

                Debug.WriteLine($"[FacetPainter] Processing band {band}:");
                Debug.WriteLine($"[FacetPainter]   dstyleIndex = {facetStyleStart} + {band} * {styleIndexStep} = {dstyleIndex}");
                Debug.WriteLine($"[FacetPainter]   Valid range: 0 to {nextStyle - 2} (nextStyle-1 = {nextStyle - 1})");

                if (dstyleIndex < 0 || dstyleIndex >= nextStyle - 1)
                {
                    Debug.WriteLine($"[FacetPainter]   WARNING: dstyleIndex {dstyleIndex} out of range, skipping");
                    continue;
                }

                // Get base style for this band
                short baseStyle = baseStyles.ContainsKey(band) ? baseStyles[band] : (short)1;

                // Create DStorey entry (6 bytes: U16 Style, U16 PaintIndex, SBYTE Count, UBYTE pad)
                // NOTE: PaintIndex is 1-based in the original engine (starts at next_paint_mem=1)
                var storeyBytes = new byte[DStoreySize];
                WriteU16(storeyBytes, 0, (ushort)baseStyle);                   // Style (base style id)
                WriteU16(storeyBytes, 2, (ushort)currentPaintMemIndex);        // PaintIndex (1-based index into paint_mem)
                storeyBytes[4] = (byte)columnsCount;                            // Count (as SBYTE, but we use positive values)
                storeyBytes[5] = 0;                                             // pad
                newStoreys.Add(storeyBytes);

                Debug.WriteLine($"[FacetPainter]   DStorey #{currentStoreyId}: Style={baseStyle}, PaintIndex={currentPaintMemIndex}, Count={columnsCount}");

                // Get paint bytes for this band - REVERSE ORDER
                // The UI stores paint bytes in visual left-to-right order, but the game engine
                // expects them in the opposite order (facet direction dependent)
                var bandPaintBytes = paintData[band];
                for (int col = columnsCount - 1; col >= 0; col--)
                {
                    byte paintByte = col < bandPaintBytes.Length ? bandPaintBytes[col] : (byte)0;
                    newPaintBytes.Add(paintByte);
                }

                // Update dstyles to point to this DStorey (negative value)
                dstylesUpdates[dstyleIndex] = (short)(-currentStoreyId);

                Debug.WriteLine($"[FacetPainter]   Will update dstyles[{dstyleIndex}] = -{currentStoreyId}");

                currentStoreyId++;
                currentPaintMemIndex += (ushort)columnsCount;
            }

            // Now build the new file
            using var ms = new System.IO.MemoryStream();

            // 1. Copy everything up to building block header
            ms.Write(bytes, 0, blockStart);

            // 2. Write updated header
            var header = new byte[HeaderSize];
            Buffer.BlockCopy(bytes, blockStart, header, 0, HeaderSize);
            WriteU16(header, 8, (ushort)(nextPaintMem + newPaintBytesCount));   // Update NextPaintMem
            WriteU16(header, 10, (ushort)(nextStorey + newStoreysCount));       // Update NextStorey
            ms.Write(header, 0, HeaderSize);

            // 3. Copy buildings
            int buildingsSize = (nextBuilding - 1) * DBuildingSize;
            if (buildingsSize > 0)
                ms.Write(bytes, buildingsOff, buildingsSize);

            // 4. Copy padding
            ms.Write(bytes, padOff, AfterBuildingsPad);

            // 5. Copy facets
            int facetsSize = (nextFacet - 1) * DFacetSize;
            if (facetsSize > 0)
                ms.Write(bytes, facetsOff, facetsSize);

            // 6. Write updated dstyles (copy existing, but update the painted ones)
            // NOTE: dstyles uses all indices 0..(nextStyle-1), so we copy nextStyle entries
            int stylesSize = nextStyle * DStyleSize;
            var stylesData = new byte[stylesSize];
            Buffer.BlockCopy(bytes, stylesOff, stylesData, 0, stylesSize);

            // Apply updates
            foreach (var kvp in dstylesUpdates)
            {
                int index = kvp.Key;
                short value = kvp.Value;
                if (index >= 0 && (index * DStyleSize + 1) < stylesData.Length)
                {
                    WriteS16(stylesData, index * DStyleSize, value);
                    Debug.WriteLine($"[FacetPainter] Updated dstyles[{index}] = {value}");
                }
            }
            ms.Write(stylesData, 0, stylesSize);

            // 7. Copy existing paint_mem (paint_mem comes BEFORE storeys in file layout!)
            // NOTE: The file includes slot 0 (unused), so we copy nextPaintMem bytes, not nextPaintMem-1
            // However, if nextPaintMem=1, there's just the unused slot 0, so we copy that 1 byte
            int existingPaintMemSize = nextPaintMem; // Copy all existing bytes including slot 0
            if (existingPaintMemSize > 0)
                ms.Write(bytes, paintMemOff, existingPaintMemSize);

            // 8. Write new paint bytes (these go at position nextPaintMem onwards)
            ms.Write(newPaintBytes.ToArray(), 0, newPaintBytes.Count);

            // 9. Copy existing DStoreys (storeys come AFTER paint_mem)
            // NOTE: The file includes slot 0 (unused), so we copy nextStorey entries, not nextStorey-1
            int existingStoreysSize = nextStorey * DStoreySize; // Copy all existing entries including slot 0
            if (existingStoreysSize > 0)
                ms.Write(bytes, storeysOff, existingStoreysSize);

            // 10. Write new DStoreys (these go at position nextStorey onwards)
            foreach (var storeyBytes in newStoreys)
                ms.Write(storeyBytes, 0, DStoreySize);

            // 11. Copy everything after storeys (indoors, walkables, objects, etc.)
            int afterStoreysOff = storeysOff + existingStoreysSize;
            int tailSize = bytes.Length - afterStoreysOff;
            if (tailSize > 0)
                ms.Write(bytes, afterStoreysOff, tailSize);

            var newBytes = ms.ToArray();

            int expectedGrowth = (newStoreysCount * DStoreySize) + newPaintBytesCount;
            int actualGrowth = newBytes.Length - bytes.Length;

            Debug.WriteLine($"[FacetPainter] File size: {bytes.Length} -> {newBytes.Length} (expected growth: {expectedGrowth}, actual: {actualGrowth})");

            if (actualGrowth != expectedGrowth)
            {
                return FacetPaintResult.Fail($"File size mismatch: expected growth {expectedGrowth}, got {actualGrowth}");
            }

            _svc.ReplaceBytes(newBytes);

            // Verify what was written by re-reading
            Debug.WriteLine($"[FacetPainter] ===== VERIFICATION =====");
            var verifyBytes = _svc.GetBytesCopy();

            // Check header
            ushort verifyNextStyle = ReadU16(verifyBytes, blockStart + 6);
            ushort verifyNextPaintMem = ReadU16(verifyBytes, blockStart + 8);
            ushort verifyNextStorey = ReadU16(verifyBytes, blockStart + 10);
            Debug.WriteLine($"[FacetPainter] Header AFTER: NextStyle={verifyNextStyle}, NextPaintMem={verifyNextPaintMem}, NextStorey={verifyNextStorey}");

            // Recalculate offsets for the NEW file structure
            // Layout: styles → paint_mem → storeys
            // NOTE: dstyles has verifyNextStyle entries (uses index 0)
            // NOTE: paint_mem includes slot 0, so size is verifyNextPaintMem
            // NOTE: storeys includes slot 0, so we read from index 1 onwards
            int newPaintMemOff = stylesOff + verifyNextStyle * DStyleSize;
            int newStoreysOff = newPaintMemOff + verifyNextPaintMem;  // Full paint_mem size including slot 0
            Debug.WriteLine($"[FacetPainter] New offsets: paintMemOff=0x{newPaintMemOff:X}, storeysOff=0x{newStoreysOff:X}");

            // Check dstyles values
            Debug.WriteLine($"[FacetPainter] dstyles values AFTER:");
            for (int band = 0; band < bandsCount; band++)
            {
                int dstyleIdx = facetStyleStart + band * styleIndexStep;
                if (dstyleIdx >= 0 && dstyleIdx < verifyNextStyle - 1)
                {
                    int fileOff = stylesOff + dstyleIdx * DStyleSize;
                    short val = (short)(verifyBytes[fileOff] | (verifyBytes[fileOff + 1] << 8));
                    Debug.WriteLine($"[FacetPainter]   Band {band}: dstyles[{dstyleIdx}] = {val}");
                }
            }

            // Check DStorey entries (6 bytes each: U16 Style, U16 PaintIndex, SBYTE Count, pad)
            // Note: Storeys are 1-based, so DStorey[1] is at offset newStoreysOff + 1*DStoreySize
            Debug.WriteLine($"[FacetPainter] DStorey entries AFTER:");
            for (int i = 1; i < verifyNextStorey; i++)  // Start from 1 (slot 0 is unused)
            {
                int storeyOff = newStoreysOff + i * DStoreySize;
                ushort style = (ushort)(verifyBytes[storeyOff] | (verifyBytes[storeyOff + 1] << 8));
                ushort paintIndex = (ushort)(verifyBytes[storeyOff + 2] | (verifyBytes[storeyOff + 3] << 8));
                sbyte count = (sbyte)verifyBytes[storeyOff + 4];
                Debug.WriteLine($"[FacetPainter]   DStorey[{i}]: Style={style}, PaintIndex={paintIndex}, Count={count}");
            }

            // Check paint_mem bytes (starting from index 1, slot 0 is unused)
            Debug.WriteLine($"[FacetPainter] paint_mem bytes AFTER:");
            int pmSize = verifyNextPaintMem - 1;  // Actual used bytes (excluding slot 0)
            if (pmSize > 0 && pmSize <= 64) // Limit output
            {
                var pmBytes = new byte[pmSize];
                Buffer.BlockCopy(verifyBytes, newPaintMemOff + 1, pmBytes, 0, pmSize);  // Skip slot 0
                Debug.WriteLine($"[FacetPainter]   paint_mem[1..{verifyNextPaintMem - 1}]: [{string.Join(",", pmBytes.Select(b => $"0x{b:X2}"))}]");
            }

            Debug.WriteLine($"[FacetPainter] ===== ApplyPaint COMPLETE =====");

            BuildingsChangeBus.Instance.NotifyBuildingChanged(facet.Building, BuildingChangeType.Modified);
            BuildingsChangeBus.Instance.NotifyChanged();

            return FacetPaintResult.Success(newStoreysCount, newPaintBytesCount);
        }

        #region Byte Helpers

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

        private static void WriteS32(byte[] b, int off, int val)
        {
            b[off] = (byte)(val & 0xFF);
            b[off + 1] = (byte)((val >> 8) & 0xFF);
            b[off + 2] = (byte)((val >> 16) & 0xFF);
            b[off + 3] = (byte)((val >> 24) & 0xFF);
        }

        #endregion
    }

    public sealed class FacetPaintResult
    {
        public bool IsSuccess { get; }
        public string? ErrorMessage { get; }
        public int StoreysAllocated { get; }
        public int PaintBytesAllocated { get; }

        private FacetPaintResult(bool success, string? error, int storeys, int paintBytes)
        {
            IsSuccess = success;
            ErrorMessage = error;
            StoreysAllocated = storeys;
            PaintBytesAllocated = paintBytes;
        }

        public static FacetPaintResult Success(int storeys, int paintBytes) =>
            new(true, null, storeys, paintBytes);

        public static FacetPaintResult Fail(string error) =>
            new(false, error, 0, 0);
    }
}