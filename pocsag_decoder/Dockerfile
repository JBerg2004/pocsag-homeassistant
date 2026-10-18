ARG BUILD_FROM=ghcr.io/home-assistant/amd64-base-debian:bookworm

# 1. Programm übersetzen (eine einzelne Datei, .NET wird mitgeliefert)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/ ./
RUN dotnet publish PocsagDecoder.Service/PocsagDecoder.Service.csproj -c Release -r linux-x64 --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o /out

# 2. Laufzeit mit rtl_fm und multimon-ng aus Debian
FROM ${BUILD_FROM}
RUN apt-get update \
    && apt-get install -y --no-install-recommends rtl-sdr multimon-ng ca-certificates libstdc++6 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /out/pocsag-decoder /usr/local/bin/pocsag-decoder
ENTRYPOINT []
CMD ["/usr/local/bin/pocsag-decoder", "/data/options.json"]
