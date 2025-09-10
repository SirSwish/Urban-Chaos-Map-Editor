using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using UrbanChaosMapEditor.Models;

namespace UrbanChaosMapEditor.Services
{
    /// <summary>
    /// Manages the in-memory .lgt bytes and basic load/save/dirty state.
    /// Also exposes parsed models (Header, Entries, Properties, NightColour).
    /// API intentionally parallels MapDataService where relevant.
    /// </summary>
    public sealed class LightsDataService
    {
        // Singleton
        public static LightsDataService Instance { get; } = new LightsDataService();
        private LightsDataService() { }

        // ---- Constants (v1 format) ----
        private const int HeaderSize = 12;
        private const int ReservedAfterHeader = 20;
        private const int EntrySize = 20;
        private const int EntryCount = 255;
        private const int PropertiesSize = 36;
        private const int NightColourSize = 3;
        private const int TotalSize = HeaderSize + ReservedAfterHeader + (EntrySize * EntryCount) + PropertiesSize + NightColourSize; // 5171

        // ---- State ----
        private byte[] _bytes = Array.Empty<byte>();
        private bool _isLoaded;
        private bool _hasChanges;

        /// <summary>Full path of the current .lgt file (null if unsaved template/default).</summary>
        public string? CurrentPath { get; private set; }

        /// <summary>Alias for compatibility with ViewModels that use CurrentFilePath.</summary>
        public string? CurrentFilePath => CurrentPath;

        /// <summary>True once a lights buffer is present in memory (from load or template).</summary>
        public bool IsLoaded => _isLoaded;

        /// <summary>True if there are unsaved changes.</summary>
        public bool HasChanges => _hasChanges;

        /// <summary>Returns a copy of the current lights bytes (never null).</summary>
        public byte[] GetBytesCopy() => (byte[])_bytes.Clone();

        // ---- Parsed Model (kept in sync with _bytes) ----
        public LightHeader Header { get; private set; } = new();
        public List<LightEntry> Entries { get; private set; } = new(EntryCount);
        public LightProperties Properties { get; private set; }
        public LightNightColour NightColour { get; private set; }

        // ---- Events ----
        public event EventHandler<PathEventArgs>? LightsLoaded;
        public event EventHandler<PathEventArgs>? LightsSaved;
        public event EventHandler? LightsCleared;
        public event EventHandler? LightsBytesReset;
        public event EventHandler? DirtyStateChanged;

        // ---------------------------------------------------------------------
        // Load / Save (raw)
        // ---------------------------------------------------------------------

        /// <summary>Load lights from disk into memory (resets dirty) and parse models.</summary>
        public async Task LoadAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path is null/empty.", nameof(path));

            _bytes = await System.IO.File.ReadAllBytesAsync(path).ConfigureAwait(false);
            if (_bytes.Length < TotalSize)
                throw new InvalidDataException($".lgt file too small (got {_bytes.Length}, expected ≥ {TotalSize}).");

            ParseFromBytes(_bytes);

            _isLoaded = true;
            _hasChanges = false;
            CurrentPath = path;

            LightsBytesReset?.Invoke(this, EventArgs.Empty);
            LightsLoaded?.Invoke(this, new PathEventArgs(path));
            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Seeds the lights buffer from provided template bytes (not dirty) and parse models.
        /// Leaves CurrentPath = null (unsaved).
        /// </summary>
        public void NewFromTemplate(byte[] bytes)
        {
            if (bytes is null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length < TotalSize)
                throw new InvalidDataException($"Template .lgt is too small (got {bytes.Length}, expected ≥ {TotalSize}).");

            _bytes = (byte[])bytes.Clone();
            ParseFromBytes(_bytes);

            _isLoaded = true;
            _hasChanges = false;
            CurrentPath = null;

            LightsBytesReset?.Invoke(this, EventArgs.Empty);
            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Saves to CurrentPath. Throws if there is no current path.</summary>
        public async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentPath))
                throw new InvalidOperationException("No current lights file path. Use SaveAsAsync first.");

            // Rebuild bytes from parsed models to ensure consistency.
            _bytes = BuildBytesFromModel();
            await System.IO.File.WriteAllBytesAsync(CurrentPath, _bytes).ConfigureAwait(false);
            _hasChanges = false;

            LightsSaved?.Invoke(this, new PathEventArgs(CurrentPath));
            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Saves to a new path and updates CurrentPath.</summary>
        public async Task SaveAsAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path is null/empty.", nameof(path));

            _bytes = BuildBytesFromModel();
            await System.IO.File.WriteAllBytesAsync(path, _bytes).ConfigureAwait(false);
            CurrentPath = path;
            _hasChanges = false;

