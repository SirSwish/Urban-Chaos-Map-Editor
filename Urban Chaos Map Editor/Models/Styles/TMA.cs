using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UrbanChaosMapEditor.Models.Styles
{
    public class TMAFile
    {
        public uint SaveType { get; set; }
        public List<TextureStyle> TextureStyles { get; set; } = new();

        /// <summary>
        /// True if we removed a leading dummy style (all-zero entries) during normalization.
        /// </summary>
        public bool DroppedLeadingDummy { get; private set; }

        public static TMAFile ReadTMAFile(string filePath)
        {
            var tma = new TMAFile();
            using var reader = new BinaryReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read));

            tma.SaveType = reader.ReadUInt32();                        // little-endian

            // TEXTURES_XY
            ushort stylesCount = reader.ReadUInt16();                  // rows (styles)
            ushort entriesPerStyle = reader.ReadUInt16();              // columns (entries per style) — usually 5
            tma.TextureStyles = new List<TextureStyle>(stylesCount);
            for (int i = 0; i < stylesCount; i++)
            {
                var style = new TextureStyle { Entries = new List<TextureEntry>(entriesPerStyle) };
                for (int j = 0; j < entriesPerStyle; j++)
                {
                    style.Entries.Add(new TextureEntry
                    {
                        Page = reader.ReadByte(),
                        Tx = reader.ReadByte(),
                        Ty = reader.ReadByte(),
                        Flip = reader.ReadByte() // opaque for now
                    });
                }
                tma.TextureStyles.Add(style);
            }

            // TEXTURE_STYLE_NAMES
            ushort nameRows = reader.ReadUInt16();
            ushort nameLen = reader.ReadUInt16();
            for (int i = 0; i < nameRows; i++)
            {
                var nameBytes = reader.ReadBytes(nameLen);
                var name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
                if (i < tma.TextureStyles.Count) tma.TextureStyles[i].Name = name;
            }

            // TEXTURES_FLAGS (optional)
            if (tma.SaveType > 2)
            {
                ushort flagRows = reader.ReadUInt16();
                ushort flagCols = reader.ReadUInt16();
                for (int i = 0; i < flagRows; i++)
                {
                    var flags = new List<TextureFlag>(flagCols);
                    for (int j = 0; j < flagCols; j++)
                        flags.Add((TextureFlag)reader.ReadByte());
                    if (i < tma.TextureStyles.Count) tma.TextureStyles[i].Flags = flags;
                }
            }

            // Normalize once so raw dstyles values can be treated as 0-based everywhere.
            tma.NormalizeDropLeadingDummy();

            return tma;
        }

        /// <summary>
        /// If the first style is a "dummy" (all entries Page/Tx/Ty/Flip are zero),
        /// drop it so that raw style ids from the map (0-based) align with TextureStyles[0].
        /// Returns true if an entry was removed.
        /// </summary>
        public bool NormalizeDropLeadingDummy()
        {
            DroppedLeadingDummy = false;
            if (TextureStyles.Count == 0) return false;

            if (IsDummy(TextureStyles[0]))
            {
                TextureStyles.RemoveAt(0);
                DroppedLeadingDummy = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Convenience accessor: fetch (Page,Tx,Ty,Flip) for a 0-based raw style id and slot (0..4).
        /// </summary>
        public bool TryGetEntry(int rawStyleId, int slot, out TextureEntry entry)
        {
            entry = default;
            if (rawStyleId < 0 || rawStyleId >= TextureStyles.Count) return false;
            var s = TextureStyles[rawStyleId];
            if (s.Entries == null || slot < 0 || slot >= s.Entries.Count) return false;
            entry = s.Entries[slot];
            return true;
        }

        private static bool IsDummy(TextureStyle? s)
        {
            if (s?.Entries == null || s.Entries.Count == 0) return true;
            // Heuristic: treat as dummy if all entries are zeroed (typical buffer row).
            return s.Entries.All(e => e.Page == 0 && e.Tx == 0 && e.Ty == 0 && e.Flip == 0);
        }
    }

    public class TextureStyle
    {
        public string Name { get; set; } = "";
        public List<TextureEntry> Entries { get; set; } = new();
        public List<TextureFlag> Flags { get; set; } = new();
    }

    public struct TextureEntry
    {
        public byte Page { get; set; }
        public byte Tx { get; set; }
        public byte Ty { get; set; }
        public byte Flip { get; set; } // opaque bitfield for now
    }

    [Flags]
    public enum TextureFlag : byte
    {
        Gouraud = 0x01,
        Textured = 0x02,
        Masked = 0x04,
        Transparent = 0x08,
        Alpha = 0x10,
        Tiled = 0x20,
        TwoSided = 0x40
        // bit7 unused
    }
}
