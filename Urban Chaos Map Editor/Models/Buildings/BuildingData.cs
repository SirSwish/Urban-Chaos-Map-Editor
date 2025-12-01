// Models/BuildingData.cs
using System;

namespace UrbanChaosMapEditor.Models
{
    // ----- DBuilding (what we actually use right now) -----
    // Stored 1-based facet range: [StartFacet .. EndFacet) for the building.
    public readonly struct DBuildingRec
    {
        public readonly ushort StartFacet; // 1-based
        public readonly ushort EndFacet;   // 1-based end (exclusive)
        public DBuildingRec(ushort startFacet, ushort endFacet)
        {
            StartFacet = startFacet;
            EndFacet = endFacet;
        }
    }

    // ----- Facet enums (unchanged) -----
    public enum FacetType : byte
    {
        Normal = 1,
        Roof = 2,
        Wall = 3,
        RoofQuad = 4,
        FloorPoints = 5,
        FireEscape = 6,
        Staircase = 7,
        Skylight = 8,
        Cable = 9,
        Fence = 10,
        FenceBrick = 11,
        Ladder = 12,
        FenceFlat = 13,
        Trench = 14,
        JustCollision = 15,
        Partition = 16,
        Inside = 17,
        Door = 18,
        InsideDoor = 19,
        OInside = 20,
        OutsideDoor = 21,
        NormalFoundation = 100
    }

    [Flags]
    public enum FacetFlags : ushort
    {
        Invisible = 1 << 0,
        Inside = 1 << 3,
        Dlit = 1 << 4,
        HugFloor = 1 << 5,
        Electrified = 1 << 6,
        TwoSided = 1 << 7,
        Unclimbable = 1 << 8,
        OnBuilding = 1 << 9,
        BarbTop = 1 << 10,
        SeeThrough = 1 << 11,
        Open = 1 << 12,
        Deg90 = 1 << 13,
        TwoTextured = 1 << 14,
        FenceCut = 1 << 15
    }

    // ----- DFacet (expanded to full C struct layout; backward compatible) -----
    //
    // struct DFacet {
    //   UBYTE FacetType; UBYTE Height; UBYTE x[2]; SWORD Y[2]; UBYTE z[2];
    //   UWORD FacetFlags; UWORD StyleIndex; UWORD Building; UWORD DStorey;
    //   UBYTE FHeight; UBYTE BlockHeight; UBYTE Open; UBYTE Dfcache;
    //   UBYTE Shake; UBYTE CutHole; UBYTE Counter[2];
    // }
    //
    // IMPORTANT (CABLE SPECIAL-CASE — from create_cable_dfacet):
    //   Height     = segment count along the cable
    //   StyleIndex = step_angle1  (SWORD)  -> treat as *signed* when Type==Cable
    //   Building   = step_angle2  (SWORD)  -> treat as *signed* when Type==Cable
    //   FHeight    = second “mode” value (TextureStyle2 for the source wall)
    // For non-cable facets, StyleIndex is the index into dstyles[], and Building is 1-based building id.
    public readonly struct DFacetRec
    {
        public readonly FacetType Type;
        public readonly byte X0, Z0, X1, Z1;

        // For most facets: coarse height/segments (often connect_count*4).
        // For cables: number of segments along the cable.
        public readonly byte Height;

        // For most facets: fine height/foundation count.
        // For cables: mode (wall_list[wall].TextureStyle2).
        public readonly byte FHeight;

        // For most facets: index into dstyles[] (signed table).
        // For cables: holds step_angle1 (written as SWORD in the game).
        public readonly ushort StyleIndex;

        // For most facets: 1-based building id.
        // For cables: holds step_angle2 (written as SWORD in the game) — NOT a building id.
        public readonly ushort Building;  // see cable helpers below

        // 1-based storey id (DStorey) when applicable; not meaningful for cables.
        public readonly ushort Storey;

        public readonly FacetFlags Flags;

        // World-space Y at the endpoints (already resolved by the engine for cables; floors/others depend on flags).
        public readonly short Y0;         // world Y at (x0,z0)
        public readonly short Y1;         // world Y at (x1,z1)

        public readonly byte BlockHeight;
        public readonly byte Open;        // door openness (for outside/inside door types)
        public readonly byte Dfcache;     // index into a night/light cache (engine-side)
        public readonly byte Shake;
        public readonly byte CutHole;
        public readonly byte Counter0;
        public readonly byte Counter1;

        // Old ctor (kept for compatibility)
        public DFacetRec(
            FacetType type, byte x0, byte z0, byte x1, byte z1,
            byte height, byte fheight, ushort style, ushort building, ushort storey, FacetFlags flags)
        {
            Type = type;
            X0 = x0; Z0 = z0; X1 = x1; Z1 = z1;
            Height = height; FHeight = fheight;
            StyleIndex = style;
            Building = building; Storey = storey;
            Flags = flags;

            // default the extended fields
            Y0 = 0; Y1 = 0;
            BlockHeight = 0;
            Open = 0;
            Dfcache = 0;
            Shake = 0;
            CutHole = 0;
            Counter0 = 0;
            Counter1 = 0;
        }

        // Full ctor (used by the enhanced parser)
        public DFacetRec(
            FacetType type, byte x0, byte z0, byte x1, byte z1,
            byte height, byte fheight, ushort style, ushort building, ushort storey, FacetFlags flags,
            short y0, short y1, byte blockHeight, byte open, byte dfcache, byte shake, byte cutHole, byte counter0, byte counter1)
        {
            Type = type;
            X0 = x0; Z0 = z0; X1 = x1; Z1 = z1;
            Height = height; FHeight = fheight;
            StyleIndex = style;
            Building = building; Storey = storey;
            Flags = flags;

            Y0 = y0; Y1 = y1;
            BlockHeight = blockHeight;
            Open = open;
            Dfcache = dfcache;
            Shake = shake;
            CutHole = cutHole;
            Counter0 = counter0;
            Counter1 = counter1;
        }

        // -------- Cable helpers (safe, read-only) --------
        public bool IsCable => Type == FacetType.Cable;

        /// <summary> Cable: number of rendered segments along the span. </summary>
        public int CableSegments => IsCable ? Height : 0;

        /// <summary> Cable: step_angle1 (signed). For non-cables, 0. </summary>
        public short CableStep1Signed => IsCable ? unchecked((short)StyleIndex) : (short)0;

        /// <summary> Cable: step_angle2 (signed). For non-cables, 0. </summary>
        public short CableStep2Signed => IsCable ? unchecked((short)Building) : (short)0;

        /// <summary> Cable: raw unsigned views of the step fields, if you prefer. </summary>
        public ushort CableStep1 => IsCable ? StyleIndex : (ushort)0;
        public ushort CableStep2 => IsCable ? Building : (ushort)0;

        /// <summary> Cable: mode value (TextureStyle2 in source), carried in FHeight. </summary>
        public int CableMode => IsCable ? FHeight : 0;
    }

