# Timecut

Timecut is a desktop application built with .NET 9 MAUI Blazor Hybrid. It allows you to capture HTML animations and render them into MP4 videos using Playwright and FFmpeg.

## Features

- **Desktop Application**: Built as a Windows desktop app using MAUI and Blazor Hybrid.
- **HTML to Video**: Captures HTML animations frame-by-frame using Playwright.
- **Video Encoding**: Encodes captured frames into high-quality MP4 videos using FFmpeg.
- **Templates**: Create and manage HTML templates with a built-in code editor (Monaco).
- **Presets**: Save and reuse rendering configurations (resolution, framerate, duration).
- **History**: Keep track of your rendered videos and view them within the app.
- **Local Storage**: Uses SQLite for local data persistence.

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [FFmpeg](https://ffmpeg.org/download.html) (Must be installed and available in your system's PATH)
- Windows 10 or later (The app is currently configured for Windows)

## Getting Started

1. **Clone the repository**
   \\\ash
   git clone <repository-url>
   cd timecut
   \\\

2. **Install Playwright Browsers**
   The application uses Playwright to render HTML. You need to install the required browsers:
   \\\ash
   pwsh bin/Debug/net9.0-windows10.0.19041.0/win10-x64/playwright.ps1 install
   \\\
   *(Note: The path to playwright.ps1 might vary depending on your build configuration. You can also run dotnet build first to generate it).*

3. **Build and Run**
   You can run the application directly using the .NET CLI:
   \\\ash
   dotnet run --project src/Timecut.App/Timecut.App.csproj
   \\\
   Or open Timecut.sln in Visual Studio 2022 and run the Timecut.App project.

## Architecture

The project follows a Clean Architecture approach:

- **Timecut.Core**: Contains domain entities (Template, Preset, RenderHistory) and interfaces.
- **Timecut.Infrastructure**: Implements data access (SQLite/EF Core) and the video rendering engine (Playwright + FFmpeg).
- **Timecut.UI**: Contains the Blazor components, pages, and MudBlazor UI logic.
- **Timecut.App**: The MAUI application host that wraps the Blazor UI in a native desktop window.

## Technologies Used

- .NET 9
- MAUI (Multi-platform App UI)
- Blazor Hybrid
- MudBlazor (UI Components)
- BlazorMonaco (Code Editor)
- Entity Framework Core (SQLite)
- Microsoft.Playwright
- FFmpeg

## License

This project is licensed under the MIT License.
