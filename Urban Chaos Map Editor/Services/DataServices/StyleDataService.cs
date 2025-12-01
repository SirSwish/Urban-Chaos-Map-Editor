// /Services/DataServices/StyleDataService.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UrbanChaosMapEditor.Models.Styles;

namespace UrbanChaosMapEditor.Services.DataServices
{
    public sealed class StyleDataService
    {
        private static readonly Lazy<StyleDataService> _lazy = new(() => new StyleDataService());
        public static StyleDataService Instance => _lazy.Value;

        private readonly object _sync = new();
        private TMAFile? _tma;

        private StyleDataService() { }

        public bool IsLoaded => _tma is not null;
        public string? CurrentPath { get; private set; }   // may be pack-URI label for streams
        public TMAFile? TmaSnapshot => _tma;

        public event EventHandler? StylesLoaded;
        public event EventHandler? StylesCleared;
        public event EventHandler? StylesBytesReset;

        /// <summary>
        /// Map RAW style id -> zero-based index in TMA table.
        /// Row 0 is dummy. 0x000 and 0x001 both map to row 1. Otherwise idx = raw.
        /// </summary>
        public static int MapRawStyleIdToTmaIndex(int raw) => (raw <= 1) ? 1 : raw;

        public async Task LoadAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path required.", nameof(path));
            var full = Path.GetFullPath(path);

            TMAFile parsed = await Task.Run(() => TMAFile.ReadTMAFile(full));
            lock (_sync) { _tma = parsed; CurrentPath = full; }

            StylesLoaded?.Invoke(this, EventArgs.Empty);
            StylesBytesReset?.Invoke(this, EventArgs.Empty);
            Debug.WriteLine($"[StyleDataService] Loaded from file '{full}', styles={_tma.TextureStyles.Count}");
        }

        /// <summary>
        /// Fetch a TMA entry (Page/Tx/Ty/Flip) for a RAW style id + slot (0..4).
        /// Applies the alias rule above.
        /// </summary>
        public bool TryGetTmaEntry(int rawStyleId, int slot, out Models.Styles.TextureEntry entry)
        {
            entry = default;
            var tma = _tma;
            if (tma == null || slot < 0) return false;

            int idx = MapRawStyleIdToTmaIndex(rawStyleId);
            if (idx < 0 || idx >= tma.TextureStyles.Count) return false;

            var style = tma.TextureStyles[idx];
            if (style.Entries == null || slot >= style.Entries.Count) return false;

            entry = style.Entries[slot];
            return true;
        }

        /// <summary>
        /// Nice label for UI using the same alias rule: "Style #N: Name" where N is 1-based human index.
        /// </summary>
        public string GetStyleDisplayLabel(int rawStyleId)
        {
            var tma = _tma;
            if (tma == null) return $"Style raw={rawStyleId}";
            int idx = MapRawStyleIdToTmaIndex(rawStyleId);
            if (idx < 0 || idx >= tma.TextureStyles.Count) return $"Style raw={rawStyleId}";
            var s = tma.TextureStyles[idx];
            // “human” is still 1-based, but row 0 is dummy—so this shows real style numbering.
            int human = idx; // if you want first real to show “Style 1”, set human = idx; (since idx=1 for first real)
            return string.IsNullOrWhiteSpace(s.Name) ? $"Style {human}" : $"Style {human}: {s.Name}";
        }

        /// <summary>
        /// Load a TMA from a stream (e.g., pack resource). We don’t depend on a file path.
        /// Internally copies to a temp file so we can reuse TMAFile.ReadTMAFile(string).
        /// No “disk fallback” — the source is the embedded stream you provide.
        /// </summary>
        public async Task LoadFromResourceStreamAsync(Stream stream, string sourceLabel)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            string temp = Path.Combine(Path.GetTempPath(), $"style_{Guid.NewGuid():N}.tma");
            try
            {
                using (var fs = File.Create(temp))
                {
                    await stream.CopyToAsync(fs).ConfigureAwait(false);
                }

                TMAFile parsed = await Task.Run(() => TMAFile.ReadTMAFile(temp));
                lock (_sync) { _tma = parsed; CurrentPath = sourceLabel; }

                StylesLoaded?.Invoke(this, EventArgs.Empty);
                StylesBytesReset?.Invoke(this, EventArgs.Empty);
                Debug.WriteLine($"[StyleDataService] Loaded from resource '{sourceLabel}', styles={_tma.TextureStyles.Count}");
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
            }
        }

        public void Clear()
        {
            lock (_sync) { _tma = null; CurrentPath = null; }
            StylesCleared?.Invoke(this, EventArgs.Empty);
        }
    }
}
