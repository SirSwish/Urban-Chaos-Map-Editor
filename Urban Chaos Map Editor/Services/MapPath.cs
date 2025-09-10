// UrbanChaosMapEditor/Services/MapMath.cs
namespace UrbanChaosMapEditor.Services
{
    internal static class MapMath
    {
        // V1 formula: start of NumObjects (Int32) in the file
        public static int CalculateObjectOffset(int fileLength, int saveType, int objectBytes)
        {
            int sizeAdjustment = saveType >= 25 ? 2000 : 0;
            return fileLength - 12 - sizeAdjustment - objectBytes + 8;
        }
    }
}
