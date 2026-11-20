namespace DrugaMusic.Models;

public sealed class ExternalTrack
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Provider { get; set; } = "";
    public string ProviderTrackId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string Isrc { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public string? LocalFilePath { get; set; }
}
