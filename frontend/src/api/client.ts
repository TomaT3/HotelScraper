import type { AuthUser, City, ConfigResponse, Hotel, HotelPrices, Status, FetchResult, VersionInfo } from "./types";

const BASE = "/api";

/**
 * Fired whenever an authenticated API call returns 401 (session expired).
 * The AuthContext listens for this and returns the UI to the login screen.
 */
export const AUTH_UNAUTHORIZED_EVENT = "auth:unauthorized";

function notifyUnauthorized() {
  window.dispatchEvent(new CustomEvent(AUTH_UNAUTHORIZED_EVENT));
}

async function fetchJson<T>(
  url: string,
  init?: RequestInit,
  opts?: { silent401?: boolean }
): Promise<T> {
  const res = await fetch(url, init);
  if (!res.ok) {
    if (res.status === 401 && !opts?.silent401) {
      notifyUnauthorized();
    }
    const text = await res.text();
    throw new Error(`API error ${res.status}: ${text}`);
  }
  return res.json();
}

// ── Auth ────────────────────────────────────────────────────────────────

export async function login(email: string, password: string): Promise<AuthUser> {
  return fetchJson<AuthUser>(
    `${BASE}/auth/login`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password }),
    },
    { silent401: true }
  );
}

export async function logout(): Promise<void> {
  await fetch(`${BASE}/auth/logout`, { method: "POST" });
}

export async function getMe(): Promise<AuthUser> {
  return fetchJson<AuthUser>(`${BASE}/auth/me`);
}

// ── Watchlist ───────────────────────────────────────────────────────────

export async function getWatchlist(): Promise<number[]> {
  return fetchJson<number[]>(`${BASE}/watchlist`);
}

export async function addToWatchlist(hotelId: number): Promise<{ hotel_id: number; added: boolean }> {
  return fetchJson<{ hotel_id: number; added: boolean }>(`${BASE}/watchlist/${hotelId}`, {
    method: "PUT",
  });
}

export async function removeFromWatchlist(hotelId: number): Promise<{ hotel_id: number; removed: boolean }> {
  return fetchJson<{ hotel_id: number; removed: boolean }>(`${BASE}/watchlist/${hotelId}`, {
    method: "DELETE",
  });
}

// ── Data ────────────────────────────────────────────────────────────────

export async function getCities(): Promise<City[]> {
  return fetchJson<City[]>(`${BASE}/cities`);
}

export async function getHotels(city: string): Promise<Hotel[]> {
  return fetchJson<Hotel[]>(`${BASE}/hotels?city=${encodeURIComponent(city)}`);
}

export async function updateHotel(
  id: number,
  data: { active?: boolean }
): Promise<Hotel> {
  return fetchJson<Hotel>(`${BASE}/hotels/${id}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
}

export async function getPrices(params?: {
  hotelIds?: number[];
  from?: string;
  to?: string;
  roomType?: string;
}): Promise<HotelPrices[]> {
  const searchParams = new URLSearchParams();
  if (params?.hotelIds?.length) {
    searchParams.set("hotel_ids", params.hotelIds.join(","));
  }
  if (params?.from) searchParams.set("from", params.from);
  if (params?.to) searchParams.set("to", params.to);
  if (params?.roomType) searchParams.set("room_type", params.roomType);

  const qs = searchParams.toString();
  return fetchJson<HotelPrices[]>(`${BASE}/prices${qs ? `?${qs}` : ""}`);
}

export async function getStatus(city?: string): Promise<Status> {
  const qs = city ? `?city=${encodeURIComponent(city)}` : "";
  return fetchJson<Status>(`${BASE}/status${qs}`);
}

export async function getVersion(): Promise<VersionInfo> {
  return fetchJson<VersionInfo>(`${BASE}/version`);
}

export async function getConfig(): Promise<ConfigResponse> {
  return fetchJson<ConfigResponse>(`${BASE}/config`);
}

export async function triggerFetch(city?: string, maxDates?: number): Promise<FetchResult> {
  const params = new URLSearchParams();
  if (city) params.set("city", city);
  if (maxDates) params.set("max_dates", String(maxDates));
  const qs = params.toString();
  return fetchJson<FetchResult>(`${BASE}/fetch${qs ? `?${qs}` : ""}`, { method: "POST" });
}
