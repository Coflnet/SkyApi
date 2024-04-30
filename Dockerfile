FROM mcr.microsoft.com/dotnet/sdk:8.0 as build
WORKDIR /build
RUN git clone -b net6 --depth=1 https://github.com/Coflnet/HypixelSkyblock.git dev \
    && git clone --depth=1 https://github.com/Coflnet/SkyBackendForFrontend.git \
    && git clone -b net6 --depth=1 https://github.com/Coflnet/SkyFilter.git
WORKDIR /build/sky
COPY SkyApi.csproj SkyApi.csproj
RUN dotnet restore
COPY . .
RUN rm SkyApi.sln && dotnet test
RUN dotnet publish -c release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8000

RUN useradd --uid $(shuf -i 2000-65000 -n 1) app-user
USER app-user

ENTRYPOINT ["dotnet", "SkyApi.dll", "--hostBuilder:reloadConfigOnChange=false"]
