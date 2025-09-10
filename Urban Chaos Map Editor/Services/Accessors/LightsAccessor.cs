using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UrbanChaosMapEditor.Models;
using System.Diagnostics;

namespace UrbanChaosMapEditor.Services
{
    /// <summary>
    /// Byte-accurate accessor for .lgt buffer in LightsDataService.
    /// 
    /// Layout (V1):
    ///   0x0000: LightHeader                 (12 bytes)
    ///   0x000C: Reserved padding            (20 bytes)
    ///   0x0020: 255 * LightEntry (20 bytes) (5100 bytes)
    ///   0x140C: LightProperties             (36 bytes)
    ///   0x1430: LightNightColour            ( 3 bytes)
    ///   Total = 5171 bytes
    /// </summary>
    public sealed class LightsAccessor
    {
        private readonly LightsDataService _svc;
        public LightsAccessor(LightsDataService svc) => _svc = svc;

        // ---- Layout constants ----
        public const int HeaderSize = 12;
        public const int ReservedPad = 20;
        public const int EntrySize = 20;
        public const int EntryCount = 255;

        public const int EntriesOffset = HeaderSize + ReservedPad;                 // 0x0020 = 32
        public const int PropertiesOffset = EntriesOffset + EntrySize * EntryCount;// 0x140C = 5132
        public const int PropertiesSize = 36;
        public const int NightColourOffset = PropertiesOffset + PropertiesSize;    // 0x1430 = 5168
        public const int NightColourSize = 3;
        public const int TotalSize = NightColourOffset + NightColourSize;          // 5171

        private Span<byte> SpanAll() => _svc.GetBytesCopy().AsSpan();

        private byte[] GetBufferForWrite()
        {
            var buf = _svc.GetBytesCopy();
            if (buf.Length < TotalSize)
                throw new InvalidOperationException($".lgt buffer too small ({buf.Length} < {TotalSize}).");
            return buf;
        }

        // ---------- Header ----------
        public LightHeader ReadHeader()
        {
            var s = SpanAll();
            if (s.Length < HeaderSize) throw new InvalidOperationException(".lgt: missing header.");
            return new LightHeader
            {
                SizeOfEdLight = Unsafe.ReadUnaligned<int>(ref s[0]),
                EdMaxLights = Unsafe.ReadUnaligned<int>(ref s[4]),
                SizeOfNightColour = Unsafe.ReadUnaligned<int>(ref s[8])
            };
        }

        // ---------- Entries ----------
        public LightEntry ReadEntry(int index)
        {
            if ((uint)index >= EntryCount) throw new ArgumentOutOfRangeException(nameof(index));
            var s = SpanAll();
            int off = EntriesOffset + index * EntrySize;

            return new LightEntry
            {
                Range = s[off + 0],
                Red = unchecked((sbyte)s[off + 1]),
                Green = unchecked((sbyte)s[off + 2]),
                Blue = unchecked((sbyte)s[off + 3]),
                Next = s[off + 4],
                Used = s[off + 5],
                Flags = s[off + 6],
                Padding = s[off + 7],
                X = Unsafe.ReadUnaligned<int>(ref s[off + 8]),
                Y = Unsafe.ReadUnaligned<int>(ref s[off + 12]),
                Z = Unsafe.ReadUnaligned<int>(ref s[off + 16]),
            };
        }

        public List<LightEntry> ReadAllEntries()
        {
            var list = new List<LightEntry>(EntryCount);
            for (int i = 0; i < EntryCount; i++) list.Add(ReadEntry(i));
            return list;
        }

        public void WriteEntry(int index, LightEntry e)
        {
            if ((uint)index >= EntryCount) throw new ArgumentOutOfRangeException(nameof(index));
            var buf = GetBufferForWrite();
            int off = EntriesOffset + index * EntrySize;

            buf[off + 0] = e.Range;
            buf[off + 1] = unchecked((byte)e.Red);
            buf[off + 2] = unchecked((byte)e.Green);
            buf[off + 3] = unchecked((byte)e.Blue);
            buf[off + 4] = e.Next;
            buf[off + 5] = e.Used;
            buf[off + 6] = e.Flags;
            buf[off + 7] = e.Padding;

            WriteIntLE(buf, off + 8, e.X);
            WriteIntLE(buf, off + 12, e.Y);
            WriteIntLE(buf, off + 16, e.Z);

            _svc.ReplaceAllBytes(buf); // fires LightsBytesReset + Dirty
        }

