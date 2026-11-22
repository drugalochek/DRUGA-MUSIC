using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DrugaMusic.Models;

public sealed class Track : INotifyPropertyChanged
{
    private bool isFavorite;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Unknown title";
    public string Artist { get; set; } = "Unknown artist";
    public string Album { get; set; } = "";
    public string Genre { get; set; } = "";
    public uint? Year { get; set; }
    public string FilePath { get; set; } = "";
    public string ArtworkPath { get; set; } = "";
    public string ProviderId { get; set; } = "local";
    public string DurationText { get; set; } = "";

    public bool IsFavorite
    {
        get => isFavorite;
        set
        {
            if (isFavorite == value) return;
            isFavorite = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FavoriteGlyph));
        }
    }

    [JsonIgnore]
    public string FavoriteGlyph => IsFavorite ? "♥" : "♡";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}