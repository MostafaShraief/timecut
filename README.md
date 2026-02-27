# Timecut - Bulk HTML Recorder

Timecut is an ASP.NET Core backend + static web UI for rendering HTML animations into video files.

## Features

- Batch HTML recording via drag-and-drop.
- Configurable concurrent rendering jobs.
- Live progress updates with server-sent events.
- FFmpeg-based encoding pipeline.

## Prerequisites

- .NET 9 SDK
- FFmpeg available on your system `PATH`

## Setup

1. Restore and build:

```bash
dotnet build
```

2. Install Playwright browsers:

```bash
pwsh .\\bin\\Debug\\net9.0\\playwright.ps1 install
```

## Run

```bash
dotnet run
```

Then open the URL shown in the terminal (typically `http://localhost:5xxx`) and navigate to `/index.html`.

## API

- `POST /record` - Create a render job.
- `POST /concurrency` - Set max concurrent jobs.
- `POST /stop/{id}` - Stop a running job.
- `GET /progress/{id}` - Receive SSE progress updates.

## Troubleshooting

- If FFmpeg is not found, verify `ffmpeg -version` works in a new terminal.
- If browser binaries are missing, rerun the Playwright install command.
