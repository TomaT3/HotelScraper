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

const STAR_OPTIONS = [null, 3, 4, 5] as const;

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
    if (starFilter !== null && (h.stars === null || h.stars < starFilter)) return false;
    if (searchQuery && !h.name.toLowerCase().includes(searchQuery.toLowerCase())) return false;
    return true;
  });

  return (
    <div className="bg-white rounded-lg shadow p-4">
      <div className="flex items-center justify-between mb-3">
        <h3 className="font-semibold text-gray-700">Hotels</h3>
        <div className="flex gap-2">
          <button
            onClick={onSelectAll}
            className="text-xs text-blue-600 hover:underline"
          >
            Alle
          </button>
          <span className="text-gray-300">|</span>
          <button
            onClick={onDeselectAll}
            className="text-xs text-blue-600 hover:underline"
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
            className={`px-2 py-1 text-xs rounded ${
              starFilter === s
                ? "bg-blue-600 text-white"
                : "bg-gray-100 text-gray-600 hover:bg-gray-200"
            }`}
          >
            {s === null ? "Alle" : `${s}+ ★`}
          </button>
        ))}
      </div>

      {/* Search field */}
      <input
        type="text"
        placeholder="Hotel suchen..."
        value={searchQuery}
        onChange={(e) => setSearchQuery(e.target.value)}
        className="w-full px-3 py-2 text-sm border border-gray-300 rounded-lg mb-3 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
      />

      {/* Hotel list */}

      <div className="max-h-80 overflow-y-auto space-y-1">
        {filteredHotels.length === 0 ? (
          <p className="text-sm text-gray-400 italic">Keine Hotels gefunden</p>
        ) : (
          filteredHotels.map((hotel) => (
            <label
              key={hotel.id}
              className="flex items-center gap-2 py-1 px-2 rounded hover:bg-gray-50 cursor-pointer text-sm"
            >
              <input
                type="checkbox"
                checked={selectedIds.has(hotel.id)}
                onChange={() => onToggle(hotel.id)}
                className="rounded text-blue-600"
              />
              {/* Favorite star */}
              <button
                onClick={(e) => {
                  e.preventDefault();
                  onToggleFavorite(hotel.id);
                }}
                className={`text-base flex-shrink-0 transition-colors ${
                  favorites.has(hotel.id)
                    ? "text-yellow-500 hover:text-yellow-600"
                    : "text-gray-300 hover:text-gray-400"
                }`}
                title={favorites.has(hotel.id) ? "Favorit entfernen" : "Als Favorit markieren"}
              >
                {favorites.has(hotel.id) ? "⭐" : "☆"}
              </button>
              <span className="truncate flex-1">{hotel.name}</span>
              {hotel.stars && (
                <span className="text-yellow-500 text-xs flex-shrink-0">
                  {"★".repeat(hotel.stars)}
                </span>
              )}
              {hotel.review_score && (
                <span className="text-gray-400 text-xs flex-shrink-0">
                  {hotel.review_score.toFixed(1)}
                </span>
              )}
            </label>
          ))
        )}
      </div>
      <div className="mt-2 text-xs text-gray-400">
        {selectedIds.size} von {filteredHotels.length} ausgewählt
        {favorites.size > 0 && (
          <span className="ml-2">· {favorites.size} Favoriten</span>
        )}
      </div>
    </div>
  );
}
