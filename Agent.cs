using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ValoTools;

public sealed class Agent : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Role { get; init; }
    public required string IconUrl { get; init; }
    public required string PortraitUrl { get; init; }
    public string? RoleIconUrl { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
