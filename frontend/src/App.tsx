import { useCallback, useEffect, useState, type FormEvent } from "react";
import StatusBar from "./components/StatusBar";
import HotelFilter from "./components/HotelFilter";
import DateRangePicker from "./components/DateRangePicker";
import HotelChart from "./components/HotelChart";
import CitySelector from "./components/CitySelector";
import AdminView from "./components/AdminView";
import { ChevronDown } from "./components/Icons";
import { useAuth } from "./auth/AuthContext";
import {
  addToWatchlist,
  getCities,
  getConfig,
  getHotels,
  getPrices,
  getStatus,
  getVersion,
  getWatchlist,
  removeFromWatchlist,
  triggerFetch,
} from "./api/client";
import type { City, Hotel, HotelPrices, Status, FetchResult } from "./api/types";

function todayStr(): string {
  return new Date().toISOString().slice(0, 10);
}

function datesPerRunEndStr(datesPerRun: number): string {
  const d = new Date();
  d.setDate(d.getDate() + datesPerRun - 1);
  return d.toISOString().slice(0, 10);
}

function LoginForm() {
  const { login } = useAuth();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      await login(email, password);
    } catch (err) {
      setError(
        err instanceof Error && err.message.includes("401")
          ? "E-Mail oder Passwort ist falsch."
          : "Anmeldung fehlgeschlagen. Bitte später erneut versuchen."
      );
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="min-h-screen flex items-center justify-center px-4">
      <div className="w-full max-w-sm bg-surface-card border border-hairline rounded-none p-8 space-y-5">
        <div>
          <h1 className="font-display text-2xl tracking-display-sm text-ink">Hotel Price Tracker</h1>
          <p className="text-sm text-muted mt-1">
            Bitte melden Sie sich mit Ihrem Konto an.
          </p>
        </div>
        <form onSubmit={handleSubmit} className="space-y-3">
          <div>
            <label htmlFor="login-email" className="block font-mono text-xs uppercase tracking-label-sm text-muted">
              E-Mail
            </label>
            <input
              id="login-email"
              type="email"
              required
              autoComplete="username"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className="mt-1 w-full py-2 bg-transparent border-0 border-b border-hairline-strong rounded-none text-sm text-ink placeholder:text-muted-soft focus:outline-none focus:border-ink"
              placeholder="name@hotel.de"
            />
          </div>
          <div>
            <label htmlFor="login-password" className="block font-mono text-xs uppercase tracking-label-sm text-muted">
              Passwort
            </label>
            <input
              id="login-password"
              type="password"
              required
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="mt-1 w-full py-2 bg-transparent border-0 border-b border-hairline-strong rounded-none text-sm text-ink placeholder:text-muted-soft focus:outline-none focus:border-ink"
            />
          </div>
          {error && (
            <div className="text-sm text-danger border border-hairline-strong rounded-none px-3 py-2">
              {error}
            </div>
          )}
          <button
            type="submit"
            disabled={submitting}
            className="w-full py-3 border border-ink text-ink rounded-pill font-mono text-sm uppercase tracking-label hover:bg-ink hover:text-canvas transition-colors disabled:opacity-40"
          >
            {submitting ? "Anmelden..." : "Anmelden"}
          </button>
        </form>
      </div>
    </div>
  );
}

