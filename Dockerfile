FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build
ARG CACHEBUST=1
RUN git clone --depth=1 https://github.com/Coflnet/HypixelSkyblock.git dev \
    && git clone --depth=1 https://github.com/Coflnet/SkyBackendForFrontend.git \
    && git clone --depth=1 https://github.com/Coflnet/SkyFilter.git
WORKDIR /build/sky
RUN git clone --depth=1 https://github.com/Ekwav/NotEnoughUpdates-REPO.git NEU-REPO \
    && rm -rf NEU-REPO/.git NEU-REPO/items
COPY SkyApi.csproj SkyApi.csproj
RUN dotnet restore
COPY . .
RUN rm SkyApi.sln && dotnet test
RUN dotnet publish -c release -o /app /p:UseAppHost=false /p:PublishReadyToRun=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app

COPY --from=build --chown=$APP_UID:$APP_UID /app .
COPY --from=build --chown=$APP_UID:$APP_UID /build/sky/NEU-REPO NEU-REPO

ENV ASPNETCORE_URLS=http://+:8000 \
    DOTNET_EnableDiagnostics=0 \
    COMPlus_EnableDiagnostics=0 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    HOME=/tmp \
    TMPDIR=/tmp

USER $APP_UID

ENTRYPOINT ["dotnet", "SkyApi.dll", "--hostBuilder:reloadConfigOnChange=false"]
