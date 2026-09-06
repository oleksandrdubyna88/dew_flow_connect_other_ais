# Build context is the REPOSITORY ROOT, not this folder: central package management
# (Directory.Packages.props), the SDK pin (global.json) and the feed list (nuget.config)
# all live there, and a build that cannot see them resolves different versions than CI.
#
#   docker build -f src_server/Dockerfile -t coai-server .
#
# The publish is NATIVE AOT: the runtime stage carries no .NET at all. Build per-architecture
# on a native runner (TARGETARCH is honoured), then stitch a manifest; AOT does not
# cross-compile inside qemu.

# ---------------------------------------------------------------------------
# build
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /source

# Native AOT links with the platform toolchain — this is the whole extra cost.
RUN apt-get update \
    && apt-get install -y --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

# Restore on the project files alone first, so a source-only edit reuses this layer.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src_server/src/CoaiServer.csproj src_server/src/
COPY src_mcp/core/CoaiMcp.Core.csproj src_mcp/core/
COPY src_mcp/runners/CoaiMcp.Runners.csproj src_mcp/runners/
COPY src_mcp/service_defaults/CoaiMcp.ServiceDefaults.csproj src_mcp/service_defaults/
RUN dotnet restore src_server/src/CoaiServer.csproj -a $TARGETARCH

COPY src_server/src/ src_server/src/
COPY src_mcp/core/ src_mcp/core/
COPY src_mcp/runners/ src_mcp/runners/
COPY src_mcp/service_defaults/ src_mcp/service_defaults/
RUN mkdir /empty
# The version the release tag names — the log and /api/catalog report it. A local build says so.
ARG VERSION=0.0.0-local
RUN dotnet publish src_server/src/CoaiServer.csproj \
        -c Release \
        -a $TARGETARCH \
        -o /app \
        --no-restore \
        -p:Version=$VERSION

# ---------------------------------------------------------------------------
# runtime — NOT chiseled, unlike the vault's.
#
# This image has to run three vendor CLIs, and their installers are shell scripts fetched
# with curl. A chiseled image has neither. So this is `runtime-deps:10.0-noble` plus curl
# and ca-certificates, and nothing else — in particular NO Node: an earlier draft carried
# Node 22 on the assumption that the CLIs arrive through npm. They do not. All three
# vendors publish standalone binaries through their own installers, verified on the target
# VM in a plain debian:bookworm-slim with no node on PATH at all.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble AS runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# The account the CLIs are installed as, and the account the server runs as — the SAME one,
# because the installers put binaries under $HOME and `claude` and `codex` are symlinks into
# it rather than real files in ~/.local/bin. Installing as root and running as 10001 would
# leave two of the three CLIs pointing at a home directory this user cannot read.
RUN useradd --uid 10001 --create-home --shell /bin/bash coai
USER 10001:10001
ENV HOME=/home/coai
WORKDIR /home/coai

# --------------------------------------------------------------------------
# The three vendor CLIs.
#
# LATEST, not pinned — the operator's decision of 2026-09-05, recorded with its cost. None
# of the three installers takes a version argument; each fetches whatever is current. The
# risk is real: the adapters' flags are verified against particular versions, and a vendor
# that changes a flag or an envelope between two rebuilds passes every fake-CLI test in CI
# and fails every real review. The mitigation that costs nothing is that the server REPORTS
# the versions it actually has — read from the binaries at startup and carried in
# /api/catalog — so "what is running there" has an answer rather than an assumption.
#
# Each install is followed by `--version` IN THE BUILD, so an image cannot be published
# carrying a CLI that does not start. That is the check the pin would otherwise have been.
# --------------------------------------------------------------------------
RUN curl -fsSL https://chatgpt.com/codex/install.sh | sh \
    && "$HOME/.local/bin/codex" --version
RUN curl -fsSL https://claude.ai/install.sh | bash \
    && "$HOME/.local/bin/claude" --version
RUN curl -fsSL https://antigravity.google/cli/install.sh | bash \
    && "$HOME/.local/bin/agy" --version

ENV PATH="/home/coai/.local/bin:${PATH}"

WORKDIR /app
USER root
COPY --from=build --chown=root:root /app/coai-server /app/appsettings.json ./
COPY --from=build --chown=10001:10001 /empty /data
COPY --from=build --chown=10001:10001 /empty /logs
USER 10001:10001

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Coai__DataDir=/data \
    Logging__Directory=/logs

EXPOSE 8080
VOLUME ["/data", "/logs"]

# The app answers /api/health by probing that the data directory is writable, so this check
# fails when the volume is gone or full — the failure modes that actually strand a 24/7
# deployment — and not merely when the process is alive.
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD ["/app/coai-server", "--healthcheck"]

ENTRYPOINT ["./coai-server"]
