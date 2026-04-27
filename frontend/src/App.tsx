import { useCallback, useEffect, useState } from "react";
import StatusBar from "./components/StatusBar";
import HotelFilter from "./components/HotelFilter";
import DateRangePicker from "./components/DateRangePicker";
import HotelChart from "./components/HotelChart";
import CitySelector from "./components/CitySelector";
import { ChevronDown } from "./components/Icons";
import { getCities, getHotels, getPrices, getStatus, getVersion, getConfig } from "./api/client";
import type { City, Hotel, HotelPrices, Status } from "./api/types";

const FAVORITES_KEY = "hotelFavorites";

function loadFavorites(): Map<string, Set<number>> {
  try {
    const raw = localStorage.getItem(FAVORITES_KEY);
    if (!raw) return new Map();
    const parsed = JSON.parse(raw) as Record<string, number[]>;
    const map = new Map<string, Set<number>>();
    for (const [city, ids] of Object.entries(parsed)) {
      map.set(city, new Set(ids));
    }
    return map;
  } catch {
    return new Map();
  }
}

function saveFavorites(favorites: Map<string, Set<number>>) {
  const obj: Record<string, number[]> = {};
  for (const [city, ids] of favorites) {
    obj[city] = Array.from(ids);
  }
  localStorage.setItem(FAVORITES_KEY, JSON.stringify(obj));
}

function todayStr(): string {
  return new Date().toISOString().slice(0, 10);
}

function datesPerRunEndStr(datesPerRun: number): string {
  const d = new Date();
  d.setDate(d.getDate() + datesPerRun - 1);
  return d.toISOString().slice(0, 10);
}

export default function App() {
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
  const [loading, setLoading] = useState(true);
  const [showFilters, setShowFilters] = useState(false);
  const [favorites, setFavorites] = useState<Map<string, Set<number>>>(() => loadFavorites());
  const [version, setVersion] = useState<string | null>(null);

  // Persist favorites to localStorage whenever they change
  useEffect(() => {
    saveFavorites(favorites);
  }, [favorites]);

  // Load version and config on mount
  useEffect(() => {
    async function load() {
      try {
        const [v, cfg] = await Promise.all([
          getVersion(),
          getConfig(),
        ]);
        setVersion(v.version);
        setDatesPerRun(cfg.dates_per_run);
        setDateTo(datesPerRunEndStr(cfg.dates_per_run));
      } catch {
        // Version/config endpoints may not be available (e.g. dev mode)
      }
    }
    load();
  }, []);

  // Load cities on mount
  useEffect(() => {
    async function loadCities() {
      try {
        const c = await getCities();
        setCities(c);
        if (c.length > 0) {
          setSelectedCity(c[0].name);
        }
      } catch (e) {
        console.error("Failed to load cities:", e);
      }
    }
    loadCities();
  }, []);

  // Load hotels + status when city changes
  useEffect(() => {
    if (!selectedCity) return;
    let cancelled = false;

    async function load() {
      setLoading(true);
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
      } catch (e) {
        console.error("Failed to load data for city:", e);
      } finally {
        if (!cancelled) setLoading(false);
      }
    }
    load();
    return () => {
      cancelled = true;
    };
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
  }, [selectedIds, dateFrom, dateTo]);

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
    const filtered = starFilter
      ? hotels.filter((h) => h.stars !== null && h.stars >= starFilter)
      : hotels;
    setSelectedIds(new Set(filtered.map((h) => h.id)));
  }, [hotels, starFilter]);

  const handleDeselectAll = useCallback(() => {
    setSelectedIds(new Set());
  }, []);

  const handleToggleFavorite = useCallback((id: number) => {
    setFavorites((prev) => {
      const next = new Map(prev);
      const existing = next.get(selectedCity);
      const cityFavorites = existing ? new Set(existing) : new Set<number>();
      if (cityFavorites.has(id)) {
        cityFavorites.delete(id);
      } else {
        cityFavorites.add(id);
      }
      if (cityFavorites.size > 0) {
        next.set(selectedCity, cityFavorites);
      } else {
        next.delete(selectedCity);
      }
      return next;
    });
  }, [selectedCity]);

  const handleDateChange = useCallback((from: string, to: string) => {
    setDateFrom(from);
    setDateTo(to);
  }, []);

  return (
    <div className="max-w-7xl mx-auto px-3 sm:px-4 py-4 sm:py-6 space-y-3 sm:space-y-4">
      {/* Header */}
      <div className="flex items-center gap-3 flex-wrap">
        <h1 className="text-lg sm:text-2xl font-bold text-gray-800">
          🏨 {selectedCity || "Hotel"} Hotel Price Tracker
        </h1>
        <CitySelector
          cities={cities}
          selectedCity={selectedCity}
          onCityChange={handleCityChange}
        />
      </div>

      {/* Status bar */}
      <StatusBar
        status={status}
        loading={loading}
      />

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
          <HotelChart data={prices} selectedIds={selectedIds} />
        </div>
      </div>

      {/* Footer */}
      <div className="text-center text-xs text-gray-400 pt-4">
        Daten via Booking.com (RapidAPI) · Preise für Doppelzimmer / 1 Nacht / 2 Erwachsene
        {version && (
          <span className="ml-2 font-mono text-gray-300">· {version}</span>
        )}
      </div>
    </div>
  );
}
