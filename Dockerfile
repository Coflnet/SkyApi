FROM mcr.microsoft.com/dotnet/sdk:10.0 as build
WORKDIR /build
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
RUN dotnet publish -c release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app .
COPY --from=build /build/sky/NEU-REPO NEU-REPO

ENV ASPNETCORE_URLS=http://+:8000

RUN useradd --uid $(shuf -i 2000-65000 -n 1) app-user
USER app-user

ENTRYPOINT ["dotnet", "SkyApi.dll", "--hostBuilder:reloadConfigOnChange=false"]
