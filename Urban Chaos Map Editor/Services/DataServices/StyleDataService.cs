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
