// UrbanChaosMapEditor/Services/BuildingHeaderStrict.cs
using System;
using System.Diagnostics;

namespace UrbanChaosMapEditor.Services
{
    internal static class BuildingHeaderStrict
    {
        private const int HeaderSize = 48;
        private const int DBuildingSize = 24;
        private const int AfterBuildings14 = 14;
        private const int DFacetSize = 26;

        public static int Find(byte[] bytes, int objectOffset)
        {
            int window = Math.Min(objectOffset, 1 * 1024 * 1024);
            int start = objectOffset - window;

            for (int off = start; off <= objectOffset - HeaderSize; off += 2)
            {
                if (!PlausibleHeader(bytes, off, objectOffset, out long wallsOff, out long wallsEnd))
                    continue;

                int pad = (int)(objectOffset - wallsEnd);
                if (pad < 0 || pad > 16) continue;
                if (pad > 0 && !IsAllZero(bytes, (int)wallsEnd, pad)) continue;

                return off;
            }

            return -1;
        }

        private static bool PlausibleHeader(byte[] b, int off, int objOff, out long wallsOff, out long wallsEnd)
        {
            wallsOff = wallsEnd = -1;
            if (off < 0 || off + HeaderSize > b.Length) return false;

            int nextB = ReadU16(b, off + 2);
            int nextF = ReadU16(b, off + 4);
            int buildings = nextB - 1;
            int facets = nextF - 1;

            if (buildings < 1 || facets < 8) return false;
            if (buildings > 5000 || facets > 200000) return false;

            wallsOff = off + HeaderSize + (long)buildings * DBuildingSize + AfterBuildings14;
            wallsEnd = wallsOff + (long)facets * DFacetSize;

            if (wallsOff < off + HeaderSize) return false;
            if (wallsEnd > objOff) return false;

            int toCheck = Math.Min(facets, 8);
            for (int i = 0; i < toCheck; i++)
            {
                int fOff = (int)(wallsOff + i * DFacetSize);
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

        private static bool IsAllZero(byte[] b, int off, int len)
        {
            if (off < 0 || off + len > b.Length) return false;
            for (int i = 0; i < len; i++) if (b[off + i] != 0) return false;
            return true;
        }

        private static int ReadU16(byte[] b, int off) => b[off] | (b[off + 1] << 8);
    }
}
