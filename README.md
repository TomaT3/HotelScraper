# Stuttgart Hotel Price Tracker 🏨

Web-App die täglich Hotelpreise für Doppelzimmer in Stuttgart über die Booking.com API (RapidAPI) abruft und als interaktives Liniendiagramm darstellt.

![Stack](https://img.shields.io/badge/FastAPI-Python-009688?style=flat-square) ![Stack](https://img.shields.io/badge/React-TypeScript-61DAFB?style=flat-square) ![Stack](https://img.shields.io/badge/SQLite-Database-003B57?style=flat-square) ![Stack](https://img.shields.io/badge/Docker-Deployment-2496ED?style=flat-square)

## Features

- **Automatischer Preisabruf** — Scheduler holt täglich neue Hotelpreise (konfigurierbar)
- **365-Tage-Abdeckung** — Preise für das komplette nächste Jahr
- **Interaktives Dashboard** — Liniendiagramm mit Recharts, Filter nach Hotels und Sternen
- **Leichtgewicht** — Single Docker Container, SQLite, kein Redis/Celery nötig
- **Synology-ready** — Docker Compose für NAS-Deployment optimiert

## Voraussetzungen

1. **RapidAPI Key** für die [Booking.com API (Tipsters)](https://rapidapi.com/tipsters/api/booking-com)
   - Kostenlosen Account erstellen auf [rapidapi.com](https://rapidapi.com)
   - „Booking.com" API subscriben (Free Tier reicht)
   - API Key aus dem Dashboard kopieren

2. **Docker** und **Docker Compose** (auf dem NAS oder lokal)

## Schnellstart

### 1. Repository klonen

```bash
git clone <repo-url>
cd HotelScraper
```

### 2. Environment konfigurieren

```bash
cp .env.example .env
# .env editieren und RAPIDAPI_KEY eintragen
```

### 3. Starten

```bash
docker compose up -d --build
```

Die App ist erreichbar unter: **http://localhost:8080**

### 4. Ersten Abruf starten

Auf der Web-Oberfläche den Button **„Jetzt abrufen"** klicken. Der erste Abruf holt Preise für ~15 Tage (konfigurierbar). Nach ~25 Tagen ist das gesamte Jahr abgedeckt.

## Konfiguration

Alle Einstellungen werden über Umgebungsvariablen gesetzt (`.env` oder `docker-compose.yml`):

| Variable | Default | Beschreibung |
|---|---|---|
| `RAPIDAPI_KEY` | — | **Pflicht.** Dein RapidAPI Key |
| `DATES_PER_RUN` | `15` | Wie viele Tage pro Scheduler-Lauf abgerufen werden |
| `FETCH_HOUR` | `3` | Uhrzeit (Stunde, 0-23) für den täglichen Abruf |
| `SEARCH_CITIES` | `Stuttgart` | Städte für die Hotelsuche (komma-getrennt, z.B. `Stuttgart,München,Berlin`) |
| `DATABASE_URL` | `sqlite+aiosqlite:///./data/hotel_prices.db` | Datenbank-Pfad |

## Deployment auf Synology NAS

### Über SSH

```bash
# SSH auf NAS
ssh admin@<NAS-IP>

# Ordner erstellen
mkdir -p /volume1/docker/hotel-scraper
cd /volume1/docker/hotel-scraper

# Dateien hochladen (per SCP/SFTP) oder Git klonen
git clone <repo-url> .

# .env anlegen
cp .env.example .env
vi .env  # RAPIDAPI_KEY eintragen

# Starten
docker compose up -d --build
```

### Über Container Manager (DSM GUI)

1. Dateien per File Station nach `/docker/hotel-scraper/` hochladen
2. Container Manager → Projekt → Erstellen
3. Pfad: `/docker/hotel-scraper/`
4. Docker Compose Datei wird automatisch erkannt
5. In den Umgebungsvariablen `RAPIDAPI_KEY` setzen
6. Starten

Die App ist dann erreichbar unter: `http://<NAS-IP>:8080`

## API-Endpunkte

| Methode | Pfad | Beschreibung |
|---|---|---|
| `GET` | `/api/hotels` | Liste aller Hotels |
| `PATCH` | `/api/hotels/{id}` | Hotel aktiv/inaktiv setzen |
| `GET` | `/api/prices?hotel_ids=1,2&from=2026-04-01&to=2026-12-31` | Preise abfragen |
| `GET` | `/api/status` | Scheduler-Status & Statistiken |
| `POST` | `/api/fetch?max_dates=5` | Manueller Preisabruf |

## Entwicklung

### Backend (lokal)

```bash
cd backend
python -m venv .venv
.venv/Scripts/activate  # Windows
pip install -e ".[dev]"
uvicorn app.main:app --reload
```

### Frontend (lokal)

```bash
cd frontend
npm install
npm run dev
```

Frontend Dev-Server läuft auf `http://localhost:5173` mit Proxy zu `http://localhost:8000`.

## Architektur

```
Browser → FastAPI (Port 8000)
            ├── /api/*          → REST API (Hotels, Preise, Status)
            ├── /assets/*       → Static Files (React Build)
            └── /*              → SPA Fallback (index.html)
                    ↓
              SQLite (./data/hotel_prices.db)
                    ↓
              APScheduler → RapidAPI Booking.com
```

## Datenquelle

Preise werden über die **[Booking.com API auf RapidAPI](https://rapidapi.com/tipsters/api/booking-com)** (von Tipsters) abgerufen. Der Free Tier erlaubt ~500 Requests/Monat, was bei 15 Dates/Tag für eine monatliche Rotation ausreicht.

**Hinweis:** Dies ist eine inoffizielle API. Preise und Verfügbarkeit können von den tatsächlichen Booking.com-Preisen abweichen.

## Lizenz

MIT
