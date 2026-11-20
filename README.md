# DRUGA MUSIC

A modern Windows desktop music platform foundation. The visual direction combines the editorial feel of Spotify and Apple Music with a stable blue brand identity and artwork-adaptive secondary accents.

## Current milestone — 0.1.0

### Implemented
- Windows desktop application using WPF / .NET 8
- Dark interface with blue brand color
- Main navigation and music-library view
- Import multiple local audio files
- Read basic title, artist and album metadata
- Persistent local library in the user's AppData folder
- Playback, pause/resume, next/previous and volume using NAudio
- Local-library search
- Provider abstraction for future streaming integrations

### Not implemented yet
- Cloud accounts and synchronization
- Deezer / Yandex Music / Spotify integrations
- Streaming catalogue playback
- Playlist editing and advanced search
- Artwork extraction and dynamic artwork colors
- Lyrics, recommendations and installer

Third-party service features will only be implemented through supported APIs and permitted playback mechanisms. A provider must explicitly declare its capabilities; the app must not pretend that a service supports playback when it does not.

## Build

Requirements:
- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 with the .NET desktop development workload

Open DrugaMusic/DrugaMusic.csproj, restore NuGet packages and run.

For a self-contained Windows x64 build:

    dotnet publish DrugaMusic/DrugaMusic.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true

## Architecture
- Models — application domain models
- Services — playback and provider abstractions
- MainWindow — desktop shell and first local-library workflow

## Roadmap
1. Foundation and local playback
2. Metadata, artwork extraction and polished library
3. Playlists, search, favourites and persistence improvements
4. Provider integrations, each validated against the service's current official API and terms
5. Recommendations, adaptive artwork styling and richer player experience
6. Packaging, QA and release
