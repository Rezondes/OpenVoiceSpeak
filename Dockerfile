# syntax=docker/dockerfile:1
# Server image for linux/amd64 and linux/arm64:
#   docker buildx build --platform linux/amd64,linux/arm64 -t openvoicespeak/server:dev .
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY nuget.config Directory.Build.props ./
COPY src/OVS.Shared/OVS.Shared.csproj src/OVS.Shared/
COPY src/OVS.Server/OVS.Server.csproj src/OVS.Server/
RUN dotnet restore src/OVS.Server/OVS.Server.csproj -a $TARGETARCH -p:SelfContained=true
COPY src/OVS.Shared/ src/OVS.Shared/
COPY src/OVS.Server/ src/OVS.Server/
RUN dotnet publish src/OVS.Server/OVS.Server.csproj -c Release -a $TARGETARCH --no-restore \
    --self-contained -p:PublishSingleFile=true -o /app

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
# Named volumes inherit this ownership on first mount, so the non-root user can write /data.
RUN mkdir /data && chown $APP_UID /data
ENV OVS_DATA_DIR=/data
VOLUME /data
EXPOSE 7000/tcp 7000/udp
USER $APP_UID
WORKDIR /app
COPY --from=build /app/OVS.Server .
ENTRYPOINT ["./OVS.Server"]
