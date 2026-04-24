ARG RUNTIME_PLATFORM=linux/amd64

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY Tmf921.IntentManagement.Api/Tmf921.IntentManagement.Api.fsproj Tmf921.IntentManagement.Api/
RUN --mount=type=secret,id=ca_bundle,target=/tmp/ca_bundle.pem \
    if [ -s /tmp/ca_bundle.pem ]; then \
        cp /tmp/ca_bundle.pem /usr/local/share/ca-certificates/local-build-ca.crt \
        && update-ca-certificates; \
    fi \
    && dotnet restore Tmf921.IntentManagement.Api/Tmf921.IntentManagement.Api.fsproj

COPY Tmf921.IntentManagement.Api/ Tmf921.IntentManagement.Api/
RUN dotnet publish Tmf921.IntentManagement.Api/Tmf921.IntentManagement.Api.fsproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM --platform=$RUNTIME_PLATFORM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

ARG FSTAR_VERSION=2025.12.15
ARG FSTAR_SHA256=d175809ba0fbdacad871fa7ba049d966d1c12aee0c52b7c0c35d30a5a649ffd8

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    TMF921_REPO_ROOT=/app \
    PATH=/opt/fstar/bin:$PATH

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl tar \
    && rm -rf /var/lib/apt/lists/*

RUN --mount=type=secret,id=ca_bundle,target=/tmp/ca_bundle.pem \
    if [ -s /tmp/ca_bundle.pem ]; then \
        curl -fsSL --cacert /tmp/ca_bundle.pem \
            "https://github.com/FStarLang/FStar/releases/download/v${FSTAR_VERSION}/fstar-v${FSTAR_VERSION}-Linux-x86_64.tar.gz" \
            -o /tmp/fstar.tar.gz; \
    else \
        curl -fsSL \
            "https://github.com/FStarLang/FStar/releases/download/v${FSTAR_VERSION}/fstar-v${FSTAR_VERSION}-Linux-x86_64.tar.gz" \
            -o /tmp/fstar.tar.gz; \
    fi \
    && echo "${FSTAR_SHA256}  /tmp/fstar.tar.gz" | sha256sum -c - \
    && mkdir -p /opt/fstar \
    && tar -xzf /tmp/fstar.tar.gz -C /opt/fstar --strip-components=2 \
    && rm /tmp/fstar.tar.gz \
    && fstar.exe --version

COPY --from=build /app/publish ./
COPY --from=build /src/Tmf921.IntentManagement.Api/FStar ./Tmf921.IntentManagement.Api/FStar
COPY --from=build /src/Tmf921.IntentManagement.Api/FStarDemo ./Tmf921.IntentManagement.Api/FStarDemo
COPY --from=build /src/Tmf921.IntentManagement.Api/Schemas ./Tmf921.IntentManagement.Api/Schemas
COPY --from=build /src/Tmf921.IntentManagement.Api/DemoSchemas ./Tmf921.IntentManagement.Api/DemoSchemas
COPY --from=build /src/Tmf921.IntentManagement.Api/DemoFixtures ./Tmf921.IntentManagement.Api/DemoFixtures

RUN fstar.exe \
    --include /app/Tmf921.IntentManagement.Api/FStar \
    /app/Tmf921.IntentManagement.Api/FStar/TmForumTr292CommonCore.fst

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Tmf921.IntentManagement.Api.dll"]
