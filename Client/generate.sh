VERSION=0.4.2

docker run --rm -v "${PWD}:/local" --network host -u $(id -u ${USER}):$(id -g ${USER})  openapitools/openapi-generator-cli generate \
-i http://localhost:5005/swagger/v1/swagger.json \
-g csharp-netcore \
-o /local/out --additional-properties=packageName=Coflnet.Sky.Api.Client,packageVersion=$VERSION,licenseId=MIT

cd out
sed -i 's/GIT_USER_ID/Coflnet/g' src/Coflnet.Sky.Api.Client/Coflnet.Sky.Api.Client.csproj
sed -i 's/GIT_REPO_ID/SkyApi/g' src/Coflnet.Sky.Api.Client/Coflnet.Sky.Api.Client.csproj
sed -i 's/>OpenAPI/>Coflnet/g' src/Coflnet.Sky.Api.Client/Coflnet.Sky.Api.Client.csproj

dotnet pack
cp src/Coflnet.Sky.Api.Client/bin/Debug/Coflnet.Sky.Api.Client.*.nupkg ..
