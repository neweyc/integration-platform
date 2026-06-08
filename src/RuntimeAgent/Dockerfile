FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

COPY src/Shared/Shared.csproj src/Shared/
COPY src/Sdk/Sdk.csproj src/Sdk/
COPY src/RuntimeAgent/RuntimeAgent.csproj src/RuntimeAgent/
RUN if [ "$TARGETARCH" = "amd64" ]; then ARCH=x64; else ARCH=$TARGETARCH; fi && \
    dotnet restore src/RuntimeAgent/RuntimeAgent.csproj -r linux-${ARCH}

COPY src/Shared/ src/Shared/
COPY src/Sdk/ src/Sdk/
COPY src/RuntimeAgent/ src/RuntimeAgent/

RUN if [ "$TARGETARCH" = "amd64" ]; then ARCH=x64; else ARCH=$TARGETARCH; fi && \
    dotnet publish src/RuntimeAgent/RuntimeAgent.csproj \
        -c Release \
        -r linux-${ARCH} \
        --no-restore \
        -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "RuntimeAgent.dll"]
