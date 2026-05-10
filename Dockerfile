# ---- Stage 1: Build React frontend ----
FROM node:20-alpine AS frontend-build
WORKDIR /build
COPY frontend/package.json frontend/package-lock.json* ./
RUN npm install
COPY frontend/ ./
ENV BUILD_OUTDIR=dist
RUN npm run build

# ---- Stage 2: Build .NET backend ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
ARG VERSION=unknown
WORKDIR /src

# Restore dependencies
COPY src/HotelScraper.Api/HotelScraper.Api.csproj ./src/HotelScraper.Api/
RUN dotnet restore src/HotelScraper.Api/HotelScraper.Api.csproj

# Copy source and publish
COPY src/ ./src/
RUN dotnet publish src/HotelScraper.Api/HotelScraper.Api.csproj -c Release -o /app /p:Version=${VERSION}

# ---- Stage 3: Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0

ARG VERSION=unknown
ENV APP_VERSION=${VERSION}

WORKDIR /app

# Copy published .NET app
COPY --from=backend-build /app ./

# Copy built frontend from stage 1 → wwwroot
COPY --from=frontend-build /build/dist ./wwwroot/

# Create data directory
RUN mkdir -p /app/data

# Non-root user
RUN useradd --create-home appuser && chown -R appuser:appuser /app
USER appuser

EXPOSE 8000

ENV ASPNETCORE_URLS=http://0.0.0.0:8000
ENTRYPOINT ["dotnet", "HotelScraper.Api.dll"]
