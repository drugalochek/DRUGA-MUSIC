using System.IO;
using System.Text;
using DrugaMusic.Models;

namespace DrugaMusic.Services;

public sealed class PlaylistExportImporter
{
    private static readonly string[] TitleHeaders = ["title", "track", "track name", "name", "song"];
    private static readonly string[] ArtistHeaders = ["artist", "artists", "artist name", "performer"];
    private static readonly string[] AlbumHeaders = ["album", "album name"];
    private static readonly string[] IdHeaders = ["id", "track id", "deezer id", "spotify id", "yandex id"];
    private static readonly string[] IsrcHeaders = ["isrc"];
    private static readonly string[] UrlHeaders = ["url", "track url", "link", "uri"];

    public List<ExternalTrack> ImportCsv(string filePath, string provider)
    {
        using var reader = new StreamReader(filePath, Encoding.UTF8, true);
        var firstLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(firstLine)) return new List<ExternalTrack>();

        var delimiter = DetectDelimiter(firstLine);
        var headers = ParseCsvLine(firstLine, delimiter).Select(NormalizeHeader).ToList();
        var titleIndex = FindHeader(headers, TitleHeaders);
        var artistIndex = FindHeader(headers, ArtistHeaders);
        if (titleIndex < 0 || artistIndex < 0)
            throw new InvalidDataException("В CSV не найдены обязательные столбцы с названием трека и исполнителем.");

        var albumIndex = FindHeader(headers, AlbumHeaders);
        var idIndex = FindHeader(headers, IdHeaders);
        var isrcIndex = FindHeader(headers, IsrcHeaders);
        var urlIndex = FindHeader(headers, UrlHeaders);
        var imported = new List<ExternalTrack>();
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var values = ParseCsvLine(line, delimiter);
            string ValueAt(int index) => index >= 0 && index < values.Count ? values[index].Trim() : "";
            var title = ValueAt(titleIndex);
            var artist = ValueAt(artistIndex);
            if (title.Length == 0 || artist.Length == 0) continue;

            imported.Add(new ExternalTrack
            {
                Provider = provider,
                ProviderTrackId = ValueAt(idIndex),
                Title = title,
                Artist = artist,
                Album = ValueAt(albumIndex),
                Isrc = ValueAt(isrcIndex),
                SourceUrl = ValueAt(urlIndex)
            });
        }
        return imported;
    }

    private static char DetectDelimiter(string line) =>
        new[] { ',', ';', '\t' }
            .Select(delimiter => new { Delimiter = delimiter, Count = ParseCsvLine(line, delimiter).Count })
            .OrderByDescending(candidate => candidate.Count)
            .First().Delimiter;

    private static int FindHeader(IReadOnlyList<string> headers, IEnumerable<string> aliases)
    {
        var normalizedAliases = aliases.Select(NormalizeHeader).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
            if (normalizedAliases.Contains(headers[i])) return i;
        return -1;
    }

    private static string NormalizeHeader(string value) =>
        value.Trim().Trim('\uFEFF').ToLowerInvariant().Replace("_", " ").Replace("-", " ");

    private static List<string> ParseCsvLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];
            if (character == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else quoted = !quoted;
            }
            else if (character == delimiter && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else field.Append(character);
        }
        fields.Add(field.ToString());
        return fields;
    }
}
