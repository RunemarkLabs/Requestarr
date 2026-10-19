# syntax=docker/dockerfile:1
# Multi-stage build: web UI (Node), backend (.NET 10 SDK), then a slim ASP.NET runtime image.
# Build from the repository root:  docker build -f docker/Dockerfile -t requestarr:dev .

FROM node:20-bookworm-slim AS frontend
WORKDIR /src
COPY package.json yarn.lock .yarnrc tsconfig.json ./
RUN yarn install --frozen-lockfile --network-timeout 120000
COPY frontend/ frontend/
RUN yarn build --env production

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
ARG TARGETARCH
WORKDIR /src
COPY .editorconfig LICENSE ./
COPY src/ src/
# global.json pins the local SDK patch; the image's 10.0 SDK is used as-is.
RUN case "$TARGETARCH" in arm64) RID=linux-arm64 ;; *) RID=linux-x64 ;; esac \
 && for project in src/NzbDrone.Console/Requestarr.Console.csproj src/NzbDrone.Mono/Requestarr.Mono.csproj; do \
      dotnet publish "$project" -c Release -f net10.0 -r "$RID" --self-contained false -p:Platform=Posix -p:SolutionDir=/src/src/ -o /app/bin || exit 1; \
    done

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /config \
 && chown app:app /config
COPY --from=backend /app/bin /app/bin
COPY --from=frontend /src/_output/UI /app/bin/UI
COPY LICENSE /app/LICENSE

# Updates come from new images, never from inside the container.
ENV REQUESTARR__UPDATE__MECHANISM=Docker

USER app
VOLUME /config
EXPOSE 9797
HEALTHCHECK --interval=15s --timeout=5s --start-period=40s --retries=5 \
  CMD curl -fsS http://localhost:9797/ping || exit 1
ENTRYPOINT ["/app/bin/Requestarr", "-nobrowser", "-data=/config"]
