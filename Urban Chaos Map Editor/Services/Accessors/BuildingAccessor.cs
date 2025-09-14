// UrbanChaosMapEditor/Services/BuildingsAccessor.cs
using System;
using System.Diagnostics;
using UrbanChaosMapEditor.Models;

namespace UrbanChaosMapEditor.Services
{
    /// <summary>
    /// Primary accessor for the building (“super map”) block.
    /// Read-once/Write-once pattern: parses from MapDataService buffer when asked.
    /// Produces a BuildingArrays snapshot compatible with older BuildingParser usage.
    /// </summary>
    public sealed class BuildingsAccessor
    {
        private readonly MapDataService _svc;

        // Fixed V1 layout we’re targeting (same as your renderer):
        private const int HeaderSize = 48;
        private const int DBuildingSize = 24;
        private const int AfterBuildingsPad = 14;
        private const int DFacetSize = 26;

        public BuildingsAccessor(MapDataService svc)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));

            // Forward map buffer lifecycle events so overlays/VMs can react using this accessor.
            _svc.MapLoaded += (_, __) => BuildingsBytesReset?.Invoke(this, EventArgs.Empty);
            _svc.MapCleared += (_, __) => BuildingsBytesReset?.Invoke(this, EventArgs.Empty);
            _svc.MapBytesReset += (_, __) => BuildingsBytesReset?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Raised whenever the underlying building bytes may have changed
        /// (forwarded from MapDataService events).
        /// </summary>
        public event EventHandler? BuildingsBytesReset;

        /// <summary>
        /// Returns a full, immutable snapshot of the building block:
        /// DBuildings, DFacets, dstyles[], paint_mem[] and header counters.
        /// If no map is loaded or bounds are invalid, returns an empty snapshot.
        /// </summary>
        public BuildingArrays ReadSnapshot()
        {
            var empty = new BuildingArrays
            {
                StartOffset = -1,
                Length = 0,
                Buildings = Array.Empty<DBuildingRec>(),
                Facets = Array.Empty<DFacetRec>(),
                Styles = Array.Empty<ushort>(),
                PaintMem = Array.Empty<byte>(),
                NextDBuilding = 0,
                NextDFacet = 0,
                NextDStyle = 0,
                NextPaintMem = 0,
                NextDStorey = 0,
                SaveType = 0
            };

            if (!_svc.IsLoaded) return empty;

            // Use the same region math your renderer uses (cached on the service)
            _svc.ComputeAndCacheBuildingRegion();
            if (!_svc.TryGetBuildingRegion(out int start, out int len)) return empty;

            var bytes = _svc.GetBytesCopy();
            if (start < 0 || len <= HeaderSize || start + len > bytes.Length) return empty;

            int saveType = BitConverter.ToInt32(bytes, 0);

            // Header: note these counters are 1-based (index 0 unused) for DBuilding/DFacet
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

            long facetsEnd = cursor + (long)totalFacets * DFacetSize;
            if (facetsOff < start || facetsEnd > blockEnd)
            {
                Debug.WriteLine($"[BuildingsAccessor] Bad facet bounds: start=0x{start:X} len={len} " +
                                $"facetsOff=0x{facetsOff:X} facetsEnd=0x{facetsEnd:X} blockEnd=0x{blockEnd:X}");
                return empty;
            }

            // ---- DBuildings ----
            var buildings = new DBuildingRec[totalBuildings];
            for (int i = 0; i < totalBuildings; i++)
            {
                int off = buildingsOff + i * DBuildingSize;
                ushort startFacet = ReadU16(bytes, off + 12); // 1-based
                ushort endFacet = ReadU16(bytes, off + 14); // 1-based
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
                        styles[i] = ReadU16(bytes, (int)(cursor + i * 2));
                    cursor = stylesEnd;
                }
                else
                {
                    Debug.WriteLine($"[BuildingsAccessor] styles OOB: cursor=0x{cursor:X} " +
                                    $"count={nextDStyle} blockEnd=0x{blockEnd:X}");
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
                    Debug.WriteLine($"[BuildingsAccessor] paint_mem OOB: cursor=0x{cursor:X} size={nextPaintMem} " +
                                    $"blockEnd=0x{blockEnd:X}");
                }
            }

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

        // ---------- Optional: strict scan helper kept here for convenience ----------
        // If you still want the header finder to live with the accessor, keep this.
        public static bool TryFindRegion(byte[] bytes, int objectOffset, out int headerOffset, out int regionLength)
        {
            headerOffset = -1; regionLength = 0;
            if (bytes == null || objectOffset <= 0 || objectOffset > bytes.Length) return false;

            int window = Math.Min(objectOffset, 1 * 1024 * 1024);
            int start = objectOffset - window;

            for (int off = start; off <= objectOffset - HeaderSize; off += 2)
            {
                if (!PlausibleHeader(bytes, off, objectOffset, out long facetsOff, out long facetsEnd)) continue;

                int pad = (int)(objectOffset - facetsEnd);
                if (pad < 0 || pad > 16) continue;
                if (!IsZero(bytes, (int)facetsEnd, pad)) continue;

                headerOffset = off;
                regionLength = objectOffset - off;
                return true;
            }
            return false;
        }

        // ---------- Helpers ----------
        private static bool PlausibleHeader(byte[] b, int off, int objOff, out long facetsOff, out long facetsEnd)
        {
            facetsOff = facetsEnd = -1;
            if (off < 0 || off + HeaderSize > b.Length) return false;

            int nextB = ReadU16(b, off + 2);
            int nextF = ReadU16(b, off + 4);
            int buildings = nextB - 1;
            int facets = nextF - 1;

            if (buildings < 1 || facets < 8) return false;
            if (buildings > 5000 || facets > 200000) return false;

            facetsOff = off + HeaderSize + (long)buildings * DBuildingSize + AfterBuildingsPad;
            facetsEnd = facetsOff + (long)facets * DFacetSize;

            if (facetsOff < off + HeaderSize) return false;
            if (facetsEnd > objOff) return false;

            int check = Math.Min(facets, 8);
            for (int i = 0; i < check; i++)
            {
                int fOff = (int)(facetsOff + i * DFacetSize);
                if (fOff + DFacetSize > b.Length) return false;

                byte type = b[fOff + 0];
                byte h = b[fOff + 1];
                byte x0 = b[fOff + 2];
                byte x1 = b[fOff + 3];
                byte z0 = b[fOff + 8];
                byte z1 = b[fOff + 9];

                if (!((type <= 25) || type == 100)) return false;
                if (h > 64) return false;
                if (x0 > 127 || x1 > 127 || z0 > 127 || z1 > 127) return false;
            }
            return true;
        }

        private static bool IsZero(byte[] b, int off, int len)
        {
            if (len <= 0) return true;
            if (off < 0 || off + len > b.Length) return false;
            for (int i = 0; i < len; i++) if (b[off + i] != 0) return false;
            return true;
        }

        private static ushort ReadU16(byte[] b, int off)
            => (ushort)(b[off + 0] | (b[off + 1] << 8));
    }
}
