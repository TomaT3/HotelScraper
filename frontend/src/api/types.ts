export interface Hotel {
  id: number;
  booking_id: string;
  name: string;
  address: string | null;
  stars: number | null;
  review_score: number | null;
  image_url: string | null;
  active: boolean;
  city: string;
}

export interface City {
  name: string;
  dest_label: string | null;
}

export interface PricePoint {
  date: string;
  price_eur: number;
}

export interface HotelPrices {
  hotel_id: number;
  hotel_name: string;
  stars: number | null;
  prices: PricePoint[];
}

export interface Status {
  city: string | null;
  total_hotels: number;
  active_hotels: number;
  total_prices: number;
  dates_covered: number;
  dates_total: number;
  coverage_pct: number;
  last_fetch: string | null;
  scheduler_running: boolean;
  next_run: string | null;
}

export interface FetchResult {
  dates_fetched: number;
  hotels_found: number;
  prices_saved: number;
  errors: string[];
}
