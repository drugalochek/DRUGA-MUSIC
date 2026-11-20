namespace DrugaMusic.Models;
public sealed class Track
{
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
 public bool IsFavorite { get; set; }
}