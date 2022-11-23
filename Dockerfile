FROM mcr.microsoft.com/dotnet/sdk:7.0 as build
WORKDIR /build
RUN git clone --depth=1 -b feature/opentelemetry https://github.com/Coflnet/HypixelSkyblock.git dev
RUN git clone --depth=1 https://github.com/Coflnet/SkyBackendForFrontend.git
RUN git clone --depth=1 https://github.com/Coflnet/SkyFilter.git
WORKDIR /build/sky
COPY SkyApi.csproj SkyApi.csproj
RUN dotnet restore
COPY . .
RUN dotnet test
RUN dotnet publish -c release

FROM mcr.microsoft.com/dotnet/aspnet:7.0
WORKDIR /app

COPY --from=build /build/sky/bin/release/net7.0/publish/ .

ENV ASPNETCORE_URLS=http://+:8000

RUN useradd --uid $(shuf -i 2000-65000 -n 1) app
USER app

ENTRYPOINT ["dotnet", "SkyApi.dll", "--hostBuilder:reloadConfigOnChange=false"]
