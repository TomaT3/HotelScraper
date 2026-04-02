import type { Hotel, HotelPrices, Status, FetchResult } from "./types";

const BASE = "/api";

async function fetchJson<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, init);
  if (!res.ok) {
    const text = await res.text();
    throw new Error(`API error ${res.status}: ${text}`);
  }
  return res.json();
}

export async function getHotels(): Promise<Hotel[]> {
  return fetchJson<Hotel[]>(`${BASE}/hotels`);
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

export async function getStatus(): Promise<Status> {
  return fetchJson<Status>(`${BASE}/status`);
}

export async function triggerFetch(maxDates?: number): Promise<FetchResult> {
  const qs = maxDates ? `?max_dates=${maxDates}` : "";
  return fetchJson<FetchResult>(`${BASE}/fetch${qs}`, { method: "POST" });
}
