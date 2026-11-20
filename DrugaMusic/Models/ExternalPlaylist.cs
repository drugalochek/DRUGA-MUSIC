namespace DrugaMusic.Models;

public sealed class ExternalPlaylist
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Provider { get; set; } = "";
    public string ProviderPlaylistId { get; set; } = "";
    public string Name { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public List<string> TrackIds { get; set; } = new();
}
