import { useState } from "react";
import type { Hotel } from "../api/types";

interface Props {
  hotels: Hotel[];
  selectedIds: Set<number>;
  onToggle: (id: number) => void;
  onSelectAll: () => void;
  onDeselectAll: () => void;
  starFilter: number | null;
  onStarFilterChange: (stars: number | null) => void;
  favorites: Set<number>;
  onToggleFavorite: (id: number) => void;
}

const STAR_OPTIONS = [null, 0, 1, 2, 3, 4, 5] as const;

export default function HotelFilter({
  hotels,
  selectedIds,
  onToggle,
  onSelectAll,
  onDeselectAll,
  starFilter,
  onStarFilterChange,
  favorites,
  onToggleFavorite,
}: Props) {
  const [searchQuery, setSearchQuery] = useState("");

  const filteredHotels = hotels.filter((h) => {
    if (starFilter !== null && h.stars !== starFilter && !(starFilter === 0 && h.stars === null)) return false;
    if (searchQuery && !h.name.toLowerCase().includes(searchQuery.toLowerCase())) return false;
    return true;
  });

  return (
    <div className="bg-surface-card border border-hairline rounded-none p-4">
      <div className="flex items-center justify-between mb-3">
        <h3 className="font-display uppercase tracking-display-md text-ink">Hotels</h3>
        <div className="flex gap-2">
          <button
            onClick={onSelectAll}
            className="text-xs text-link hover:underline"
          >
            Alle
          </button>
          <span className="text-muted">|</span>
          <button
            onClick={onDeselectAll}
            className="text-xs text-link hover:underline"
          >
            Keine
          </button>
        </div>
      </div>

      {/* Star filter */}
      <div className="flex gap-2 mb-3">
        {STAR_OPTIONS.map((s) => (
          <button
            key={s ?? "all"}
            onClick={() => onStarFilterChange(s)}
            className={`px-2 py-1 text-xs rounded-pill font-mono uppercase tracking-label-sm transition-colors ${
              starFilter === s
                ? "border border-ink text-ink"
                : "bg-surface-soft text-muted hover:bg-surface-elevated"
            }`}
          >
            {s === null ? "Alle" : `${s} ★`}
          </button>
        ))}
      </div>

      {/* Search field */}
      <input
        type="text"
        placeholder="Hotel suchen..."
        value={searchQuery}
        onChange={(e) => setSearchQuery(e.target.value)}
        className="w-full py-2 bg-transparent border-0 border-b border-hairline-strong rounded-none text-sm text-ink placeholder:text-muted-soft focus:outline-none focus:border-ink mb-3"
      />

      {/* Hotel list */}

      <div className="max-h-80 overflow-y-auto space-y-1">
        {filteredHotels.length === 0 ? (
          <p className="text-sm text-muted italic">Keine Hotels gefunden</p>
        ) : (
          filteredHotels.map((hotel) => (
            <label
              key={hotel.id}
              className="flex items-center gap-2 py-1 px-2 rounded-none hover:bg-surface-elevated cursor-pointer text-sm"
            >
              <input
                type="checkbox"
                checked={selectedIds.has(hotel.id)}
                onChange={() => onToggle(hotel.id)}
                className="rounded text-link"
              />
              {/* Favorite star */}
              <button
                onClick={(e) => {
                  e.preventDefault();
                  onToggleFavorite(hotel.id);
                }}
                className={`text-base flex-shrink-0 transition-colors ${
                  favorites.has(hotel.id)
                    ? "text-warning hover:opacity-75"
                    : "text-muted-soft hover:text-muted"
                }`}
                title={favorites.has(hotel.id) ? "Favorit entfernen" : "Als Favorit markieren"}
              >
                {favorites.has(hotel.id) ? "⭐" : "☆"}
              </button>
              <span className="truncate flex-1">{hotel.name}</span>
              {hotel.stars != null && (
                <span className="text-warning text-xs flex-shrink-0">
                  {hotel.stars > 0 ? "★".repeat(hotel.stars) : "—"}
                </span>
              )}
              {hotel.review_score && (
                <span className="text-muted text-xs flex-shrink-0">
                  {hotel.review_score.toFixed(1)}
                </span>
              )}
            </label>
          ))
        )}
      </div>
      <div className="mt-2 text-xs text-muted-soft">
        {selectedIds.size} von {filteredHotels.length} ausgewählt
        {favorites.size > 0 && (
          <span className="ml-2">· {favorites.size} Favoriten</span>
        )}
      </div>
    </div>
  );
}
