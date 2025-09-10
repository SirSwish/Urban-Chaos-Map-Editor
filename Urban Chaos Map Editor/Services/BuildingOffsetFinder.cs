using System;
using System.Diagnostics;

namespace UrbanChaosMapEditor.Services
{
    public static class BuildingOffsetFinder
    {
        private const int HeaderSize = 48;
        private const int DBuildingSize = 24;
        private const int WallRecordSize = 26;
        private const int AfterBuildingsPad = 14;

        /// <summary>
        /// Try to locate the 48-byte "building header" by scanning backward from objectOffset.
        /// Validates counts and that wall block finishes before the object section.
        /// </summary>
        public static int FindHeader(byte[] fileBytes, int objectOffset)
        {
            if (fileBytes == null) throw new ArgumentNullException(nameof(fileBytes));
            if (objectOffset < HeaderSize || objectOffset > fileBytes.Length)
                throw new ArgumentOutOfRangeException(nameof(objectOffset));

            // Scan back up to 1MB (plenty for UC maps)
            int scanStart = Math.Max(0, objectOffset - 1024 * 1024);
            for (int pos = objectOffset - HeaderSize; pos >= scanStart; pos--)
            {
                if (IsPlausibleHeader(fileBytes, pos, objectOffset))
                {
                    Debug.WriteLine($"[Buildings] Header @ 0x{pos:X}");
                    return pos;
                }
            }

            throw new InvalidOperationException("Building header not found before object section.");
        }

        public static int FindHeader(ObjectsAccessor.Snapshot snap, byte[] fileBytes)
            => FindHeader(fileBytes, snap.ObjectOffset);

        private static bool IsPlausibleHeader(byte[] b, int off, int objectOffset)
        {
            if (off < 0 || off + HeaderSize > b.Length) return false;

            int nextBuildings = ReadU16(b, off + 2);
            int nextFacets = ReadU16(b, off + 4);
            int totalBuildings = nextBuildings - 1;
            int totalWalls = nextFacets - 1;

            if (nextBuildings == 0 || nextFacets == 0) return false;
            if (totalBuildings < 0 || totalWalls < 0) return false;
            if (totalBuildings > 20000 || totalWalls > 200000) return false; // sanity caps

            long wallsOffset = (long)off + HeaderSize + (long)totalBuildings * DBuildingSize + AfterBuildingsPad;
            long wallsEnd = wallsOffset + (long)totalWalls * WallRecordSize;

            if (wallsOffset < 0 || wallsEnd < 0) return false;
            if (wallsEnd > objectOffset) return false; // must end before objects
            if (wallsEnd > b.Length) return false;

            return true;
        }

        private static int ReadU16(byte[] b, int off) => b[off] | (b[off + 1] << 8);
    }
}
