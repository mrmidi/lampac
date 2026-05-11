# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

**Lampac NextGen** is a self-hosted ASP.NET Core (.NET 10) backend for the [Lampa](https://github.com/yumata/lampa) media app. It aggregates streaming links from 70+ sources and exposes them as JSON API plugins. Default port: **9118**.

## Build & Run

```bash
# Build (publishes to ./publish/)
./build.sh

# Equivalent dotnet command
dotnet publish Core/Core.csproj -c Release -o publish

# Run
cd publish && dotnet Core.dll

# Code formatting
./build.sh --format          # runs dotnet format NextGen.slnx

# Clean build artifacts
./build.sh --clean
```

**Environment variables for build.sh:**
- `OUTPUT` — output directory (default: `publish`)
- `CONFIG` — build configuration (default: `Release`)
- `RUNTIME_ID` — target RID (e.g. `linux-x64`, `linux-arm64`)

## Tests

```bash
# Run all tests
dotnet test Tests/TvClient.Tests/TvClient.Tests.csproj

# Run a single test
dotnet test Tests/TvClient.Tests/TvClient.Tests.csproj --filter "FullyQualifiedName~PlaybackSelectionTests"
```

Tests use **xunit** and live under `Tests/TvClient.Tests/`. Currently only `TvClient` has tests.

## Docker

```bash
# Build for current platform (loads into local Docker)
./build-docker.sh

# Build for linux/amd64 on Apple Silicon
./build-docker.sh --amd64

# Build & push multi-arch
./build-docker.sh --all --push --tag v1.2.3

# Run the stack (production — Caddy + Lampac, requires DOMAIN env var)
DOMAIN=lampac.example.com ACME_EMAIL=you@example.com docker compose up -d

# HTTP-only bootstrap (no TLS)
HTTP_ONLY=1 docker compose up -d

# Development instance (port 29118, direct exposure)
docker compose -f docker-compose.dev.yaml up -d
```

The production compose (`docker-compose.yaml`) puts Lampac behind **Caddy** (automatic HTTPS). Dev compose exposes port 29118 directly. A `debug` profile (`lampac-debug-port` service) forwards 127.0.0.1:9118 into the backend network.

## Solution Structure

```
NextGen.slnx          — solution file (open this in IDEs)
Core/                 — ASP.NET Core host (entry point: Core.csproj)
Shared/               — shared library (config, base controllers, services)
Online/               — VOD module (online.js plugin, /lite/... routes)
SISI/                 — Adult content module (sisi.js plugin)
Modules/              — pluggable feature modules
  OnlineRUS/          — Russian VOD sources (Collaps, HDVB, Zetflix, …)
  OnlinePaid/         — Paid sources (Alloha, Filmix, KinoPub, Rezka, …)
  OnlineAnime/        — Anime sources (AniLibria, Kodik, …)
  OnlineENG/          — English sources (VidSrc, VidLink, …)
  OnlineUKR/          — Ukrainian sources
  OnlineGEO/          — Georgian sources
  Adult/              — 18+ platforms (PornHub, Xvideos, …)
  JacRed/             — Jackett-compatible torrent indexer aggregator
  TorrServer/         — embedded torrent server subprocess
  DLNA/               — UPnP/DLNA media server
  Transcoding/        — FFmpeg transcoding (up to 5 streams)
  Sync/               — bookmark/timecode sync (Storage, Sync, SyncEvents, TimeCode)
  Proxy/              — CORS/media proxy modules
  Kit/                — CryptoKit (token validation)
  WatchTogether/      — co-watching sync
  NextHUB/            — YAML-driven adult aggregator
  TvClient/           — TV client with tests
Tests/
  TvClient.Tests/     — xunit tests for TvClient
config/
  base.conf           — base configuration
  example.init.conf   — annotated example config (copy to init.conf)
  example.init.yaml   — YAML alternative
```

## Architecture Overview

### Module System

Lampac uses a **runtime module loading system** built on `AssemblyLoadContext` + optional Roslyn dynamic compilation:

1. At startup (`Startup.ConfigureServices`), `ModuleRepository.UpdateModules()` scans `mods/` then `module/` directories.
2. Subdirectories with a **`manifest.json`** are treated as source-code modules — compiled at startup via **Roslyn** (`CSharpEval`).
3. Pre-compiled `.dll` files are loaded as assembly modules directly.
4. After loading, each module's `IModuleConfigure.Configure` is called; after startup, `IModuleLoaded.Loaded` is called.
5. Modules with `"dynamic": true` in their manifest are watched for file changes and recompiled live.

**`manifest.json` example:**
```json
{ "enable": true, "tree": ["SQL", "ModInit.cs", "ModuleConf.cs", "Controller.cs"] }
```

`LoadModules` in `init.conf` accepts: exact module name (`"Collaps"`), group/folder name (`"OnlineRUS"`), regex (`"LME.*"`), or `".*"` for all.

### Writing a New Module

Every module must have a public `ModInit` class implementing `IModuleLoaded`:

```csharp
public class ModInit : IModuleLoaded, IModuleOnline
{
    public static ModuleConf conf;

    public void Loaded(InitspaceModel baseconf)
    {
        updateConf();
        EventListener.UpdateInitFile += updateConf;  // react to config changes
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
    }

    void updateConf() => conf = ModuleInvoke.Init<ModuleConf>("ModuleName", new ModuleConf { ... });
}
```

- Online module controllers inherit from `BaseOnlineController` (from `Shared`).
- SISI controllers inherit from `BaseSisiController`.
- Routes follow the pattern `/lite/{modulename}` for online sources.
- Register config via `ModuleInvoke.Init("Name", defaultConf)` and subscribe to `EventListener.UpdateInitFile`.

### Configuration

- Main config: `init.conf` (or `init.yaml`) — read at startup and watched (~1s interval)
- On change: `EventListener.UpdateInitFile` fires → modules re-read their config sections
- Backup written to `database/backup/init/`; current state to `current.conf`
- Global config snapshot: `CoreInit.conf` (type: `CoreConf`)

### Middleware Pipeline (simplified)

`ForwardedHeaders` → `BaseMod` → `ModHeaders` → `RequestInfo` → WebSocket (`/nws`) → Routing → Compression → Staticache → early module middleware → `/proxy/` & `/proxyimg` → static files → WAF (optional) → Authorization → Accsdb → late module middleware → rate limits → controllers → RCH endpoints

See `Core/Startup.cs` for the exact order and conditionals.

### Key Services (Shared/)

| Service | Purpose |
|---|---|
| `Http` / `FriendlyHttp` | HTTP client wrappers with proxy support |
| `HybridCache` / `HybridFileCache` | In-memory + file cache |
| `CSharpEval` | Roslyn compilation for dynamic modules |
| `GeoIP2` | MaxMind-based geo lookup |
| `CryptoKit` | Kit token validation |
| `PlaywrightCore/` | Chromium/Firefox for JS-protected sites |

### Publish Layout

When `dotnet publish` runs, `Core.csproj` copies source trees into the output:
- `Modules/**` → `module/`
- `Online/`, `SISI/` → `mods/`

So the runtime loads these as source-module directories and compiles them with Roslyn.
