FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Shared/Shared.csproj src/Shared/
COPY src/Sdk/Sdk.csproj src/Sdk/
COPY src/RuntimeAgent/RuntimeAgent.csproj src/RuntimeAgent/
RUN dotnet restore src/RuntimeAgent/RuntimeAgent.csproj

COPY src/Shared/ src/Shared/
COPY src/Sdk/ src/Sdk/
COPY src/RuntimeAgent/ src/RuntimeAgent/

RUN dotnet publish src/RuntimeAgent/RuntimeAgent.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "RuntimeAgent.dll"]
