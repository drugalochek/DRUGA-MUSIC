using DrugaMusic.Models;
namespace DrugaMusic.Services;
public interface IMusicProvider
{
    string Id { get; }
    string DisplayName { get; }
    Task<IReadOnlyList<Track>> SearchAsync(string query, CancellationToken cancellationToken = default);
}