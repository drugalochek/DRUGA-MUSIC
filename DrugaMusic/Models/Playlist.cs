namespace DrugaMusic.Models;
public sealed class Playlist
{
 public string Id { get; set; } = Guid.NewGuid().ToString("N");
 public string Name { get; set; } = "Новый плейлист";
 public DateTime CreatedAt { get; set; } = DateTime.Now;
 public List<string> TrackIds { get; set; } = new();
}