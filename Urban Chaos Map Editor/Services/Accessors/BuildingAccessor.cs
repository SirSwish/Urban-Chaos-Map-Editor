// UrbanChaosMapEditor/Services/BuildingsAccessor.cs
using System;
using System.Diagnostics;
using UrbanChaosMapEditor.Models;
using UrbanChaosMapEditor.Services.DataServices;

namespace UrbanChaosMapEditor.Services
{
    /// <summary>
    /// Primary accessor for the building (“super map”) block.
    /// Produces a BuildingArrays snapshot (DBuildings, DFacets, dstyles, paint_mem, dstoreys).
    /// </summary>
    public sealed class BuildingsAccessor
    {
        private readonly MapDataService _svc;

        // Fixed V1 layout we’re targeting (same as renderer):
        private const int HeaderSize = 48;
        private const int DBuildingSize = 24;
        private const int AfterBuildingsPad = 14;

        public const int DFacetSize = 26; // expose for callers
        private const int DStoreyRecSize = 6;  // U16 StyleIndex; U16 PaintIndex; U16 Count

        public BuildingsAccessor(MapDataService svc)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));

            // Forward map buffer lifecycle events so overlays/VMs can react using this accessor.
            _svc.MapLoaded += (_, __) => BuildingsBytesReset?.Invoke(this, EventArgs.Empty);
            _svc.MapCleared += (_, __) => BuildingsBytesReset?.Invoke(this, EventArgs.Empty);
            _svc.MapBytesReset += (_, __) => BuildingsBytesReset?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Raised whenever the underlying building bytes may have changed.</summary>
        public event EventHandler? BuildingsBytesReset;

        /// <summary>
        /// Returns a full, immutable snapshot of the building block:
        /// DBuildings, DFacets, dstyles[], paint_mem[], dstoreys[] and header counters.
        /// If no map is loaded or bounds are invalid, returns an empty snapshot.
        /// </summary>
        public BuildingArrays ReadSnapshot()
        {
            var empty = new BuildingArrays
            {
                StartOffset = -1,
                Length = 0,
                FacetsStart = -1,
                Buildings = Array.Empty<DBuildingRec>(),
                Facets = Array.Empty<DFacetRec>(),
                Styles = Array.Empty<short>(),
                PaintMem = Array.Empty<byte>(),
                Storeys = Array.Empty<BuildingArrays.DStoreyRec>(),
                NextDBuilding = 0,
                NextDFacet = 0,
                NextDStyle = 0,
                NextPaintMem = 0,
                NextDStorey = 0,
                SaveType = 0
            };

            if (!_svc.IsLoaded) return empty;

            // Same region math as renderer (cached on the service)
            _svc.ComputeAndCacheBuildingRegion();
            if (!_svc.TryGetBuildingRegion(out int start, out int len)) return empty;

            var bytes = _svc.GetBytesCopy();
            if (start < 0 || len <= HeaderSize || start + len > bytes.Length) return empty;

            int saveType = BitConverter.ToInt32(bytes, 0);

            // Header counters (1-based where noted)
            ushort nextDBuilding = ReadU16(bytes, start + 2);  // DBuildings 1-based count
            ushort nextDFacet = ReadU16(bytes, start + 4);  // DFacets    1-based count
            ushort nextDStyle = ReadU16(bytes, start + 6);  // dstyles    full count incl. 0
            ushort nextPaintMem = (saveType >= 17) ? ReadU16(bytes, start + 8) : (ushort)0;
            ushort nextDStorey = (saveType >= 17) ? ReadU16(bytes, start + 10) : (ushort)0;

            // --- DEBUG: header / region ---
            Debug.WriteLine($"[BuildingsAccessor] saveType={saveType}  nextDBuilding={nextDBuilding} nextDFacet={nextDFacet} nextDStyle={nextDStyle} nextPaintMem={nextPaintMem} nextDStorey={nextDStorey}");
            Debug.WriteLine($"[BuildingsAccessor] region start=0x{start:X} len=0x{len:X}");

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

            // ---- DFacets (FULL 26B LAYOUT) ----
            var facets = new DFacetRec[totalFacets];
            for (int i = 0; i < totalFacets; i++)
            {
                int off = facetsOff + i * DFacetSize;

                var type = (FacetType)bytes[off + 0];
                byte h = bytes[off + 1];

                byte x0 = bytes[off + 2];
                byte x1 = bytes[off + 3];
                short y0 = BitConverter.ToInt16(bytes, off + 4);
                short y1 = BitConverter.ToInt16(bytes, off + 6);
                byte z0 = bytes[off + 8];
                byte z1 = bytes[off + 9];

                var flags = (FacetFlags)ReadU16(bytes, off + 10);

                // NOTE:
                //  - Non-cables: StyleIndex = index into dstyles[], Building = 1-based building id.
                //  - Cables:     StyleIndex = step_angle1 (SWORD),   Building = step_angle2 (SWORD).
                ushort sty = ReadU16(bytes, off + 12);
                ushort bld = ReadU16(bytes, off + 14);

                ushort st = ReadU16(bytes, off + 16); // DStorey id (1-based) or 0

                byte fh = bytes[off + 18]; // fine height; for cables this is the “mode”
                byte blockH = bytes[off + 19];
                byte open = bytes[off + 20];
                byte dfcache = bytes[off + 21];
                byte shake = bytes[off + 22];
                byte cutHole = bytes[off + 23];
                byte counter0 = bytes[off + 24];
                byte counter1 = bytes[off + 25];

                facets[i] = new DFacetRec(
                    type, x0, z0, x1, z1,
                    h, fh, sty, bld, st, flags,
                    y0, y1, blockH, open, dfcache, shake, cutHole, counter0, counter1
                );
            }
            cursor = facetsEnd;

            // --- DEBUG: sample facet ---
            if (totalFacets > 0)
            {
                var f0 = facets[0];
                Debug.WriteLine($"[BuildingsAccessor] first facet: id=1 type={f0.Type} bld={f0.Building} st={f0.Storey} styIdx={f0.StyleIndex} xy=({f0.X0},{f0.Z0})->({f0.X1},{f0.Z1})");
            }
            else
            {
                Debug.WriteLine("[BuildingsAccessor] no facets parsed.");
            }

            // ---- dstyles (S16[nextDStyle]) ----
            short[] styles = Array.Empty<short>();
            if (nextDStyle > 0)
            {
                long stylesBytes = (long)nextDStyle * 2;
                long stylesEnd = cursor + stylesBytes;
                if (stylesEnd <= blockEnd)
                {
                    styles = new short[nextDStyle];
                    for (int i = 0; i < nextDStyle; i++)
                        styles[i] = BitConverter.ToInt16(bytes, (int)(cursor + i * 2));
                    cursor = stylesEnd;
                }
                else
                {
                    Debug.WriteLine($"[BuildingsAccessor] styles OOB: cursor=0x{cursor:X} count={nextDStyle} blockEnd=0x{blockEnd:X}");
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
                    Debug.WriteLine($"[BuildingsAccessor] paint_mem OOB: cursor=0x{cursor:X} size={nextPaintMem} blockEnd=0x{blockEnd:X}");
                }
            }

            // ---- dstoreys (U16 Style; U16 PaintIndex; U16 Count) ----
            var storeys = Array.Empty<BuildingArrays.DStoreyRec>();
            if (saveType >= 17 && nextDStorey > 0)
            {
                long storeyBytes = (long)nextDStorey * DStoreyRecSize;
                long storeyEnd = cursor + storeyBytes;
                if (storeyEnd <= blockEnd)
                {
                    storeys = new BuildingArrays.DStoreyRec[nextDStorey];
                    for (int i = 0; i < nextDStorey; i++)
                    {
                        int off = (int)(cursor + i * DStoreyRecSize);
                        ushort style = ReadU16(bytes, off + 0);
                        ushort index = ReadU16(bytes, off + 2);
                        ushort count = ReadU16(bytes, off + 4);
                        storeys[i] = new BuildingArrays.DStoreyRec(style, index, count);
                    }
                    cursor = storeyEnd;
                }
                else
                {
                    Debug.WriteLine($"[BuildingsAccessor] dstoreys OOB: cursor=0x{cursor:X} count={nextDStorey} blockEnd=0x{blockEnd:X}");
                }
            }

            // --- DEBUG: parsed summary ---
            Debug.WriteLine($"[BuildingsAccessor] Parsed: buildings={totalBuildings} facets={totalFacets} styles={styles.Length} paintMem={paintMem.Length} storeys={storeys.Length}");

            return new BuildingArrays
            {
                StartOffset = start,
                Length = len,
                FacetsStart = facetsOff,           // << NEW
                Buildings = buildings,
                Facets = facets,
                Styles = styles,
                PaintMem = paintMem,
                Storeys = storeys,

                NextDBuilding = nextDBuilding,
                NextDFacet = nextDFacet,
                NextDStyle = nextDStyle,
                NextPaintMem = nextPaintMem,
                NextDStorey = nextDStorey,
                SaveType = saveType
            };
        }

        // ---------- Optional: strict scan helper ----------
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

        // ---------- Editing helpers ----------
        /// <summary>Compute the absolute byte offset of a 1-based facet id.</summary>
        public bool TryGetFacetOffset(int facetId1, out int facetOffset)
        {
            facetOffset = -1;
            if (!_svc.IsLoaded) return false;

            _svc.ComputeAndCacheBuildingRegion();
            if (!_svc.TryGetBuildingRegion(out int start, out int _)) return false;

            var bytes = _svc.GetBytesCopy();
            if (start < 0 || start + HeaderSize > bytes.Length) return false;

            ushort nextDBuilding = ReadU16(bytes, start + 2);
            ushort nextDFacet = ReadU16(bytes, start + 4);

            int totalFacets = Math.Max(0, nextDFacet - 1);
            if (facetId1 < 1 || facetId1 > totalFacets) return false;

            int buildingsOff = start + HeaderSize;
            int facetsOff = buildingsOff + (nextDBuilding - 1) * DBuildingSize + AfterBuildingsPad;

            facetOffset = facetsOff + (facetId1 - 1) * DFacetSize;
            return facetOffset + DFacetSize <= bytes.Length;
        }

        public bool TryUpdateFacetFlags(int facetId1, FacetFlags flags)
        {
            if (!TryGetFacetOffset(facetId1, out int facet0)) return false;
            int flagsOff = facet0 + 10; // U16 at +10
            return _svc.TryWriteU16_LE(flagsOff, (ushort)flags);
        }

        public bool TryUpdateFacetType(int facetId1, FacetType newType)
        {
            if (!_svc.IsLoaded) return false;
            if (!TryGetFacetOffset(facetId1, out int facet0)) return false;

            _svc.Edit(bytes =>
            {
                if (facet0 >= 0 && facet0 < bytes.Length)
                    bytes[facet0 + 0] = (byte)newType; // first byte is type
            });
            return true;
        }

        // ---------- Helpers ----------
        public static int DecodePaintPage(byte b) => b & 0x7F;         // lower 7 bits
        public static bool DecodePaintFlag(byte b) => (b & 0x80) != 0;  // high bit

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
