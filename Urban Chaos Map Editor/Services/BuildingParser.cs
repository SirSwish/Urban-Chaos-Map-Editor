using System;
using System.Diagnostics;
using UrbanChaosMapEditor.Models;

namespace UrbanChaosMapEditor.Services
{
    /// <summary>
    /// Parses the cached building (“super-map”) block using the fixed V1 layout
    /// the renderer uses:
    ///
    ///   Header (48)
    ///     +2  : next_dbuilding (U16, 1-based count)
    ///     +4  : next_dfacet    (U16, 1-based count)
    ///     +6  : next_dstyle    (U16, full count incl. index 0)
    ///     +8  : next_paint_mem (U16, if save_type >= 17)
    ///     +10 : next_dstorey   (U16, if save_type >= 17)
    ///
    ///   DBuildings [next_dbuilding] (24 bytes each)
    ///   pad (14 bytes)
    ///   DFacets    [next_dfacet]    (26 bytes each)
    ///   dstyles    [next_dstyle]    (2 bytes each)
    ///   paint_mem  [next_paint_mem] (1 byte each; save_type >= 17)
    ///
    /// (DStorey and inside data follow; we don’t need them to display facets/styles.)
    /// </summary>
    public static class BuildingParser
    {
        private const int HeaderSize = 48;
        private const int DBuildingSize = 24;
        private const int AfterBuildingsPad = 14;
        private const int DFacetSize = 26;

        public static BuildingArrays? TryParseFromService()
        {
            var svc = MapDataService.Instance;
            if (!svc.IsLoaded) return null;

            // Use the same region math as BuildingLayer
            svc.ComputeAndCacheBuildingRegion();
            if (!svc.TryGetBuildingRegion(out var start, out var len)) return null;

            var bytes = svc.GetBytesCopy();
            if (start < 0 || len <= HeaderSize || start + len > bytes.Length) return null;

            int saveType = BitConverter.ToInt32(bytes, 0);

            // ---- Read header counters (1-based where noted) ----
            // NOTE: In these PC maps, counters live at +2,+4,+6,+8,+10 from start.
            ushort nextDBuilding = ReadU16(bytes, start + 2);
            ushort nextDFacet = ReadU16(bytes, start + 4);
            ushort nextDStyle = ReadU16(bytes, start + 6);
            ushort nextPaintMem = (saveType >= 17) ? ReadU16(bytes, start + 8) : (ushort)0;
            ushort nextDStorey = (saveType >= 17) ? ReadU16(bytes, start + 10) : (ushort)0;

            int totalBuildings = Math.Max(0, nextDBuilding - 1);
            int totalFacets = Math.Max(0, nextDFacet - 1);

            int buildingsOff = start + HeaderSize;
            int facetsOff = buildingsOff + totalBuildings * DBuildingSize + AfterBuildingsPad;

            long cursor = facetsOff;
            long blockEnd = (long)start + len;

            // Basic bounds for the facets table
            long facetsEnd = cursor + (long)totalFacets * DFacetSize;
            if (facetsOff < start || facetsEnd > blockEnd)
            {
                Debug.WriteLine(
                    $"[BuildingParser] BAD facets bounds: start=0x{start:X} len={len} " +
                    $"facetsOff=0x{facetsOff:X} facetsEnd=0x{facetsEnd:X} blockEnd=0x{blockEnd:X}");
                return null;
            }

            // ---- DBuildings ----
            var buildings = new DBuildingRec[totalBuildings];
            for (int i = 0; i < totalBuildings; i++)
            {
                int off = buildingsOff + i * DBuildingSize;
                // 1-based facet window stored in DBuilding at +12/+14
                ushort startFacet = ReadU16(bytes, off + 12);
                ushort endFacet = ReadU16(bytes, off + 14);
                buildings[i] = new DBuildingRec(startFacet, endFacet);
            }

            // ---- DFacets ----
            var facets = new DFacetRec[totalFacets];
            for (int i = 0; i < totalFacets; i++)
            {
                int off = (int)(facetsOff + i * DFacetSize);

                var type = (FacetType)bytes[off + 0];
                byte h = bytes[off + 1];

                byte x0 = bytes[off + 2];
                byte x1 = bytes[off + 3];
                short y0 = BitConverter.ToInt16(bytes, off + 4);
                short y1 = BitConverter.ToInt16(bytes, off + 6);
                byte z0 = bytes[off + 8];
                byte z1 = bytes[off + 9];

                var flags = (FacetFlags)ReadU16(bytes, off + 10);
                ushort sty = ReadU16(bytes, off + 12);
                ushort bld = ReadU16(bytes, off + 14);
                ushort st = ReadU16(bytes, off + 16);

                byte fh = bytes[off + 18];
                // (We’re not surfacing trailing bytes yet: BlockHeight/Open/Dfcache/Shake/CutHole/Counter[2])

                // Your DFacetRec ctor signature is (Type, x0,z0, x1,z1, Height, FHeight, StyleIndex, Building, Storey, Flags)
                facets[i] = new DFacetRec(
                    type, x0, z0, x1, z1,
                    h, fh, sty, bld, st, flags
                );
            }
            cursor = facetsEnd;

            // ---- dstyles (UWORD[nextDStyle]) ----
            ushort[] styles = Array.Empty<ushort>();
            if (nextDStyle > 0)
            {
                long stylesBytes = (long)nextDStyle * 2;
                long stylesEnd = cursor + stylesBytes;

                if (stylesEnd <= blockEnd)
                {
                    styles = new ushort[nextDStyle];
                    for (int i = 0; i < nextDStyle; i++)
                    {
                        styles[i] = ReadU16(bytes, (int)(cursor + i * 2));
                    }
                    cursor = stylesEnd;
                }
                else
                {
                    Debug.WriteLine(
                        $"[BuildingParser] styles OOB: cursor=0x{cursor:X} nextDStyle={nextDStyle} blockEnd=0x{blockEnd:X}");
                }
            }

            // ---- paint_mem (UBYTE[nextPaintMem]) ----
            byte[] paintMem = Array.Empty<byte>();
            if (saveType >= 17 && nextPaintMem > 0)
            {
                long pmEnd = cursor + nextPaintMem;
                if (pmEnd <= blockEnd)
                {
                    paintMem = new byte[nextPaintMem];
                    Buffer.BlockCopy(bytes, (int)cursor, paintMem, 0, nextPaintMem);
                    cursor = pmEnd;
                }
                else
                {
                    Debug.WriteLine(
                        $"[BuildingParser] paint_mem OOB: cursor=0x{cursor:X} size={nextPaintMem} blockEnd=0x{blockEnd:X}");
                }
            }

            // (DStorey would follow paint_mem; not needed for facet listing/highlighting.)

            Debug.WriteLine(
                $"[BuildingParser] OK: B={totalBuildings} F={totalFacets} styles={styles.Length} " +
                $"paint_mem={paintMem.Length} saveType={saveType} region=[0x{start:X}..0x{(start + len):X})");

            return new BuildingArrays
            {
                StartOffset = start,
                Length = len,
                Buildings = buildings,
                Facets = facets,
                Styles = styles,
                PaintMem = paintMem,

                NextDBuilding = nextDBuilding,
                NextDFacet = nextDFacet,
                NextDStyle = nextDStyle,
                NextPaintMem = nextPaintMem,
                NextDStorey = nextDStorey,
                SaveType = saveType
            };
        }

        private static ushort ReadU16(byte[] b, int off)
            => (ushort)(b[off] | (b[off + 1] << 8));
    }
}
