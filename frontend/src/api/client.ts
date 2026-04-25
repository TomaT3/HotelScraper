import type { City, ConfigResponse, Hotel, HotelPrices, Status, FetchResult, VersionInfo } from "./types";

const BASE = "/api";

async function fetchJson<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, init);
  if (!res.ok) {
    const text = await res.text();
    throw new Error(`API error ${res.status}: ${text}`);
  }
  return res.json();
}

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
}): Promise<HotelPrices[]> {
  const searchParams = new URLSearchParams();
  if (params?.hotelIds?.length) {
    searchParams.set("hotel_ids", params.hotelIds.join(","));
  }
  if (params?.from) searchParams.set("from", params.from);
  if (params?.to) searchParams.set("to", params.to);

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
