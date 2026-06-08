FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# Build the React frontend (always runs on host platform — output is platform-independent JS)
FROM --platform=$BUILDPLATFORM node:20-alpine AS frontend
WORKDIR /app
COPY src/ControlPlane.Client/package*.json ./
RUN npm ci
COPY src/ControlPlane.Client/ ./
# Override outDir since Docker context differs from local dev
RUN npm run build -- --outDir /app/dist

# Build the .NET backend
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Copy project files first so Docker can cache the restore layer
COPY src/Shared/Shared.csproj src/Shared/
COPY src/Sdk/Sdk.csproj src/Sdk/
COPY src/ControlPlane/ControlPlane.csproj src/ControlPlane/
RUN if [ "$TARGETARCH" = "amd64" ]; then ARCH=x64; else ARCH=$TARGETARCH; fi && \
    dotnet restore src/ControlPlane/ControlPlane.csproj -r linux-${ARCH}

# Copy everything else and build
COPY src/Shared/ src/Shared/
COPY src/Sdk/ src/Sdk/
COPY src/ControlPlane/ src/ControlPlane/

# Copy the built frontend into wwwroot before publishing
COPY --from=frontend /app/dist src/ControlPlane/wwwroot/

RUN if [ "$TARGETARCH" = "amd64" ]; then ARCH=x64; else ARCH=$TARGETARCH; fi && \
    dotnet publish src/ControlPlane/ControlPlane.csproj \
        -c Release \
        -r linux-${ARCH} \
        --no-restore \
        -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ControlPlane.dll"]
