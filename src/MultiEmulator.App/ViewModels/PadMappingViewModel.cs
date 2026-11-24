using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiEmulator.App.Input;
using MultiEmulator.App.Services;

namespace MultiEmulator.App.ViewModels;

public partial class PadMappingViewModel : ViewModelBase
{
    private readonly EmulatorHost _host;
    private readonly Action? _onChanged;

    public ObservableCollection<PadBindingRowViewModel> Rows { get; } = new();

    public PadMappingViewModel(EmulatorHost host, Action? onChanged = null)
    {
        _host = host;
        _onChanged = onChanged;
        ReloadRows();
    }

    public void ReloadRows()
    {
        Rows.Clear();
        foreach (var (control, label) in XboxPadMapping.ControlLabels)
            Rows.Add(new PadBindingRowViewModel(control, label, _host.PadMapping.GetKeys(control), Persist));
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        _host.ResetPadMappingToDefaults();
        ReloadRows();
        _onChanged?.Invoke();
    }

    private void Persist(XboxPadControl control, IReadOnlyList<string> keys)
    {
        _host.PadMapping.SetKeys(control, keys);
        _host.SavePadMappingSettings();
        _onChanged?.Invoke();
    }
}

public partial class PadBindingRowViewModel : ViewModelBase
{
    private readonly Action<XboxPadControl, IReadOnlyList<string>> _persist;
    private readonly List<string> _keys = new();

    public XboxPadControl Control { get; }
    public string Label { get; }
    public IReadOnlyList<SpectrumKeyEntry> AvailableKeys => SpectrumKeyTokens.Catalog;
    public ObservableCollection<PadKeyChipViewModel> KeyChips { get; } = new();

    [ObservableProperty] private SpectrumKeyEntry? _selectedToAdd;
    [ObservableProperty] private string _keysSummary = "";

    public PadBindingRowViewModel(
        XboxPadControl control,
        string label,
        IEnumerable<string> initialKeys,
        Action<XboxPadControl, IReadOnlyList<string>> persist)
    {
        Control = control;
        Label = label;
        _persist = persist;
        foreach (var k in initialKeys)
            _keys.Add(k);
        RebuildChips();
    }

    [RelayCommand]
    private void AddSelected()
    {
        if (SelectedToAdd == null) return;
        string id = SelectedToAdd.Id.ToUpperInvariant();
        if (_keys.Any(k => k.Equals(id, StringComparison.OrdinalIgnoreCase)))
            return;
        _keys.Add(id);
        Commit();
    }

    [RelayCommand]
    private void ClearKeys()
    {
        _keys.Clear();
        Commit();
    }

    private void RemoveKey(string id)
    {
        var match = _keys.FirstOrDefault(k => k.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (match == null) return;
        _keys.Remove(match);
        Commit();
    }

    private void Commit()
    {
        RebuildChips();
        _persist(Control, _keys.ToList());
    }

    private void RebuildChips()
    {
        KeyChips.Clear();
        foreach (var id in _keys)
        {
            string captured = id;
            KeyChips.Add(new PadKeyChipViewModel(
                SpectrumKeyTokens.LabelOf(id),
                () => RemoveKey(captured)));
        }
        KeysSummary = _keys.Count == 0
            ? "(ninguna)"
            : string.Join(" + ", _keys.Select(SpectrumKeyTokens.LabelOf));
    }
}

public sealed class PadKeyChipViewModel
{
    public string Display { get; }
    public IRelayCommand RemoveCommand { get; }

    public PadKeyChipViewModel(string display, Action remove)
    {
        Display = display;
        RemoveCommand = new RelayCommand(remove);
    }
}