export default function App() {
  const { user, loading, logout } = useAuth();
  const [cities, setCities] = useState<City[]>([]);
  const [selectedCity, setSelectedCity] = useState<string>("");
  const [hotels, setHotels] = useState<Hotel[]>([]);
  const [prices, setPrices] = useState<HotelPrices[]>([]);
  const [status, setStatus] = useState<Status | null>(null);
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set());
  const [starFilter, setStarFilter] = useState<number | null>(null);
  const [datesPerRun, setDatesPerRun] = useState<number>(15);
  const [dateFrom, setDateFrom] = useState(todayStr());
  const [dateTo, setDateTo] = useState(datesPerRunEndStr(15));
  const [loadingData, setLoadingData] = useState(true);
  const [fetching, setFetching] = useState(false);
  const [fetchResult, setFetchResult] = useState<FetchResult | null>(null);
  const [showFilters, setShowFilters] = useState(false);
  const [favorites, setFavorites] = useState<Map<string, Set<number>>>(new Map());
  const [version, setVersion] = useState<string | null>(null);
  const [roomType, setRoomType] = useState<"single" | "double">("double");
  const [view, setView] = useState<"dashboard" | "admin">("dashboard");

  const isAdmin = user?.role === "admin";

  // Load version and config on mount (public endpoints)
  useEffect(() => {
    async function load() {
      try {
        const [v, cfg] = await Promise.all([getVersion(), getConfig()]);
        setVersion(v.version);
        setDatesPerRun(cfg.dates_per_run);
        setDateTo(datesPerRunEndStr(cfg.dates_per_run));
      } catch {
        // Version/config endpoints may not be available (e.g. dev mode)
      }
    }
    load();
  }, []);

  // Load watchlist from server once logged in (server is source of truth)
  useEffect(() => {
    if (!user) return;
    let cancelled = false;
    (async () => {
      try {
        const ids = await getWatchlist();
        if (cancelled) return;
        const map = new Map<string, Set<number>>();
        // The watchlist is tenant-wide; key the favorites per city so the UI
        // can look them up per selected city.
        (user.cities ?? []).forEach((c) => map.set(c, new Set(ids)));
        setFavorites(map);
      } catch (e) {
        console.error("Failed to load watchlist:", e);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [user]);

  // Load cities once logged in
  useEffect(() => {
    if (!user) return;
    let cancelled = false;
    async function loadCities() {
      try {
        const c = await getCities();
        if (cancelled) return;
        setCities(c);
        if (c.length > 0) {
          setSelectedCity(c[0].name);
        }
      } catch (e) {
        console.error("Failed to load cities:", e);
      }
    }
    loadCities();
    return () => {
      cancelled = true;
    };
  }, [user]);

  // Load hotels + status when city changes
  useEffect(() => {
    if (!selectedCity) return;
    let cancelled = false;

    async function load() {
      setLoadingData(true);
      try {
        const [h, s] = await Promise.all([
          getHotels(selectedCity),
          getStatus(selectedCity),
        ]);
        if (cancelled) return;
        setHotels(h);
        setStatus(s);
        // Auto-select favorites for this city, or fall back to all active hotels
        const cityFavorites = favorites.get(selectedCity);
        if (cityFavorites && cityFavorites.size > 0) {
          // Only select favorites that still exist in the hotel list
          const validFavorites = new Set(
            Array.from(cityFavorites).filter((id) => h.some((hotel) => hotel.id === id))
          );
          setSelectedIds(validFavorites);
        } else {
          const activeIds = new Set(h.filter((x) => x.active).map((x) => x.id));
          setSelectedIds(activeIds);
        }
        setStarFilter(null);
        setFetchResult(null);
      } catch (e) {
        console.error("Failed to load data for city:", e);
      } finally {
        if (!cancelled) setLoadingData(false);
      }
    }
    load();
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedCity]);

  // Fetch prices when selection or dates change
  useEffect(() => {
    if (selectedIds.size === 0) {
      setPrices([]);
      return;
    }
    let cancelled = false;
    async function loadPrices() {
      try {
        const p = await getPrices({
          hotelIds: Array.from(selectedIds),
          from: dateFrom,
          to: dateTo,
          roomType,
        });
        if (!cancelled) setPrices(p);
      } catch (e) {
        console.error("Failed to load prices:", e);
      }
    }
    loadPrices();
    return () => {
      cancelled = true;
    };
  }, [selectedIds, dateFrom, dateTo, roomType]);

  const handleCityChange = useCallback((city: string) => {
    setSelectedCity(city);
    setPrices([]);
    setSelectedIds(new Set());
  }, []);

  const handleToggle = useCallback((id: number) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }, []);

  const handleSelectAll = useCallback(() => {
    const filtered = starFilter !== null
      ? hotels.filter((h) => h.stars === starFilter || (starFilter === 0 && h.stars === null))
      : hotels;
    setSelectedIds(new Set(filtered.map((h) => h.id)));
  }, [hotels, starFilter]);

  const handleDeselectAll = useCallback(() => {
    setSelectedIds(new Set());
  }, []);

  const handleToggleFavorite = useCallback(
    async (id: number) => {
      if (!user?.cities?.length || !selectedCity) return;
      const city = selectedCity;
      const wasFavorite = favorites.get(city)?.has(id) ?? false;

      // Optimistic update
      setFavorites((prev) => {
        const next = new Map(prev);
        const set = new Set(next.get(city) ?? []);
        if (wasFavorite) set.delete(id);
        else set.add(id);
        if (set.size > 0) next.set(city, set);
        else next.delete(city);
        return next;
      });

      try {
        if (wasFavorite) await removeFromWatchlist(id);
        else await addToWatchlist(id);
      } catch (e) {
        console.error("Watchlist update failed:", e);
        // Revert on error
        setFavorites((prev) => {
          const next = new Map(prev);
          const set = new Set(next.get(city) ?? []);
          if (wasFavorite) set.add(id);
          else set.delete(id);
          if (set.size > 0) next.set(city, set);
          else next.delete(city);
          return next;
        });
      }
    },
    [favorites, user]
  );

  const handleFetch = useCallback(async () => {
    setFetching(true);
    setFetchResult(null);
    try {
      const result = await triggerFetch(selectedCity || undefined);
      setFetchResult(result);
      // Reload data for current city
      if (selectedCity) {
        const [h, s] = await Promise.all([
          getHotels(selectedCity),
          getStatus(selectedCity),
        ]);
        setHotels(h);
        setStatus(s);
        const activeIds = new Set(h.filter((x) => x.active).map((x) => x.id));
        setSelectedIds(activeIds);
      }
    } catch (e) {
      console.error("Fetch failed:", e);
    } finally {
      setFetching(false);
    }
  }, [selectedCity]);

  const handleDateChange = useCallback((from: string, to: string) => {
    setDateFrom(from);
    setDateTo(to);
  }, []);

  const handleLogout = useCallback(async () => {
    await logout();
    setView("dashboard");
    setCities([]);
    setSelectedCity("");
    setHotels([]);
    setPrices([]);
    setStatus(null);
    setSelectedIds(new Set());
    setFavorites(new Map());
    setFetchResult(null);
  }, [logout]);

  if (loading) {
    return (
      <div className="min-h-screen flex items-center justify-center text-gray-400 text-sm">
        Wird geladen…
      </div>
    );
  }

  if (!user) {
    return <LoginForm />;
  }

  return (
    <div className="max-w-7xl mx-auto px-3 sm:px-4 py-4 sm:py-6 space-y-3 sm:space-y-4">
      {/* Header */}
      <div className="flex items-center gap-3 flex-wrap">
        <h1 className="font-display uppercase tracking-display-sm text-ink text-xl sm:text-2xl">
          {selectedCity || "Hotel"} · Hotel Price Tracker
        </h1>
        <CitySelector
          cities={cities}
          selectedCity={selectedCity}
          onCityChange={handleCityChange}
        />
        <div className="ml-auto flex items-center gap-3">
          <div className="text-right">
            <div className="text-sm text-body-strong">{user.email}</div>
            <div className="text-xs text-muted">
              {user.role === "admin" ? "Administrator" : user.tenant_name ?? user.cities?.join(", ") ?? "Benutzer"}
            </div>
          </div>
          {isAdmin && (
            <button
              onClick={() => setView(view === "admin" ? "dashboard" : "admin")}
              className={`px-4 py-1.5 text-xs font-mono uppercase tracking-label-sm rounded-pill border transition-colors ${
                view === "admin"
                  ? "border-ink text-ink"
                  : "border-hairline-strong text-muted hover:text-body"
              }`}
            >
              {view === "admin" ? "Zur Übersicht" : "Verwaltung"}
            </button>
          )}
          <button
            onClick={handleLogout}
            className="px-4 py-1.5 text-xs font-mono uppercase tracking-label-sm rounded-pill border border-hairline-strong text-muted hover:text-body transition-colors"
          >
            Abmelden
          </button>
        </div>
      </div>

      {view === "admin" && isAdmin ? (
        <AdminView currentUserId={user.id} />
      ) : (
        <>
      {/* Status bar */}
      <StatusBar
        status={status}
        loading={loadingData}
        onFetch={isAdmin ? handleFetch : undefined}
        fetching={fetching}
      />

      {/* Fetch result notification */}
      {fetchResult && (
        <div
          className={`rounded-lg p-3 text-sm ${
            fetchResult.errors.length > 0
              ? "bg-yellow-50 text-yellow-800 border border-yellow-200"
              : "bg-green-50 text-green-800 border border-green-200"
          }`}
        >
          <span className="font-medium">Abruf abgeschlossen:</span>{" "}
          {fetchResult.dates_fetched} Tage, {fetchResult.prices_saved} Preise
          gespeichert, {fetchResult.hotels_found} Hotels gefunden.
          {fetchResult.errors.length > 0 && (
            <details className="mt-1">
              <summary className="cursor-pointer text-yellow-600">
                {fetchResult.errors.length} Fehler anzeigen
              </summary>
              <ul className="mt-1 list-disc list-inside">
                {fetchResult.errors.map((e, i) => (
                  <li key={i}>{e}</li>
                ))}
              </ul>
            </details>
          )}
        </div>
      )}

      {/* Filters + Chart */}
      <div className="grid grid-cols-1 lg:grid-cols-4 gap-4">
        {/* Mobile filter toggle */}
        <button
          onClick={() => setShowFilters(!showFilters)}
          className="lg:hidden flex items-center justify-between bg-white rounded-lg shadow p-3 text-sm font-medium text-gray-700 order-2"
        >
          <span>Filter & Einstellungen</span>
          <ChevronDown
            className={`w-4 h-4 transition-transform ${showFilters ? "rotate-180" : ""}`}
          />
        </button>

        {/* Left sidebar: filters – always visible on desktop, collapsible on mobile */}
        <div
          className={`space-y-4 order-3 lg:order-1 ${
            !showFilters ? "hidden lg:block" : ""
          }`}
        >
          <DateRangePicker
            from={dateFrom}
            to={dateTo}
            onChange={handleDateChange}
          />
          <HotelFilter
            hotels={hotels}
            selectedIds={selectedIds}
            onToggle={handleToggle}
            onSelectAll={handleSelectAll}
            onDeselectAll={handleDeselectAll}
            starFilter={starFilter}
            onStarFilterChange={setStarFilter}
            favorites={favorites.get(selectedCity) ?? new Set()}
            onToggleFavorite={handleToggleFavorite}
          />
        </div>

        {/* Main chart area – shown first on mobile */}
        <div className="lg:col-span-3 order-1 lg:order-2">
          <HotelChart data={prices} selectedIds={selectedIds} roomType={roomType} onRoomTypeChange={setRoomType} />
        </div>
      </div>

        </>
      )}

      {/* Footer */}
      <div className="text-center text-xs text-muted-soft pt-4">
        Daten via Booking.com (RapidAPI) · Preise für {roomType === "single" ? "Einzelzimmer" : "Doppelzimmer"} / 1 Nacht
        {version && (
          <span className="ml-2 font-mono text-muted">· {version}</span>
        )}
      </div>
    </div>
  );
}
