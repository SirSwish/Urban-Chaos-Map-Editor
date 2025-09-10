// Services/BuildingsSelectionBus.cs
using System;

namespace UrbanChaosMapEditor.Services
{
    public sealed class BuildingsSelectionBus
    {
        private static readonly Lazy<BuildingsSelectionBus> _lazy = new(() => new BuildingsSelectionBus());
        public static BuildingsSelectionBus Instance => _lazy.Value;
        private BuildingsSelectionBus() { }

        public sealed class SelectionChangedEventArgs : EventArgs
        {
            public int? BuildingId { get; }
            public int? StoreyId { get; }
            public int? FacetId1 { get; }
            public SelectionChangedEventArgs(int? buildingId, int? storeyId, int? facetId1)
            {
                BuildingId = buildingId;
                StoreyId = storeyId;
                FacetId1 = facetId1;
            }
        }

        public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

        public void SelectBuilding(int buildingId)
            => SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(buildingId, null, null));

        public void SelectStorey(int buildingId, int storeyId)
            => SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(buildingId, storeyId, null));

        public void SelectFacet(int buildingId, int storeyId, int facetId1)
            => SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(buildingId, storeyId, facetId1));

        public void Clear()
            => SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(null, null, null));
    }
}
