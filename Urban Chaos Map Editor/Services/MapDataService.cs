﻿using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace UrbanChaosMapEditor.Services
{
    public sealed class MapDataService
    {
        private static readonly Lazy<MapDataService> _lazy = new(() => new MapDataService());
        public static MapDataService Instance => _lazy.Value;

        private readonly object _sync = new();

        private MapDataService() { }
        private (int Start, int Length) _buildingRegion = (-1, 0);

        public byte[]? MapBytes { get; private set; }
        public string? CurrentPath { get; private set; }

        public bool IsLoaded => MapBytes is not null;
        public bool HasChanges { get; private set; }

        public long SizeBytes => MapBytes?.LongLength ?? 0;

        public event EventHandler<MapLoadedEventArgs>? MapLoaded;
        public event EventHandler? MapCleared;
        public event EventHandler<MapSavedEventArgs>? MapSaved;
        public event EventHandler? DirtyStateChanged;
        public event EventHandler<MapBytesResetEventArgs>? MapBytesReset;

        public async Task LoadAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
            var full = Path.GetFullPath(path);

            byte[] bytes = await File.ReadAllBytesAsync(full); // resume on UI ctx
            lock (_sync)
            {
                MapBytes = bytes;
                CurrentPath = full;
                HasChanges = false;
            }

            MapLoaded?.Invoke(this, new MapLoadedEventArgs(full, bytes.Length));
            RecentFilesService.Instance.Add(path);
            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void NewFromTemplate(byte[] templateBytes)
        {
            if (templateBytes is null) throw new ArgumentNullException(nameof(templateBytes));
            lock (_sync)
            {
                MapBytes = (byte[])templateBytes.Clone();
                CurrentPath = null;
                HasChanges = true;
            }
            MapBytesReset?.Invoke(this, new MapBytesResetEventArgs(templateBytes.Length)); // NEW
            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Clear()
        {
            lock (_sync)
            {
                MapBytes = null;
                CurrentPath = null;
                HasChanges = false;
            }

            MapCleared?.Invoke(this, EventArgs.Empty);
            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Replace the in-memory bytes and mark dirty.</summary>
        public void ReplaceBytes(byte[] bytes)
        {
            if (bytes is null) throw new ArgumentNullException(nameof(bytes));
            lock (_sync) { MapBytes = bytes; }
            MapBytesReset?.Invoke(this, new MapBytesResetEventArgs(bytes.Length)); // NEW
            MarkDirty();
        }

        /// <summary>
        /// Safely mutate the live byte buffer; marks dirty automatically.
        /// </summary>
        public void Edit(Action<byte[]> mutate)
        {
            if (mutate is null) throw new ArgumentNullException(nameof(mutate));
            if (!IsLoaded) throw new InvalidOperationException("No map loaded.");

            lock (_sync) { mutate(MapBytes!); }
            MarkDirty();
        }

        /// <summary>
        /// Async variant for mutations; marks dirty automatically.
        /// </summary>
        public async Task EditAsync(Func<byte[], Task> mutateAsync)
        {
            if (mutateAsync is null) throw new ArgumentNullException(nameof(mutateAsync));
            if (!IsLoaded) throw new InvalidOperationException("No map loaded.");

            // No lock held during await; let the editor manage discrete edits.
            var bytes = MapBytes!;
            await mutateAsync(bytes);
            MarkDirty();
        }

        /// <summary>Read-only view without copying (do not store long-term if you plan to mutate).</summary>
        public ReadOnlyMemory<byte> AsReadOnlyMemory() => new(MapBytes ?? Array.Empty<byte>());

        /// <summary>Return a defensive copy of the current bytes.</summary>
        public byte[] GetBytesCopy()
        {
            if (!IsLoaded) throw new InvalidOperationException("No map loaded.");
            lock (_sync) { return (byte[])MapBytes!.Clone(); }
        }

        public void MarkDirty()
        {
            bool raise = false;
            lock (_sync)
            {
                if (!HasChanges) { HasChanges = true; raise = true; }
            }
            if (raise) DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearDirty()
        {
            bool raise = false;
            lock (_sync)
            {
                if (HasChanges) { HasChanges = false; raise = true; }
            }
            if (raise) DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task SaveAsync()
        {
            if (!IsLoaded) throw new InvalidOperationException("No map loaded.");
            if (string.IsNullOrWhiteSpace(CurrentPath)) throw new InvalidOperationException("No save path set.");

            byte[] snapshot;
            lock (_sync) { snapshot = (byte[])MapBytes!.Clone(); }

            await File.WriteAllBytesAsync(CurrentPath!, snapshot);
            ClearDirty();
            MapSaved?.Invoke(this, new MapSavedEventArgs(CurrentPath!));
        }

        public async Task SaveAsAsync(string newPath)
        {
            if (!IsLoaded) throw new InvalidOperationException("No map loaded.");
            if (string.IsNullOrWhiteSpace(newPath)) throw new ArgumentException("Path is required.", nameof(newPath));

            var full = Path.GetFullPath(newPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);

            byte[] snapshot;
            lock (_sync) { snapshot = (byte[])MapBytes!.Clone(); }

            await File.WriteAllBytesAsync(full, snapshot);
            lock (_sync) { CurrentPath = full; }
            ClearDirty();
            RecentFilesService.Instance.Add(newPath);
            MapSaved?.Invoke(this, new MapSavedEventArgs(full));
        }

        public void DumpWholeBuildingBlockToDebug()
        {
            if (!IsLoaded)
            {
                Debug.WriteLine("[Buildings] No map loaded.");
                return;
            }

            var bytes = GetBytesCopy();

            // V1 header: [int32 saveType][int32 objectSize]
            int saveType = BitConverter.ToInt32(bytes, 0);
            int objectBytesFromHeader = BitConverter.ToInt32(bytes, 4);

            // V1 corrections
            int sizeAdjustment = (saveType >= 25) ? 2000 : 0;

            // Where object section begins (offset of NumObjects int32)
            int objectOffset = bytes.Length - 12 - sizeAdjustment - objectBytesFromHeader + 8;

            // Where building section begins: immediately after header + tiles
            const int tileBytes = 128 * 128 * 6;
            int buildingStart = 8 + tileBytes;
            int buildingEnd = objectOffset;
            int buildingLen = buildingEnd - buildingStart;

            Debug.WriteLine(
                $"[Buildings] fileLen={bytes.Length} saveType={saveType} objBytes(hdr)={objectBytesFromHeader} " +
                $"sizeAdj={sizeAdjustment} objOff=0x{objectOffset:X} " +
                $"building=[0x{buildingStart:X}..0x{buildingEnd:X}) len={buildingLen}"
            );

            // Guard rails
            if (buildingStart < 0 || buildingEnd > bytes.Length || buildingLen <= 0)
            {
                Debug.WriteLine("[Buildings] Bounds look wrong; aborting.");
                return;
            }

            DumpHex(bytes, buildingStart, buildingLen);
        }

        public void ComputeAndCacheBuildingRegion()
        {
            // Default: no region
            _buildingRegion = (-1, 0);

            if (!IsLoaded) return;

            var bytes = GetBytesCopy();
            if (bytes == null || bytes.Length < 12) return;

            // Header fields at file start (as before)
            int saveType = BitConverter.ToInt32(bytes, 0);
            int objectBytesFromHeader = BitConverter.ToInt32(bytes, 4);
            int sizeAdjustment = (saveType >= 25) ? 2000 : 0;

            // Where the object section begins (offset of NumObjects int32) — your V1 formula
            int objectOffset = bytes.Length - 12 - sizeAdjustment - objectBytesFromHeader + 8;

            // -------- Strict finder (preferred) --------
            if (objectOffset > 0 &&
                objectOffset <= bytes.Length &&
                BuildingsAccessor.TryFindRegion(bytes, objectOffset, out int headerOff, out int regionLen))
            {
                _buildingRegion = (headerOff, regionLen);
                System.Diagnostics.Debug.WriteLine(
                    $"[Buildings] Region (strict) cached: start=0x{headerOff:X} len={regionLen} (end=0x{headerOff + regionLen:X}) saveType={saveType}");
                return;
            }

            // -------- Fallback to V1 heuristic --------
            const int tileBytes = 128 * 128 * 6;
            int buildingStart = 8 + tileBytes;
            int buildingEnd = Math.Clamp(objectOffset, 0, bytes.Length);
            int buildingLen = buildingEnd - buildingStart;

            if (buildingStart >= 0 && buildingEnd <= bytes.Length && buildingLen > 0)
            {
                _buildingRegion = (buildingStart, buildingLen);
                System.Diagnostics.Debug.WriteLine(
                    $"[Buildings] Region (fallback) cached: start=0x{buildingStart:X} len={buildingLen} (end=0x{buildingEnd:X}) saveType={saveType}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Buildings] Failed to determine building region. objectOff=0x{objectOffset:X} fileLen={bytes.Length}");
            }
        }


        public bool TryGetBuildingRegion(out int start, out int length)
        {
            start = _buildingRegion.Start;
            length = _buildingRegion.Length;
            return (start >= 0 && length > 0);
        }

        private static void DumpHex(byte[] b, int off, int count)
        {
            int end = off + count;
            for (int p = off; p < end; p += 16)
            {
                int n = Math.Min(16, end - p);
                var sb = new StringBuilder();
                sb.Append(p.ToString("X06")).Append(": ");
                for (int i = 0; i < n; i++)
                    sb.Append(b[p + i].ToString("X2")).Append(' ');
                Debug.WriteLine(sb.ToString().TrimEnd());
            }
        }



    }

    public sealed class MapLoadedEventArgs : EventArgs
    {
        public string Path { get; }
        public int Length { get; }
        public MapLoadedEventArgs(string path, int length) { Path = path; Length = length; }
    }

    public sealed class MapSavedEventArgs : EventArgs
    {
        public string Path { get; }
        public MapSavedEventArgs(string path) { Path = path; }
    }

    public sealed class MapBytesResetEventArgs : EventArgs
    {
        public int Length { get; }
        public MapBytesResetEventArgs(int length) { Length = length; }
    }


}