            LightsSaved?.Invoke(this, new PathEventArgs(path));
            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Clears the lights buffer and resets state.</summary>
        public void Clear()
        {
            _bytes = Array.Empty<byte>();
            _isLoaded = false;
            _hasChanges = false;
            CurrentPath = null;

            // Reset parsed model to defaults
            Header = new LightHeader();
            Entries = new List<LightEntry>(EntryCount);
            Properties = default;
            NightColour = default;

            LightsCleared?.Invoke(this, EventArgs.Empty);
            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Replace the entire lights buffer. Marks dirty by default. Parses models.</summary>
        public void ReplaceAllBytes(byte[] bytes, bool markDirty = true)
        {
            if (bytes is null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length < TotalSize)
                throw new InvalidDataException($".lgt buffer too small (got {bytes.Length}, expected ≥ {TotalSize}).");

            _bytes = (byte[])bytes.Clone();
            _isLoaded = true;

            ParseFromBytes(_bytes);

            if (markDirty)
            {
                _hasChanges = true;
                DirtyStateChanged?.Invoke(this, EventArgs.Empty);
            }

            LightsBytesReset?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Marks the lights as dirty (call after any mutation via the parsed model setters).</summary>
        public void MarkDirty()
        {
            if (_hasChanges) return;
            _hasChanges = true;
            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        }

        // ---------------------------------------------------------------------
        // Parsed model helpers (read/write)
        // ---------------------------------------------------------------------

        private void ParseFromBytes(byte[] bytes)
        {
            int offset = 0;

            // Header
            Header = new LightHeader
            {
                SizeOfEdLight = BitConverter.ToInt32(bytes, offset + 0),
                EdMaxLights = BitConverter.ToInt32(bytes, offset + 4),
                SizeOfNightColour = BitConverter.ToInt32(bytes, offset + 8)
            };
            offset += HeaderSize;

            // Reserved 20 bytes (skip)
            offset += ReservedAfterHeader;

            // Entries
            var list = new List<LightEntry>(EntryCount);
            for (int i = 0; i < EntryCount; i++)
            {
                var e = new LightEntry
                {
                    Range = bytes[offset + 0],
                    Red = unchecked((sbyte)bytes[offset + 1]),
                    Green = unchecked((sbyte)bytes[offset + 2]),
                    Blue = unchecked((sbyte)bytes[offset + 3]),
                    Next = bytes[offset + 4],
                    Used = bytes[offset + 5],
                    Flags = bytes[offset + 6],
                    Padding = bytes[offset + 7],
                    X = BitConverter.ToInt32(bytes, offset + 8),
                    Y = BitConverter.ToInt32(bytes, offset + 12),
                    Z = BitConverter.ToInt32(bytes, offset + 16)
                };
                list.Add(e);
                offset += EntrySize;
            }
            Entries = list;

            // Properties (36 bytes)
            Properties = new LightProperties
            {
                EdLightFree = BitConverter.ToInt32(bytes, offset + 0),
                NightFlag = BitConverter.ToUInt32(bytes, offset + 4),
                NightAmbD3DColour = BitConverter.ToUInt32(bytes, offset + 8),
                NightAmbD3DSpecular = BitConverter.ToUInt32(bytes, offset + 12),
                NightAmbRed = BitConverter.ToInt32(bytes, offset + 16),
                NightAmbGreen = BitConverter.ToInt32(bytes, offset + 20),
                NightAmbBlue = BitConverter.ToInt32(bytes, offset + 24),
                NightLampostRed = unchecked((sbyte)bytes[offset + 28]),
                NightLampostGreen = unchecked((sbyte)bytes[offset + 29]),
                NightLampostBlue = unchecked((sbyte)bytes[offset + 30]),
                Padding = bytes[offset + 31],
                NightLampostRadius = BitConverter.ToInt32(bytes, offset + 32),
            };
            offset += PropertiesSize;

            // Night colour (3 bytes)
            NightColour = new LightNightColour
            {
                Red = bytes[offset + 0],
                Green = bytes[offset + 1],
                Blue = bytes[offset + 2]
            };
            offset += NightColourSize;

            // Done
        }

        private byte[] BuildBytesFromModel()
        {
            if (_bytes is null || _bytes.Length < HeaderSize)
                throw new InvalidOperationException("No existing header available to seed .lgt output.");

            var outBytes = new byte[TotalSize];
            int offset = 0;

            // Preserve the original 12-byte header
            Array.Copy(_bytes, 0, outBytes, 0, HeaderSize);
            offset += HeaderSize;

            // Reserved 20 bytes (zeros)
            offset += ReservedAfterHeader;

            // Entries (255 * 20)
            for (int i = 0; i < EntryCount; i++)
            {
                LightEntry e = i < Entries.Count ? Entries[i] : default;
                outBytes[offset + 0] = e.Range;
                outBytes[offset + 1] = unchecked((byte)e.Red);
                outBytes[offset + 2] = unchecked((byte)e.Green);
                outBytes[offset + 3] = unchecked((byte)e.Blue);
                outBytes[offset + 4] = e.Next;
                outBytes[offset + 5] = e.Used;
                outBytes[offset + 6] = e.Flags;
                outBytes[offset + 7] = e.Padding;
                Array.Copy(BitConverter.GetBytes(e.X), 0, outBytes, offset + 8, 4);
                Array.Copy(BitConverter.GetBytes(e.Y), 0, outBytes, offset + 12, 4);
                Array.Copy(BitConverter.GetBytes(e.Z), 0, outBytes, offset + 16, 4);
                offset += EntrySize;
            }

            // Properties (36)
            Array.Copy(BitConverter.GetBytes(Properties.EdLightFree), 0, outBytes, offset + 0, 4);
            Array.Copy(BitConverter.GetBytes(Properties.NightFlag), 0, outBytes, offset + 4, 4);
            Array.Copy(BitConverter.GetBytes(Properties.NightAmbD3DColour), 0, outBytes, offset + 8, 4);
            Array.Copy(BitConverter.GetBytes(Properties.NightAmbD3DSpecular), 0, outBytes, offset + 12, 4);
            Array.Copy(BitConverter.GetBytes(Properties.NightAmbRed), 0, outBytes, offset + 16, 4);
            Array.Copy(BitConverter.GetBytes(Properties.NightAmbGreen), 0, outBytes, offset + 20, 4);
            Array.Copy(BitConverter.GetBytes(Properties.NightAmbBlue), 0, outBytes, offset + 24, 4);
            outBytes[offset + 28] = unchecked((byte)Properties.NightLampostRed);
            outBytes[offset + 29] = unchecked((byte)Properties.NightLampostGreen);
            outBytes[offset + 30] = unchecked((byte)Properties.NightLampostBlue);
            outBytes[offset + 31] = Properties.Padding;
            Array.Copy(BitConverter.GetBytes(Properties.NightLampostRadius), 0, outBytes, offset + 32, 4);
            offset += PropertiesSize;

            // Night colour (3)
            outBytes[offset + 0] = NightColour.Red;
            outBytes[offset + 1] = NightColour.Green;
            outBytes[offset + 2] = NightColour.Blue;
            offset += NightColourSize;

            return outBytes;
        }

        // ---------------------------------------------------------------------
        // Parsed setters (mark dirty + notify)
        // ---------------------------------------------------------------------

        public void SetHeader(LightHeader header)
        {
            Header = header;
            MarkDirty();
            LightsBytesReset?.Invoke(this, EventArgs.Empty);
        }

        public void SetEntries(IEnumerable<LightEntry> entries)
        {
            Entries = new List<LightEntry>(entries);
            // Ensure capacity/index safety for 255
            while (Entries.Count < EntryCount) Entries.Add(default);
            MarkDirty();
            LightsBytesReset?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateEntry(int index, LightEntry entry)
        {
            if (index < 0 || index >= EntryCount) throw new ArgumentOutOfRangeException(nameof(index));
            while (Entries.Count < EntryCount) Entries.Add(default);
            Entries[index] = entry;
            MarkDirty();
            LightsBytesReset?.Invoke(this, EventArgs.Empty);
        }

        public void SetProperties(LightProperties props)
        {
            Properties = props;
            MarkDirty();
            LightsBytesReset?.Invoke(this, EventArgs.Empty);
        }

        public void SetNightColour(LightNightColour nc)
        {
            NightColour = nc;
            MarkDirty();
            LightsBytesReset?.Invoke(this, EventArgs.Empty);
        }

        // ---------------------------------------------------------------------
        // Default .lgt loader (pack resource)
        // ---------------------------------------------------------------------
        public static byte[] LoadDefaultResourceBytes()
        {
            // default.lgt embedded as Resource at Assets/Defaults/default.lgt
            var uri = new Uri("pack://application:,,,/Assets/Defaults/default.lgt", UriKind.Absolute);
            var sri = Application.GetResourceStream(uri)
                      ?? throw new FileNotFoundException("default.lgt not found as Resource at Assets/Defaults/default.lgt");
            using var s = sri.Stream;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }

    /// <summary>Simple event args that carry a file path.</summary>
    public sealed class PathEventArgs : EventArgs
    {
        public string Path { get; }
        public PathEventArgs(string path) => Path = path;
    }
}
