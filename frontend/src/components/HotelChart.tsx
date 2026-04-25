import { useEffect, useState, useCallback } from "react";
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  ReferenceLine,
} from "recharts";
import type { HotelPrices } from "../api/types";

interface Props {
  data: HotelPrices[];
  selectedIds: Set<number>;
}

// Distinct colors for up to 20 hotels
const COLORS = [
  "#2563eb", "#dc2626", "#16a34a", "#ea580c", "#9333ea",
  "#0891b2", "#ca8a04", "#e11d48", "#4f46e5", "#059669",
  "#d97706", "#7c3aed", "#0284c7", "#be123c", "#65a30d",
  "#c026d3", "#0d9488", "#b91c1c", "#1d4ed8", "#a16207",
];

interface ChartDataPoint {
  date: string;
  [hotelName: string]: number | string;
}

function useWindowWidth() {
  const [width, setWidth] = useState(window.innerWidth);
  useEffect(() => {
    const handler = () => setWidth(window.innerWidth);
    window.addEventListener("resize", handler);
    return () => window.removeEventListener("resize", handler);
  }, []);
  return width;
}

export default function HotelChart({ data, selectedIds }: Props) {
  const [hoveredHotel, setHoveredHotel] = useState<string | null>(null);
  const [selectedDate, setSelectedDate] = useState<string | null>(null);
  const [legendOpen, setLegendOpen] = useState(true);
  const windowWidth = useWindowWidth();
  const isMobile = windowWidth < 768;

  const filtered = data.filter((h) => selectedIds.has(h.hotel_id));
  const isMany = filtered.length > 15;

  // Auto-collapse legend on mobile with many hotels
  useEffect(() => {
    if (isMobile && isMany) setLegendOpen(false);
    else if (!isMobile) setLegendOpen(true);
  }, [isMobile, isMany]);

  if (filtered.length === 0) {
    return (
      <div className="bg-white rounded-lg shadow p-8 text-center text-gray-400">
        <p className="text-lg">Keine Daten verfügbar</p>
        <p className="text-sm mt-1">
          Wähle Hotels aus und starte einen Abruf, um Preise zu sehen.
        </p>
      </div>
    );
  }

  // Merge all hotel prices into a single dataset keyed by date
  const dateMap = new Map<string, ChartDataPoint>();

  for (const hotel of filtered) {
    for (const p of hotel.prices) {
      if (!dateMap.has(p.date)) {
        dateMap.set(p.date, { date: p.date });
      }
      dateMap.get(p.date)![hotel.hotel_name] = p.price_eur;
    }
  }

  const chartData = Array.from(dateMap.values()).sort((a, b) =>
    a.date.localeCompare(b.date)
  );

  // Format date for display
  const formatDate = (dateStr: string) => {
    const d = new Date(dateStr + "T00:00:00");
    return d.toLocaleDateString("de-DE", { day: "2-digit", month: "2-digit" });
  };

  // Build a lookup: for a given date, get all hotel prices
  const getPricesForDate = useCallback(
    (dateStr: string) => {
      const point = dateMap.get(dateStr);
      if (!point) return [];
      return filtered
        .map((hotel) => ({
          hotel_name: hotel.hotel_name,
          stars: hotel.stars,
          price: point[hotel.hotel_name] as number | undefined,
          color: COLORS[filtered.indexOf(hotel) % COLORS.length],
        }))
        .filter((h) => h.price !== undefined)
        .sort((a, b) => (a.price ?? 0) - (b.price ?? 0));
    },
    [filtered, dateMap]
  );

  // Handle click on chart – select the clicked date
  const handleChartClick = useCallback(
    (data: any) => {
      if (data?.activeLabel) {
        setSelectedDate(data.activeLabel);
      }
    },
    []
  );

  // Custom tooltip – only shown on hover, does NOT interfere with selected date
  const CustomTooltip = ({ active, payload, label }: any) => {
    if (!active || !payload?.length) return null;

    const items = hoveredHotel
      ? payload.filter((e: any) => e.name === hoveredHotel)
      : payload.sort((a: any, b: any) => (a.value ?? 0) - (b.value ?? 0));

    return (
      <div className="bg-white border border-gray-200 rounded-lg shadow-lg p-3 text-sm max-w-xs">
        <p className="font-semibold mb-1">
          {new Date(label + "T00:00:00").toLocaleDateString("de-DE", {
            weekday: "short",
            day: "2-digit",
            month: "long",
            year: "numeric",
          })}
        </p>
        {items.length > 5 ? (
          <p className="text-gray-400 text-xs italic">
            {items.length} Hotels · Klicke für Details
          </p>
        ) : (
          items.map((entry: any, i: number) => (
            <div key={i} className="flex justify-between gap-4">
              <span style={{ color: entry.color }} className="truncate">
                {entry.name}
              </span>
              <span className="font-medium">{entry.value?.toFixed(0)} €</span>
            </div>
          ))
        )}
      </div>
    );
  };

  const chartHeight = isMobile ? 300 : isMany ? 450 : 500;

  // Build the selected-date panel data
  const selectedDatePrices = selectedDate ? getPricesForDate(selectedDate) : [];

  return (
    <div className="bg-white rounded-lg shadow p-2 sm:p-4">
      <h3 className="font-semibold text-gray-700 mb-2 sm:mb-4 text-sm sm:text-base">
        Preisverlauf — Doppelzimmer / Nacht
      </h3>

      <div className="flex flex-col lg:flex-row gap-4">
        {/* Chart */}
        <div className="flex-1 min-w-0">
          <ResponsiveContainer width="100%" height={chartHeight}>
            <LineChart
              data={chartData}
              onClick={handleChartClick}
              style={{ cursor: "pointer" }}
            >
              <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
              <XAxis
                dataKey="date"
                tickFormatter={formatDate}
                tick={{ fontSize: isMobile ? 9 : 11 }}
                interval="preserveStartEnd"
                minTickGap={isMobile ? 30 : 40}
              />
              <YAxis
                tick={{ fontSize: isMobile ? 9 : 11 }}
                tickFormatter={(v) => `${v} €`}
                width={isMobile ? 50 : 70}
              />
              <Tooltip content={<CustomTooltip />} />
              {selectedDate && (
                <ReferenceLine
                  x={selectedDate}
                  stroke="#666"
                  strokeDasharray="4 4"
                  strokeWidth={1.5}
                />
              )}
              {filtered.map((hotel, i) => (
                <Line
                  key={hotel.hotel_id}
                  type="monotone"
                  dataKey={hotel.hotel_name}
                  stroke={COLORS[i % COLORS.length]}
                  strokeWidth={
                    hoveredHotel === hotel.hotel_name ? 3 : isMany ? 1.5 : 2
                  }
                  strokeOpacity={
                    hoveredHotel
                      ? hoveredHotel === hotel.hotel_name
                        ? 1
                        : 0.1
                      : isMany
                      ? 0.5
                      : 1
                  }
                  dot={false}
                  connectNulls
                  activeDot={{ r: hoveredHotel === hotel.hotel_name ? 6 : 4 }}
                  isAnimationActive={false}
                />
              ))}
            </LineChart>
          </ResponsiveContainer>
        </div>

        {/* Selected date panel */}
        {selectedDate && selectedDatePrices.length > 0 && (
          <div className="lg:w-72 flex-shrink-0 border border-gray-200 rounded-lg bg-gray-50 p-3 max-h-[500px] flex flex-col">
            <div className="flex items-center justify-between mb-2">
              <h4 className="font-semibold text-sm text-gray-700">
                {new Date(selectedDate + "T00:00:00").toLocaleDateString("de-DE", {
                  weekday: "short",
                  day: "2-digit",
                  month: "long",
                  year: "numeric",
                })}
              </h4>
              <button
                onClick={() => setSelectedDate(null)}
                className="text-gray-400 hover:text-gray-600 text-lg leading-none"
                title="Auswahl aufheben"
              >
                &times;
              </button>
            </div>
            <div className="overflow-y-auto flex-1 space-y-1 text-sm">
              {selectedDatePrices.map((h, i) => (
                <div
                  key={i}
                  className="flex justify-between items-center gap-2 px-2 py-1 rounded hover:bg-white"
                >
                  <div className="flex items-center gap-1.5 min-w-0">
                    <span
                      className="inline-block w-2.5 h-0.5 flex-shrink-0 rounded"
                      style={{ backgroundColor: h.color }}
                    />
                    <span className="truncate text-gray-700">{h.hotel_name}</span>
                    {h.stars && (
                      <span className="text-yellow-500 text-xs flex-shrink-0">
                        {"★".repeat(h.stars)}
                      </span>
                    )}
                  </div>
                  <span className="font-medium text-gray-900 flex-shrink-0">
                    {h.price?.toFixed(0)} €
                  </span>
                </div>
              ))}
            </div>
            <p className="text-xs text-gray-400 mt-2 pt-2 border-t border-gray-200 text-center">
              {selectedDatePrices.length} Hotels
            </p>
          </div>
        )}
      </div>

      {/* Custom interactive legend */}
      <div className="mt-2 border-t pt-2">
        <button
          onClick={() => setLegendOpen(!legendOpen)}
          className="text-xs text-gray-500 hover:text-gray-700 mb-1 flex items-center gap-1"
        >
          <span
            className={`inline-block transition-transform ${
              legendOpen ? "rotate-90" : ""
            }`}
          >
            ▶
          </span>
          Legende ({filtered.length} Hotels)
        </button>
        {legendOpen && (
          <div className="max-h-32 sm:max-h-48 overflow-y-auto text-xs flex flex-wrap gap-x-3 gap-y-0.5">
            {filtered.map((hotel, i) => (
              <span
                key={hotel.hotel_id}
                className="flex items-center gap-1 cursor-pointer whitespace-nowrap py-0.5"
                onMouseEnter={() => setHoveredHotel(hotel.hotel_name)}
                onMouseLeave={() => setHoveredHotel(null)}
                style={{
                  opacity:
                    hoveredHotel && hoveredHotel !== hotel.hotel_name
                      ? 0.3
                      : 1,
                }}
              >
                <span
                  className="inline-block w-3 h-0.5 flex-shrink-0"
                  style={{ backgroundColor: COLORS[i % COLORS.length] }}
                />
                {hotel.hotel_name}
              </span>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
