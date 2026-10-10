# syntax=docker/dockerfile:1

# =================================================================================================
# Build stage
# =================================================================================================
# global.json pins the SDK to "10.0" with rollForward "latestFeature", so the image must carry a
# .NET 10 SDK: a 9.x SDK cannot satisfy that pin and every dotnet command would fail up front with
# "compatible .NET SDK was not found". EF Core / Pomelo staying on the .NET 9 package line (see
# AGENTS.md) does not change this — those are NuGet packages running on the net10.0 runtime.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# --- restore layer -------------------------------------------------------------------------------
# Only the files that describe the dependency graph are copied here. As long as no project file and
# no pinned package version changes, Docker reuses this layer and the NuGet packages it downloaded,
# so editing source code no longer costs a full restore. Copying the sources first would invalidate
# it on every single edit — which is the whole point of splitting the two.
COPY global.json Directory.Build.props Directory.Build.targets Directory.Packages.props ./

# COPY flattens every match into the destination directory, so the project files land side by side
# in /src and are then moved back under the directory that carries their name. Every project in this
# repository lives at <Name>/<Name>.csproj; if that convention is ever broken, the restore below
# fails loudly with a missing-project error rather than silently building something else.
COPY */*.csproj ./
RUN for proj in *.csproj; do \
        mkdir -p "${proj%.csproj}" && mv "$proj" "${proj%.csproj}/"; \
    done

# Restoring Vortex.Main pulls in its full ProjectReference graph. The test projects are copied above
# but deliberately not restored — the image only ships the host.
#
# The web API needs its own line, and that is not tidiness. It is a loadable plugin, so Vortex.Main
# does not reference it and it is not in that restore. Without this line the publish below fails
# with NETSDK1004, "assets file not found" — and only in the image: a developer's tree still has
# obj/project.assets.json from an earlier build, so the same command succeeds locally and says nothing.
RUN dotnet restore Vortex.Main/Vortex.Main.csproj \
    && dotnet restore Vortex.WebApi/Vortex.WebApi.csproj

# --- source + publish ----------------------------------------------------------------------------
COPY . .

# --no-restore keeps the cached restore layer authoritative: without it the publish would hit the
# network again and the layering above would buy nothing.
RUN dotnet publish Vortex.Main/Vortex.Main.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish

# The web API, published SEPARATELY, because the host does not reference it: it is a loadable
# plugin, and a project reference is exactly what would make it impossible to unload (see the
# comment in Vortex.Main.csproj). Missing, the image ships a hotel with no website backend and
# nothing says so: the game runs, and :8080 answers nothing.
#
# Into a folder named after the plugin key, with its manifest.json beside the assembly, which is the
# shape PluginManager discovers. The Vortex.* assemblies published alongside it are never loaded
# from here: ByteLoadingAlc hands any assembly the host already holds to the default context, so one
# IVortexPlugin and one VortexDbContext type exist in the process.
RUN dotnet publish Vortex.WebApi/Vortex.WebApi.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish-plugins/turbo-webapi

# =================================================================================================
# Runtime stage
# =================================================================================================
# Vortex.Main is a console host (OutputType Exe), but Vortex.WebApi starts its own Kestrel
# WebApplication and references the Microsoft.AspNetCore.App shared framework. The plain `runtime`
# image does not carry it, so the host would die on the first WebApplication build — `aspnet` is
# the correct base here, not `runtime`.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The published output, owned by the non-root `app` user that the official .NET images provide
# (uid 1654). Copying with --chown avoids a second full-size layer just to fix ownership.
COPY --from=build --chown=app:app /app/publish ./

# The plugins that ship WITH the image: today the web API alone.
#
# WARNING FOR THE OPERATOR: a volume mounted at /app/plugins REPLACES this directory, and the hotel
# comes back with no website backend. The loader scans every subdirectory of the plugin folder and
# expects a manifest.json in each, so there is no "extra plugins" subfolder to mount instead. Ship
# plugins in the image; to add one outside it, mount the volume AND seed it with this directory's
# contents.
COPY --from=build --chown=app:app /app/publish-plugins /app/plugins/

# Writable directories the host expects under its content root, created while still root:
#   logs/    — Vortex:Observability:AuditDeadLetterPath defaults to logs/audit-dead-letter.jsonl
#   plugins/ — the plugin loader probes AppContext.BaseDirectory/plugins
RUN mkdir -p /app/logs /app/plugins \
    && chown -R app:app /app/logs /app/plugins

# curl, for one reason: a container healthcheck runs INSIDE the container, and this image ships no
# HTTP client at all. An orchestrator that probes it gets `curl: not found`, reads that as the
# application being unhealthy, and rolls the deployment back — while the host itself has started
# perfectly and says so in its own log. That failure is indistinguishable from a crash unless you
# read the healthcheck output, so the fix belongs here rather than in a runbook.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

USER app

# `/health` on the WEB API port, not the game socket. 30001 speaks the Habbo WebSocket handshake and
# answers an HTTP GET with nothing an orchestrator can read — probing it marks a healthy hotel as
# broken. `--fail` makes curl exit non-zero on a 4xx/5xx instead of happily reporting an error page.
#
# `/health` is LIVENESS: 200 while the process answers, whatever the database does. A probe that
# fails here restarts the container, and a restart cures a dead process, not a database that is
# away. Readiness — listeners open, not draining, database reachable — is `/ready`, for the proxy
# or load balancer that decides whether to route players here; it is deliberately not probed here.
#
# start-period is generous on purpose: the host loads the whole catalogue before it listens —
# 55 000 furniture definitions and 2 100 catalogue pages on the tree this was written against, which
# took ~9 seconds. A short period fails the first probe and rolls back a deployment that was fine.
HEALTHCHECK --interval=30s --timeout=5s --start-period=90s --retries=3 \
    CMD curl --fail --silent --show-error http://localhost:8080/health || exit 1

# Documentation only — publishing these is docker-compose.yml's job.
#   30000 game TCP socket, 30001 game WebSocket socket, 8080 web API.
# The Orleans silo (11111) and gateway (3000) ports are deliberately absent: this is a single
# in-process silo and nothing outside the container should reach them.
EXPOSE 30000 30001 8080

ENTRYPOINT ["dotnet", "Vortex.Main.dll"]
