# One Dockerfile for all six services. They differ only by which project is published,
# so a per-service copy would be six files drifting out of sync for no benefit.
#
#   docker build --build-arg SERVICE=OrderService -t ecommerce/order .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG SERVICE
WORKDIR /src

# Copy only project files first so `dotnet restore` is cached: editing a .cs file does
# not invalidate the restore layer, which is most of the build time.
COPY global.json Directory.Build.props ./
COPY src/BuildingBlocks/*/*.csproj ./tmp/bb/
RUN for f in ./tmp/bb/*.csproj; do \
      n=$(basename "$f" .csproj); mkdir -p "src/BuildingBlocks/$n"; mv "$f" "src/BuildingBlocks/$n/"; \
    done

COPY src/Services/*/*.csproj ./tmp/svc/
RUN for f in ./tmp/svc/*.csproj; do \
      n=$(basename "$f" .csproj); mkdir -p "src/Services/$n"; mv "$f" "src/Services/$n/"; \
    done
RUN rm -rf ./tmp

RUN dotnet restore "src/Services/${SERVICE}/${SERVICE}.csproj"

COPY src/ ./src/
RUN dotnet publish "src/Services/${SERVICE}/${SERVICE}.csproj" \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG SERVICE
WORKDIR /app

# The .NET runtime images ship neither curl nor wget, so a compose healthcheck has
# nothing to call the health endpoint with. curl is ~1MB and makes `docker compose ps`
# tell the truth about whether a service is actually serving.
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Run as non-root. The official .NET images already ship an unprivileged "app" user
# (UID 1654), so there is nothing to create — creating one would also fail, since these
# images carry no adduser.
USER app

COPY --from=build --chown=app:app /app/publish .

# Baked in so the generic entrypoint knows which assembly to start.
ENV SERVICE_DLL=${SERVICE}.dll
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["sh", "-c", "exec dotnet $SERVICE_DLL"]
