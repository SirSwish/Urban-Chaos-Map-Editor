using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using UrbanChaosMapEditor.Services.DataServices;
using UrbanChaosMapEditor.ViewModels;

public sealed class LightEditorViewModel : BaseViewModel
{
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
           => RaisePropertyChanged(name);

    public ObservableCollection<LightEntryViewModel> Entries { get; } = new();
    private LightEntryViewModel? _selectedEntry;
    public LightEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set { _selectedEntry = value; OnPropertyChanged(); }
    }

    public ICommand AddLightCommand { get; }
    public ICommand DeleteSelectedLightCommand { get; }

    public LightEditorViewModel()
    {
        AddLightCommand = new RelayCommand(_ => AddLight(), _ => true);
        DeleteSelectedLightCommand = new RelayCommand(_ => DeleteSelected(), _ => SelectedEntry != null);

        // (re)hydrate from LightsDataService bytes on startup or on BytesReset
        LightsDataService.Instance.LightsBytesReset += (_, __) =>
        {
            // Parse bytes -> Entries, props, flags etc.
            LoadFromBytes(LightsDataService.Instance.GetBytesCopy());
        };
    }

    private void AddLight()
    {
        // find free slot, create default entry, add to Entries,
        // write back to bytes via a small helper (pack -> data service)
        // then mark dirty.
    }

    private void DeleteSelected()
    {
        if (SelectedEntry == null) return;
        // mark entry as unused, write back to bytes, mark dirty
    }

    private void LoadFromBytes(byte[] bytes)
    {
        // parse header/entries/properties and populate observable properties
        // (don’t raise file-level events; only update VM state)
    }

    // whenever a property on SelectedEntry or Entries changes,
    // re-pack bytes and LightsDataService.Instance.MarkDirty()
}
