FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first so Docker can cache the restore layer
COPY src/Shared/Shared.csproj src/Shared/
COPY src/ControlPlane/ControlPlane.csproj src/ControlPlane/
RUN dotnet restore src/ControlPlane/ControlPlane.csproj

# Copy everything else and build
COPY src/Shared/ src/Shared/
COPY src/ControlPlane/ src/ControlPlane/
RUN dotnet publish src/ControlPlane/ControlPlane.csproj -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ControlPlane.dll"]
