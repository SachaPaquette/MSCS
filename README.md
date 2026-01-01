# MSCS

MSCS is a Windows desktop application for reading and tracking manga. Built with WPF on .NET, it provides a library-first experience that seamlessly combines local files, online sources, and tracking integrations.

The goal of MSCS is to offer a clean, customizable reader while keeping your local library and online tracking services in sync.

## Features

- Local library management for folders and CBZ-based manga chapters
- Online sources for browsing and reading, including MangaDex and Madara-based sites
- Configurable reading experience with persistent progress tracking
- Tracking integrations with AniList, MyAnimeList, and Kitsu
- Library-first design focused on offline-friendly usage

## Tracking Integrations

MSCS supports the following tracking services:
- AniList
- MyAnimeList
- Kitsu

Tracking integrations allow MSCS to:
- Sync reading progress automatically
- Update chapter counts on supported services
- Keep your local library aligned with your online lists

Each tracking service must be configured individually through the application settings.

### MyAnimeList Client ID Setup

To enable MyAnimeList tracking, you must create a MyAnimeList API client ID and configure it in the application.

Steps:

1. Sign in to MyAnimeList and open the API configuration page:
   https://myanimelist.net/apiconfig

2. Create a new application and copy the generated client ID.

3. Set the application redirect URI to:
   http://127.0.0.1:51789/callback/

   This value must match the local callback listener used by MSCS.

4. Open MSCS and navigate to:
   Settings > Tracking Integrations

   Paste the MyAnimeList client ID into the corresponding field.

Notes:
- No client secret is required.
- The redirect URI must match exactly or authentication will fail.
- This setup only needs to be completed once.

## Known Limitations

- Windows-only application 
- Internet access is required for online sources and tracking services
- Tracking functionality depends on third-party APIs and their availability

## Roadmap

Planned features and improvements include:
- Additional online manga sources
- Improved tracking synchronization and reliability
- Performance optimizations for large libraries
- Ongoing UI and UX refinements

## License

This project is licensed under the MIT License.
