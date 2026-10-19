# syntax=docker/dockerfile:1
# Server image for linux/amd64 and linux/arm64:
#   docker buildx build --platform linux/amd64,linux/arm64 -t openvoicespeak/server:dev .
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
# Package 42/44: the release workflow passes the build data so the image carries the same version as the exe.
# MSBuild reads them as properties (Directory.Build.props); left empty, a Release build stamps its own time.
ARG OVS_BUILD_TIME=""
ARG GITHUB_SHA=""
ARG GITHUB_ACTIONS=""
WORKDIR /src
COPY nuget.config Directory.Build.props .editorconfig ./
COPY src/OVS.Shared/OVS.Shared.csproj src/OVS.Shared/
COPY src/OVS.Server/OVS.Server.csproj src/OVS.Server/
RUN dotnet restore src/OVS.Server/OVS.Server.csproj -a $TARGETARCH -p:SelfContained=true
COPY src/OVS.Shared/ src/OVS.Shared/
COPY src/OVS.Server/ src/OVS.Server/
RUN dotnet publish src/OVS.Server/OVS.Server.csproj -c Release -a $TARGETARCH --no-restore \
    --self-contained -p:PublishSingleFile=true -o /app

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
# Package 193 (A206): the protocol version, so the web manager knows before an update whether clients must update too.
# The release workflow passes the server's --protocol-version; a local build leaves it empty.
ARG OVS_PROTOCOL=""
LABEL org.openvoicespeak.protocol=$OVS_PROTOCOL
LABEL org.opencontainers.image.source="https://github.com/Rezondes/OpenVoiceSpeak"
LABEL org.opencontainers.image.description="OpenVoiceSpeak server: self-hosted voice chat"
# Named volumes inherit this ownership on first mount, so the non-root user can write /data.
RUN mkdir /data && chown $APP_UID /data
ENV OVS_DATA_DIR=/data
# Package 145: telemetry is opt-in. Set OTEL_EXPORTER_OTLP_ENDPOINT (and, if needed, OTEL_EXPORTER_OTLP_PROTOCOL,
# OTEL_EXPORTER_OTLP_HEADERS, OTEL_SERVICE_NAME, OTEL_RESOURCE_ATTRIBUTES) at run time to export over OTLP; unset, nothing is sent.
VOLUME /data
EXPOSE 7000/tcp 7000/udp
USER $APP_UID
WORKDIR /app
COPY --from=build /app/OVS.Server .
ENTRYPOINT ["./OVS.Server"]