    // ----- Super-map aggregate we return from the parser -----
    public sealed class BuildingArrays
    {
        public int StartOffset { get; init; }
        public int Length { get; init; }

        // NEW: absolute start of the facets table
        public int FacetsStart { get; init; }

        public DBuildingRec[] Buildings { get; init; } = Array.Empty<DBuildingRec>();
        public DFacetRec[] Facets { get; init; } = Array.Empty<DFacetRec>();

        // Style table and “paint_mem” blob (as stored in file).
        // NOTE: dstyles is a SIGNED table in the game; keep as short[] here.
        public short[] Styles { get; init; } = Array.Empty<short>();
        public byte[] PaintMem { get; init; } = Array.Empty<byte>();

        // NEW: dstorey table (each storey points at a slice inside PaintMem).
        // Matches the C layout implied by add_painted_textures():
        //   U16 StyleIndex; U16 PaintIndex; U16 Count
        public readonly record struct DStoreyRec(ushort StyleIndex, ushort PaintIndex, sbyte Count, byte Padding);
        public DStoreyRec[] Storeys { get; init; } = Array.Empty<DStoreyRec>();

        // Header counters as read from the block.
        public ushort NextDBuilding { get; init; }
        public ushort NextDFacet { get; init; }
        public ushort NextDStyle { get; init; }
        public ushort NextPaintMem { get; init; }
        public ushort NextDStorey { get; init; }
        public int SaveType { get; init; }

        /// <summary>
        /// Returns the paint_mem slice for a given storey, or empty if OOB/corrupt.
        /// Each byte: lower 7 bits = page (0..127), high bit often used as a flag.
        /// </summary>
        public ReadOnlySpan<byte> GetPaintForStorey(int storeyIndex)
        {
            if ((uint)storeyIndex >= (uint)Storeys.Length) return ReadOnlySpan<byte>.Empty;
            var s = Storeys[storeyIndex];
            int start = s.PaintIndex;
            int count = s.Count;
            if (count <= 0) return ReadOnlySpan<byte>.Empty;
            int end = start + count;
            if (start < 0 || end > PaintMem.Length) return ReadOnlySpan<byte>.Empty;
            return new ReadOnlySpan<byte>(PaintMem, start, count);
        }
    }
}
