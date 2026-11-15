# Stage 1: Build the Vite + TypeScript client bundle
FROM node:22-alpine AS frontend-build
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
ARG APP_VERSION=v1.0.0-local
ENV VITE_APP_VERSION=$APP_VERSION
RUN npm run build

# Stage 2: Publish the ASP.NET Core 10.0 server
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src/backend
COPY backend/Warzone.csproj ./
RUN dotnet restore Warzone.csproj
COPY backend/ ./
RUN dotnet publish Warzone.csproj -c Release -o /app/backend --no-restore

# Stage 3: Minimal production runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app/backend
COPY --from=backend-build /app/backend ./
COPY --from=frontend-build /src/frontend/dist /app/frontend/dist

ARG APP_VERSION=v1.0.0-local
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
ENV APP_VERSION=$APP_VERSION

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=15s --retries=3 \
  CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Warzone.dll"]
