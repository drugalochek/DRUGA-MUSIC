# SONORA

SONORA is a Windows desktop music player focused on a fast, local-first music library.

## Current milestone — 0.3.0

### Working features
- Windows desktop application built with WPF and .NET 8
- Dark interface and SONORA Windows application icon
- Import MP3, WAV, AIFF and AIF audio files
- Read title, artist, album, genre and year metadata
- Extract embedded album artwork into the local artwork cache
- Persistent local library and favourite tracks
- Create and delete playlists; add tracks and play a playlist
- Search local tracks by title, artist, album and genre
- Playback, pause/resume, previous/next and automatic next-track playback
- Playback queue, shuffle and repeat modes
- Seek through the current track
- Volume control

## Data storage

The library, playlists and extracted artwork are stored under:

`%LocalAppData%\SONORA`

On first launch after the rebrand, the application attempts to migrate the previous
`%LocalAppData%\DrugaMusic` data directory so existing libraries are preserved.

## Build

Requirements: Windows 10/11 and .NET 8 SDK.

```powershell
dotnet restore DrugaMusic.sln
dotnet build DrugaMusic.sln --configuration Release
dotnet publish DrugaMusic/DrugaMusic.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

## Current scope

SONORA currently plays local audio files. Streaming integrations, cloud accounts,
lyrics, recommendations, installer packaging and an audio equalizer are not included.
The player includes a volume control; no equalizer is exposed in this milestone.

Third-party services will only be integrated through supported official APIs and
permitted playback mechanisms.
