# ---- Stage 1: Build React frontend ----
FROM node:20-alpine AS frontend-build
WORKDIR /build
COPY frontend/package.json frontend/package-lock.json* ./
RUN npm install
COPY frontend/ ./
ENV BUILD_OUTDIR=dist
RUN npm run build

# ---- Stage 2: Python backend ----
FROM python:3.12-slim

ARG VERSION=unknown
ENV APP_VERSION=${VERSION}

WORKDIR /app

# Copy backend source and install
COPY backend/pyproject.toml ./
COPY backend/app/ ./app/
RUN pip install --no-cache-dir .

# Copy built frontend from stage 1
COPY --from=frontend-build /build/dist ./static/

# Create data directory
RUN mkdir -p /app/data

# Non-root user
RUN useradd --create-home appuser && chown -R appuser:appuser /app
USER appuser

EXPOSE 8000

CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
