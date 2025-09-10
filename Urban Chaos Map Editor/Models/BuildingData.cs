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
    // Our minimal constructor remains so existing code continues to compile.
    public readonly struct DFacetRec
    {
        public readonly FacetType Type;
        public readonly byte X0, Z0, X1, Z1;
        public readonly byte Height;
        public readonly byte FHeight;
        public readonly ushort StyleIndex;
        public readonly ushort Building;  // 1-based
        public readonly ushort Storey;    // 1-based (DStorey)
        public readonly FacetFlags Flags;

        // NEW: full fields from the C layout
        public readonly short Y0;         // world Y at (x0,z0)
        public readonly short Y1;         // world Y at (x1,z1)
        public readonly byte BlockHeight;
        public readonly byte Open;        // door openness (for outside door types)
        public readonly byte Dfcache;     // index into NIGHT_dfcache[] or 0
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
    }

    // ----- Super-map aggregate we return from the parser -----
    public sealed class BuildingArrays
    {
        public int StartOffset { get; init; }
        public int Length { get; init; }
        public DBuildingRec[] Buildings { get; init; } = Array.Empty<DBuildingRec>();
        public DFacetRec[] Facets { get; init; } = Array.Empty<DFacetRec>();

        // NEW: style & paint_mem payloads from the block
        public ushort[] Styles { get; init; } = Array.Empty<ushort>();  // dstyles table
        public byte[] PaintMem { get; init; } = Array.Empty<byte>();    // paint_mem blob

        // (optional, handy for debug / UI)
        public int NextDBuilding { get; init; }
        public int NextDFacet { get; init; }
        public int NextDStyle { get; init; }
        public int NextPaintMem { get; init; }
        public int NextDStorey { get; init; }
        public int SaveType { get; init; }
    }
}
