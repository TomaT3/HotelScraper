# Hotel Price Tracker 🏨

Zentrale SaaS-Instanz: Web-App, die täglich Hotelpreise über die Booking.com API (RapidAPI) abruft und als interaktives Liniendiagramm darstellt. **Mehrere Hotels (Mandanten) loggen sich mit E-Mail + Passwort ein** und sehen jeweils die Konkurrenz-Preise *ihrer Stadt* (Stuttgart & Tübingen).

![GitHub Release](https://img.shields.io/github/v/release/TomaT3/HotelScraper?style=flat-square&logo=github) ![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/TomaT3/HotelScraper/release-and-publish.yml?style=flat-square&logo=githubactions&label=release%20build) ![Docker Pulls](https://img.shields.io/docker/pulls/tomat3/hotel-price-scraper?style=flat-square&logo=docker) ![Stack](https://img.shields.io/badge/ASP.NET-C%23-512BD4?style=flat-square) ![Stack](https://img.shields.io/badge/React-TypeScript-61DAFB?style=flat-square) ![Stack](https://img.shields.io/badge/SQLite-Database-003B57?style=flat-square) ![Stack](https://img.shields.io/badge/Docker-Deployment-2496ED?style=flat-square)

## Features

- **Automatischer Preisabruf** — Scheduler holt täglich neue Hotelpreise (konfigurierbar)
- **365-Tage-Abdeckung** — Preise für das komplette nächste Jahr
- **Interaktives Dashboard** — Liniendiagramm mit Recharts, Filter nach Hotels und Sternen
- **Multi-Tenancy** — jeder Mandant (Hotel) sieht nur die Preise seiner Stadt
- **Login & Rollen** — `admin` (globale Verwaltung, Scraping) und `user` (eigene Stadt, Watchlist)
- **Server-seitige Watchlist** — Favoriten werden pro Mandant in der DB gespeichert (statt localStorage)
- **Leichtgewicht** — Single Docker Container, SQLite, kein Redis/Celery nötig
- **Synology-ready** — Docker Compose für NAS-Deployment optimiert

> **Multi-Tenancy-Konzept:** Die Konkurrenz-Daten sind **pro Stadt geteilt** (nicht pro Kunde isoliert) — das teure RapidAPI-Scraping läuft nur einmal pro Stadt. Ein Mandant = eine Hotelgruppe mit **1..N Städten** (z.B. ein Hotel in Stuttgart und eines in Tübingen); der Kunde sieht die Preise aller seiner Städte.

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
# ADMIN_EMAIL / ADMIN_PASSWORD für das initiale Admin-Konto setzen
```

### 3. Starten

```bash
docker compose up -d --build
```

Die App ist erreichbar unter: **http://localhost:8080**

Beim **ersten Start** wird aus `ADMIN_EMAIL` / `ADMIN_PASSWORD` ein Admin-Konto angelegt (nur wenn noch kein Admin existiert). Danach mit diesen Zugangsdaten einloggen — ein Passwort-Wechsel ist danach über die Admin-API möglich.

### 4. Mandanten & Benutzer anlegen (Admin)

1. Mit dem Admin-Konto einloggen.
2. Über die Admin-API einen Mandanten anlegen (Name + eine oder mehrere Städte):
   ```bash
   curl -X POST http://localhost:8080/api/admin/tenants \
     -H "Content-Type: application/json" \
     -H "Cookie: <Session-Cookie>" \
     -d '{"name": "Hotel Beispiel GmbH", "cities": ["Stuttgart"]}'
   ```
   Ein Mandant mit mehreren Städten (z.B. Stuttgart **und** Tübingen):
   ```bash
   curl -X POST http://localhost:8080/api/admin/tenants \
     -H "Content-Type: application/json" \
     -H "Cookie: <Session-Cookie>" \
     -d '{"name": "Hotel Gruppe GmbH", "cities": ["Stuttgart", "Tübingen"]}'
   ```
   Alle Städte müssen in `Scraper:SearchCities` (kommagetrennt) enthalten sein.
3. Benutzer für den Mandanten anlegen:
   ```bash
   curl -X POST http://localhost:8080/api/admin/users \
     -H "Content-Type: application/json" \
     -H "Cookie: <Session-Cookie>" \
     -d '{"email": "rezeption@beispiel.de", "password": "<Initialpasswort>", "tenant_id": 1, "role": "user"}'
   ```
   Alternativ lassen sich Mandanten/Benutzer direkt in der SQLite-DB pflegen (Tabellen `tenants`, `tenant_cities`, `users`).
4. Der Benutzer loggt sich ein und sieht ausschließlich die Preise seiner Städte. Die Favoriten (Watchlist) sind an sein Konto gebunden.

### 5. Ersten Abruf starten

Auf der Web-Oberfläche (als **Admin**) den Button **„Jetzt abrufen"** klicken. Der erste Abruf holt Preise für ~15 Tage (konfigurierbar). Nach ~25 Tagen ist das gesamte Jahr abgedeckt. Normale Benutzer können kein Scraping auslösen (Quota-Schutz).

## Konfiguration

Alle Einstellungen werden über Umgebungsvariablen gesetzt (`.env` oder `docker-compose.yml`):

| Variable | Default | Beschreibung |
|---|---|---|
| `RAPIDAPI_KEY` | — | **Pflicht.** Dein RapidAPI Key |
| `DATES_PER_RUN` | `15` | Wie viele Tage pro Scheduler-Lauf abgerufen werden |
| `FETCH_HOUR` | `3` | Uhrzeit (Stunde, 0-23) für den täglichen Abruf |
| `SEARCH_CITIES` | `Stuttgart` | Städte für die Hotelsuche (komma-getrennt, z.B. `Stuttgart,München,Berlin`) |
| `DATABASE_URL` | `data/hotel_prices.db` | Datenbank-Pfad |
| `ADMIN_EMAIL` | — | E-Mail des initialen Admin-Kontos (Seed beim ersten Start) |
| `ADMIN_PASSWORD` | — | Passwort des initialen Admin-Kontos (Seed beim ersten Start) |

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

Alle Endpunkte (außer `version`/`config`) erfordern eine **Login-Session** (Cookie). Rollen: `admin` = globale Verwaltung + Scraping, `user` = eigene Stadt.

| Methode | Pfad | Beschreibung | Zugriff |
|---|---|---|---|
| `POST` | `/api/auth/login` | Login (`{email, password}`) → Session-Cookie | öffentlich |
| `POST` | `/api/auth/logout` | Session beenden | öffentlich |
| `GET` | `/api/auth/me` | Aktueller Benutzer + Mandant | eingeloggt |
| `GET` | `/api/cities` | Städte (User: nur eigene Stadt) | eingeloggt |
| `GET` | `/api/hotels?city=…` | Hotels (User: immer eigene Stadt, `?city=` wird ignoriert) | eingeloggt |
| `PATCH` | `/api/hotels/{id}` | Hotel aktiv/inaktiv setzen | **admin** |
| `GET` | `/api/prices?hotel_ids=1,2&from=…&to=…` | Preise abfragen (gefiltert auf eigene Stadt) | eingeloggt |
| `GET` | `/api/status` | Scheduler-Status & Statistiken (User: eigene Stadt) | eingeloggt |
| `POST` | `/api/fetch?max_dates=5` | Manueller Preisabruf | **admin** |
| `GET` | `/api/watchlist` | Favoriten (Hotel-IDs) des eigenen Mandanten | eingeloggt |
| `PUT` | `/api/watchlist/{hotelId}` | Hotel zur Watchlist hinzufügen | eingeloggt |
| `DELETE` | `/api/watchlist/{hotelId}` | Hotel aus der Watchlist entfernen | eingeloggt |
| `GET` | `/api/admin/tenants` | Mandanten auflisten | **admin** |
| `POST` | `/api/admin/tenants` | Mandant anlegen (`{name, city}`) | **admin** |
| `PATCH` | `/api/admin/tenants/{id}` | Mandant bearbeiten | **admin** |
| `GET` | `/api/admin/users` | Benutzer auflisten | **admin** |
| `POST` | `/api/admin/users` | Benutzer anlegen (`{email, password, tenant_id, role}`) | **admin** |
| `POST` | `/api/admin/users/{id}/reset-password` | Passwort zurücksetzen (`{password}`) | **admin** |
| `GET` | `/api/version`, `/api/config` | Meta-Informationen | öffentlich |

## Migrationen & Bestands-Datenbank (Baseline)

Die App nutzt **EF Core Migrationen** (`Migrate()` beim Start), nicht mehr `EnsureCreated`. Eine frische Installation legt das komplette Schema selbst an.

**Beim Upgrade einer bestehenden Kunden-DB** (ohne `__EFMigrationsHistory`-Tabelle) einmalig wie folgt vorgehen:

1. **Sicherung** erstellen:
   ```bash
   cp data/hotel_prices.db data/hotel_prices.db.bak
   ```
2. Prüfen, dass das bestehende Schema exakt dem `InitialCreate`-Modell entspricht (Tabellen `hotels`, `prices`, `settings`).
3. **Baseline** eintragen (danach führt `Migrate()` nur noch die neue Migration `AddTenantsUsersWatchlist` aus):
   ```sql
   INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
   VALUES ('20260901212251_InitialCreate', '10.0.0');
   ```
4. App starten — sie migriert die Alt-DB auf das neue Schema (Tabellen `tenants`, `users`, `watchlist_items`), ohne Bestandsdaten anzufassen.

⚠️ Ohne Schritt 3 versucht `Migrate()` die Tabellen `hotels`/`prices`/`settings` **neu anzulegen** → Fehler auf der Alt-DB.

## Entwicklung

### Backend (lokal)

```bash
cd backend/src/HotelScraper.Api
dotnet run
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
Browser → ASP.NET Core (Port 8000)
            ├── /api/auth/*     → Login/Logout/Session (Cookie)
            ├── /api/*          → REST API (Hotels, Preise, Status, Watchlist, Admin)
            ├── /assets/*       → Static Files (React Build)
            └── /*              → SPA Fallback (index.html)
                    ↓
              SQLite (./data/hotel_prices.db, EF Core Migrationen)
                    ↓
              Quartz.NET → RapidAPI Booking.com
```

## Datenquelle

Preise werden über die **[Booking.com API auf RapidAPI](https://rapidapi.com/tipsters/api/booking-com)** (von Tipsters) abgerufen. Der Free Tier erlaubt ~500 Requests/Monat, was bei 15 Dates/Tag für eine monatliche Rotation ausreicht.

**Hinweis:** Dies ist eine inoffizielle API. Preise und Verfügbarkeit können von den tatsächlichen Booking.com-Preisen abweichen.

## Lizenz

MIT