        // ---------- Properties ----------
        public LightProperties ReadProperties()
        {
            var s = SpanAll();
            if (s.Length < PropertiesOffset + PropertiesSize)
                throw new InvalidOperationException(".lgt: missing properties block.");

            int off = PropertiesOffset;

            var p = new LightProperties
            {
                EdLightFree = Unsafe.ReadUnaligned<int>(ref s[off + 0]),
                NightFlag = Unsafe.ReadUnaligned<uint>(ref s[off + 4]),
                NightAmbD3DColour = Unsafe.ReadUnaligned<uint>(ref s[off + 8]),
                NightAmbD3DSpecular = Unsafe.ReadUnaligned<uint>(ref s[off + 12]),
                NightAmbRed = Unsafe.ReadUnaligned<int>(ref s[off + 16]),
                NightAmbGreen = Unsafe.ReadUnaligned<int>(ref s[off + 20]),
                NightAmbBlue = Unsafe.ReadUnaligned<int>(ref s[off + 24]),
                NightLampostRed = unchecked((sbyte)s[off + 28]),
                NightLampostGreen = unchecked((sbyte)s[off + 29]),
                NightLampostBlue = unchecked((sbyte)s[off + 30]),
                Padding = s[off + 31],
                NightLampostRadius = Unsafe.ReadUnaligned<int>(ref s[off + 32]),
            };

            try
            {
                string bits = Convert.ToString(unchecked((int)p.NightFlag), 2).PadLeft(32, '0');
                bool bit0 = (p.NightFlag & 0x00000001) != 0;
                bool bit1 = (p.NightFlag & 0x00000002) != 0;
                bool bit2 = (p.NightFlag & 0x00000004) != 0;

                Debug.WriteLine(
                    $"[LGT] ReadProperties off=0x{off:X}  EdFree={p.EdLightFree}  " +
                    $"NightFlag=0x{p.NightFlag:X8} b{bits}  " +
                    $"b0={bit0} b1={bit1} b2={bit2}  " +
                    $"AmbD3D=0x{p.NightAmbD3DColour:X8} Spec=0x{p.NightAmbD3DSpecular:X8}  " +
                    $"AmbRGB=({p.NightAmbRed},{p.NightAmbGreen},{p.NightAmbBlue})  " +
                    $"LampRGB=({p.NightLampostRed},{p.NightLampostGreen},{p.NightLampostBlue})  " +
                    $"Radius={p.NightLampostRadius}");
            }
            catch { /* debug only */ }

            return p;
        }

        public void WriteProperties(LightProperties p)
        {
            var buf = GetBufferForWrite();
            int off = PropertiesOffset;

            WriteIntLE(buf, off + 0, p.EdLightFree);
            WriteUIntLE(buf, off + 4, p.NightFlag);
            WriteUIntLE(buf, off + 8, p.NightAmbD3DColour);
            WriteUIntLE(buf, off + 12, p.NightAmbD3DSpecular);
            WriteIntLE(buf, off + 16, p.NightAmbRed);
            WriteIntLE(buf, off + 20, p.NightAmbGreen);
            WriteIntLE(buf, off + 24, p.NightAmbBlue);
            buf[off + 28] = unchecked((byte)p.NightLampostRed);
            buf[off + 29] = unchecked((byte)p.NightLampostGreen);
            buf[off + 30] = unchecked((byte)p.NightLampostBlue);
            buf[off + 31] = p.Padding;
            WriteIntLE(buf, off + 32, p.NightLampostRadius);

            _svc.ReplaceAllBytes(buf);
        }

        // ---------- Night Colour ----------
        public LightNightColour ReadNightColour()
        {
            var s = SpanAll();
            if (s.Length < NightColourOffset + NightColourSize)
                throw new InvalidOperationException(".lgt: missing night colour block.");

            int off = NightColourOffset;
            return new LightNightColour
            {
                Red = s[off + 0],
                Green = s[off + 1],
                Blue = s[off + 2]
            };
        }

        public void WriteNightColour(LightNightColour c)
        {
            var buf = GetBufferForWrite();
            int off = NightColourOffset;
            buf[off + 0] = c.Red;
            buf[off + 1] = c.Green;
            buf[off + 2] = c.Blue;
            _svc.ReplaceAllBytes(buf);
        }

        // ---------- Convenience ----------
        public int FindFirstFreeIndex()
        {
            var props = ReadProperties();
            if (props.EdLightFree > 0 && props.EdLightFree <= EntryCount)
            {
                int i = props.EdLightFree - 1; // 1-based in file
                if (ReadEntry(i).Used == 0) return i;
            }
            for (int i = 0; i < EntryCount; i++)
                if (ReadEntry(i).Used == 0) return i;
            return -1;
        }

        public int AddLight(LightEntry e)
        {
            int idx = FindFirstFreeIndex();
            if (idx < 0) return -1;

            e.Used = 1;
            e.Next = 0;
            WriteEntry(idx, e);

            var props = ReadProperties();
            int next = -1;
            for (int i = idx + 1; i < EntryCount; i++) if (ReadEntry(i).Used == 0) { next = i; break; }
            if (next < 0) for (int i = 0; i < idx; i++) if (ReadEntry(i).Used == 0) { next = i; break; }
            props.EdLightFree = next >= 0 ? next + 1 : 0; // 1-based or 0
            WriteProperties(props);

            return idx;
        }

        public void DeleteLight(int index)
        {
            var e = ReadEntry(index);
            e.Used = 0;
            WriteEntry(index, e);

            var props = ReadProperties();
            if (props.EdLightFree == 0 || props.EdLightFree - 1 > index)
            {
                props.EdLightFree = index + 1; // 1-based
                WriteProperties(props);
            }
        }

        // ---------- Helpers ----------
        private static void WriteIntLE(byte[] b, int off, int v)
        {
            b[off + 0] = (byte)(v);
            b[off + 1] = (byte)(v >> 8);
            b[off + 2] = (byte)(v >> 16);
            b[off + 3] = (byte)(v >> 24);
        }

        private static void WriteUIntLE(byte[] b, int off, uint v)
        {
            b[off + 0] = (byte)(v);
            b[off + 1] = (byte)(v >> 8);
            b[off + 2] = (byte)(v >> 16);
            b[off + 3] = (byte)(v >> 24);
        }

        // Mapping helpers (8192 UI px ↔ 32768 world units)
        public static int UiXToWorldX(int uiX) => 32768 - uiX * 4;
        public static int UiZToWorldZ(int uiZ) => 32768 - uiZ * 4;
        public static int WorldXToUiX(int worldX) => (32768 - worldX) / 4;
        public static int WorldZToUiZ(int worldZ) => (32768 - worldZ) / 4;
    }
}
