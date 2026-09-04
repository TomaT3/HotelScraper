import { useEffect, useState, useCallback, useMemo } from "react";
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  ReferenceLine,
  ReferenceArea,
} from "recharts";
import type { HotelPrices } from "../api/types";
import { CHART_COLORS } from "../theme/chartColors";

interface Props {
  data: HotelPrices[];
  selectedIds: Set<number>;
  roomType: "single" | "double";
  onRoomTypeChange: (type: "single" | "double") => void;
}

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

export default function HotelChart({ data, selectedIds, roomType, onRoomTypeChange }: Props) {
  const [hoveredHotel, setHoveredHotel] = useState<string | null>(null);
  const [selectedDate, setSelectedDate] = useState<string | null>(null);
  const [legendOpen, setLegendOpen] = useState(true);
  const windowWidth = useWindowWidth();
  const isMobile = windowWidth < 768;

  const filtered = data.filter((h) => selectedIds.has(h.hotel_id));
  const isMany = filtered.length > 15;

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

  // Compute consecutive weekend spans (Sat–Sun) for background highlighting
  const weekendSpans = useMemo(() => {
    const spans: { start: string; end: string }[] = [];
    let spanStart: string | null = null;

    for (let i = 0; i < chartData.length; i++) {
      const dateStr = chartData[i].date;
      const d = new Date(dateStr + "T00:00:00");
      const day = d.getDay();
      const isWeekend = day === 0 || day === 6;

      if (isWeekend && spanStart === null) {
        spanStart = dateStr;
      }

      if (!isWeekend && spanStart !== null) {
        spans.push({ start: spanStart, end: chartData[i - 1].date });
        spanStart = null;
      }
    }

    if (spanStart !== null) {
      spans.push({ start: spanStart, end: chartData[chartData.length - 1].date });
    }

    return spans;
  }, [chartData]);

  // Compute data gaps per hotel for dashed bridge lines
  const hotelGaps = useMemo(() => {
    const gaps: {
      hotelId: number;
      color: string;
      startDate: string;
      startPrice: number;
      endDate: string;
      endPrice: number;
    }[] = [];

    for (const hotel of filtered) {
      const name = hotel.hotel_name;
      const color = CHART_COLORS[filtered.indexOf(hotel) % CHART_COLORS.length];
      let prevIdx: number | null = null;

      for (let i = 0; i < chartData.length; i++) {
        const price = chartData[i][name];
        if (price !== undefined) {
          if (prevIdx !== null && prevIdx < i - 1) {
            // Gap: at least one null date between prevIdx and i
            gaps.push({
              hotelId: hotel.hotel_id,
              color,
              startDate: chartData[prevIdx].date,
              startPrice: chartData[prevIdx][name] as number,
              endDate: chartData[i].date,
              endPrice: price as number,
            });
          }
          prevIdx = i;
        }
      }
    }

    return gaps;
  }, [chartData, filtered]);

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
          color: CHART_COLORS[filtered.indexOf(hotel) % CHART_COLORS.length],
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

  // Auto-collapse legend on mobile with many hotels
  useEffect(() => {
    if (isMobile && isMany) setLegendOpen(false);
    else if (!isMobile) setLegendOpen(true);
  }, [isMobile, isMany]);

  // Custom tooltip – only shown on hover, does NOT interfere with selected date
  const CustomTooltip = ({ active, payload, label }: any) => {
    if (!active || !payload?.length) return null;

    const items = hoveredHotel
      ? payload.filter((e: any) => e.name === hoveredHotel)
      : payload.sort((a: any, b: any) => (a.value ?? 0) - (b.value ?? 0));

    return (
      <div className="bg-surface-card border border-hairline rounded-none p-3 text-sm max-w-xs">
        <p className="font-display text-ink mb-1">
          {new Date(label + "T00:00:00").toLocaleDateString("de-DE", {
            weekday: "short",
            day: "2-digit",
            month: "long",
            year: "numeric",
          })}
        </p>
        {items.length > 5 ? (
          <p className="text-muted text-xs italic">
            {items.length} Hotels · Klicke für Details
          </p>
        ) : (
          items.map((entry: any, i: number) => (
            <div key={i} className="flex justify-between gap-4">
              <span style={{ color: entry.color }} className="truncate">
                {entry.name}
              </span>
              <span className="text-body-strong">{entry.value?.toFixed(0)} €</span>
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
    <div className="bg-surface-card border border-hairline rounded-none p-2 sm:p-4">
      <div className="flex items-center justify-between mb-2 sm:mb-4 flex-wrap gap-2">
        <h3 className="font-display uppercase tracking-display-md text-ink text-sm sm:text-base">
          Preisverlauf — {roomType === "single" ? "Einzelzimmer" : "Doppelzimmer"} / Nacht
        </h3>
        <div className="flex bg-surface-soft rounded-pill p-0.5 text-xs">
          <button
            onClick={() => onRoomTypeChange("double")}
            aria-pressed={roomType === "double"}
            className={`px-3 py-1 rounded-pill transition-colors ${
              roomType === "double"
                ? "bg-surface-elevated text-ink"
                : "text-muted hover:text-body"
            }`}
          >
            Doppelzimmer
          </button>
          <button
            onClick={() => onRoomTypeChange("single")}
            aria-pressed={roomType === "single"}
            className={`px-3 py-1 rounded-pill transition-colors ${
              roomType === "single"
                ? "bg-surface-elevated text-ink"
                : "text-muted hover:text-body"
            }`}
          >
            Einzelzimmer
          </button>
        </div>
      </div>

      <div className="flex flex-col lg:flex-row gap-4">
        {/* Empty state */}
        {filtered.length === 0 ? (
          <div className="flex-1 text-center text-muted py-16">
            <p className="text-lg">Keine Daten verfügbar</p>
            <p className="text-sm mt-1">
              Wähle Hotels aus und starte einen Abruf, um Preise zu sehen.
            </p>
          </div>
        ) : (
          <>
            {/* Chart */}
            <div className="flex-1 min-w-0">
          <ResponsiveContainer width="100%" height={chartHeight}>
            <LineChart
              data={chartData}
              onClick={handleChartClick}
              style={{ cursor: "pointer" }}
            >
              {weekendSpans.map((span, i) => (
                <ReferenceArea
                  key={`we-${i}`}
                  x1={span.start}
                  x2={span.end}
                  fill="#1f1f1f"
                  fillOpacity={1}
                  ifOverflow="hidden"
                />
              ))}
              <CartesianGrid strokeDasharray="3 3" stroke="#262626" />
              <XAxis
                dataKey="date"
                tickFormatter={formatDate}
                tick={{ fontSize: isMobile ? 9 : 11, fill: "#999999" }}
                interval="preserveStartEnd"
                minTickGap={isMobile ? 30 : 40}
              />
              <YAxis
                tick={{ fontSize: isMobile ? 9 : 11, fill: "#999999" }}
                tickFormatter={(v) => `${v} €`}
                width={isMobile ? 50 : 70}
                axisLine={{ stroke: "#262626" }}
              />
              <Tooltip content={<CustomTooltip />} />
              {selectedDate && (
                <ReferenceLine
                  x={selectedDate}
                  stroke="#999999"
                  strokeDasharray="4 4"
                  strokeWidth={1.5}
                />
              )}
              {filtered.map((hotel, i) => {
                const color = CHART_COLORS[i % CHART_COLORS.length];
                const isHovered = hoveredHotel === hotel.hotel_name;
                const strokeW = isHovered ? 3 : isMany ? 1.5 : 2;
                const opacity = hoveredHotel
                  ? isHovered
                    ? 1
                    : 0.1
                  : isMany
                    ? 0.5
                    : 1;

                return (
                  <Line
                    key={hotel.hotel_id}
                    type="monotone"
                    dataKey={hotel.hotel_name}
                    stroke={color}
                    strokeWidth={strokeW}
                    strokeOpacity={opacity}
                    dot={false}
                    connectNulls={false}
                    activeDot={{ r: isHovered ? 6 : 4 }}
                    isAnimationActive={false}
                  />
                );
              })}
              {/* Dashed bridge lines across data gaps */}
              {hotelGaps.map((gap, i) => {
                const gapName = filtered.find(
                  (h) => h.hotel_id === gap.hotelId
                )?.hotel_name;
                const gapOpacity = hoveredHotel
                  ? gapName === hoveredHotel
                    ? 1
                    : 0.1
                  : isMany
                    ? 0.5
                    : 1;

                return (
                  <ReferenceLine
                    key={`gap-${gap.hotelId}-${i}`}
                    segment={[
                      { x: gap.startDate, y: gap.startPrice },
                      { x: gap.endDate, y: gap.endPrice },
                    ]}
                    stroke={gap.color}
                    strokeDasharray="5 4"
                    strokeWidth={isMany ? 1.2 : 1.6}
                    strokeOpacity={gapOpacity}
                  />
                );
              })}
            </LineChart>
          </ResponsiveContainer>
            </div>

            {/* Selected date panel */}
            {selectedDate && selectedDatePrices.length > 0 && (
              <div className="lg:w-72 flex-shrink-0 border border-hairline rounded-none bg-surface-soft p-3 max-h-[500px] flex flex-col">
                <div className="flex items-center justify-between mb-2">
                  <h4 className="font-mono uppercase tracking-label-sm text-muted">
                    {new Date(selectedDate + "T00:00:00").toLocaleDateString("de-DE", {
                      weekday: "short",
                      day: "2-digit",
                      month: "long",
                      year: "numeric",
                    })}
                  </h4>
                  <button
                    onClick={() => setSelectedDate(null)}
                    className="text-muted hover:text-body text-lg leading-none"
                    title="Auswahl aufheben"
                  >
                    &times;
                  </button>
                </div>
                <div className="overflow-y-auto flex-1 space-y-1 text-sm">
                  {selectedDatePrices.map((h, i) => (
                    <div
                      key={i}
                      className="flex justify-between items-center gap-2 px-2 py-1 rounded-none hover:bg-surface-elevated"
                    >
                      <div className="flex items-center gap-1.5 min-w-0">
                        <span
                          className="inline-block w-2.5 h-0.5 flex-shrink-0 rounded"
                          style={{ backgroundColor: h.color }}
                        />
                        <span className="truncate text-body">{h.hotel_name}</span>
                        {h.stars && (
                          <span className="text-warning text-xs flex-shrink-0">
                            {"★".repeat(h.stars)}
                          </span>
                        )}
                      </div>
                      <span className="text-ink flex-shrink-0">
                        {h.price?.toFixed(0)} €
                      </span>
                    </div>
                  ))}
                </div>
                <p className="text-xs text-muted mt-2 pt-2 border-t border-hairline text-center">
                  {selectedDatePrices.length} Hotels
                </p>
              </div>
            )}
          </>
        )}
      </div>

      {/* Custom interactive legend */}
      <div className="mt-2 border-t border-hairline pt-2">
        <button
          onClick={() => setLegendOpen(!legendOpen)}
          className="text-xs text-muted hover:text-body mb-1 flex items-center gap-1"
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
                  style={{ backgroundColor: CHART_COLORS[i % CHART_COLORS.length] }}
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
