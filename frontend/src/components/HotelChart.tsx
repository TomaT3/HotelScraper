import { useEffect, useState, useCallback, useMemo, useRef, useLayoutEffect } from "react";
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
  Brush,
} from "recharts";
import type { HotelPrices } from "../api/types";
import { CHART_COLORS } from "../theme/chartColors";

interface Props {
  data: HotelPrices[];
  selectedIds: Set<number>;
  roomType: "single" | "double";
  onRoomTypeChange: (type: "single" | "double") => void;
  favorites: Set<number>;
  onToggleSelected: (id: number) => void;
  onToggleFavorite: (id: number) => void;
}

interface ChartDataPoint {
  date: string;
  [hotelName: string]: number | string;
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
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

export default function HotelChart({
  data,
  selectedIds,
  roomType,
  onRoomTypeChange,
  favorites,
  onToggleSelected,
  onToggleFavorite,
}: Props) {
  const [hoveredHotel, setHoveredHotel] = useState<string | null>(null);
  const [selectedHotelId, setSelectedHotelId] = useState<number | null>(null);
  const [selectedDate, setSelectedDate] = useState<string | null>(null);
  const [legendOpen, setLegendOpen] = useState(true);
  const windowWidth = useWindowWidth();
  const isMobile = windowWidth < 768;
  // Kontrollierter Brush-Bereich; null = kompletter Datensatz (uncontrolled)
  const [brushRange, setBrushRange] = useState<{
    startIndex: number;
    endIndex: number;
  } | null>(null);
  const chartWrapperRef = useRef<HTMLDivElement | null>(null);

  const filtered = useMemo(
    () => data.filter((h) => selectedIds.has(h.hotel_id)),
    [data, selectedIds]
  );
  const isMany = filtered.length > 15;

  // Merge all hotel prices into a single dataset keyed by date
  const dateMap = useMemo(() => {
    const map = new Map<string, ChartDataPoint>();
    for (const hotel of filtered) {
      for (const p of hotel.prices) {
        if (!map.has(p.date)) {
          map.set(p.date, { date: p.date });
        }
        map.get(p.date)![hotel.hotel_name] = p.price_eur;
      }
    }
    return map;
  }, [filtered]);

  const chartData = useMemo(
    () =>
      Array.from(dateMap.values()).sort((a, b) =>
        a.date.localeCompare(b.date)
      ),
    [dateMap]
  );

  // Reset stale selection when the selected hotel leaves the filtered set
  useEffect(() => {
    if (
      selectedHotelId !== null &&
      !filtered.some((h) => h.hotel_id === selectedHotelId)
    ) {
      setSelectedHotelId(null);
    }
  }, [filtered, selectedHotelId]);

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
          hotel_id: hotel.hotel_id,
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

    // Hit-Linien duplizieren den dataKey der sichtbaren Linien → deduplizieren
    const deduped = payload.filter((e: any, i: number, arr: any[]) => {
      const key = e.dataKey ?? e.name;
      return arr.findIndex((x: any) => (x.dataKey ?? x.name) === key) === i;
    });
    const items = hoveredHotel
      ? deduped.filter((e: any) => e.name === hoveredHotel)
      : deduped.sort((a: any, b: any) => (a.value ?? 0) - (b.value ?? 0));

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
        {items.slice(0, 5).map((entry: any, i: number) => (
          <div key={i} className="flex justify-between gap-4">
            <span style={{ color: entry.color }} className="truncate">
              {entry.name}
            </span>
            <span className="text-body-strong">{entry.value?.toFixed(0)} €</span>
          </div>
        ))}
        {items.length > 5 && (
          <p className="text-muted text-xs mt-1">
            … +{items.length - 5} weitere
          </p>
        )}
      </div>
    );
  };

  const chartHeight = isMobile ? 300 : isMany ? 450 : 500;

  // Build the selected-date panel data
  const selectedDatePrices = selectedDate ? getPricesForDate(selectedDate) : [];

  // Hotel selected via line click (drives highlight + quick actions)
  const selectedHotel =
    selectedHotelId !== null
      ? filtered.find((h) => h.hotel_id === selectedHotelId) ?? null
      : null;
  const selectedHotelColor =
    selectedHotel !== null
      ? CHART_COLORS[filtered.indexOf(selectedHotel) % CHART_COLORS.length]
      : undefined;

  // Mausrad-Zoom: bei markiertem Datum um dieses zoomen, sonst um die Mitte
  // des aktuell sichtbaren Bereichs. Reinzoomen bei deltaY < 0, rauszoomen
  // bei deltaY > 0; der Fokus bleibt proportional an seiner Bildschirmposition.
  const handleWheelZoom = useCallback(
    (e: WheelEvent) => {
      const n = chartData.length;
      if (n < 2) return;
      const base = brushRange ?? { startIndex: 0, endIndex: n - 1 };
      const span = base.endIndex - base.startIndex + 1;
      const zoomingIn = e.deltaY < 0;
      // An den Zoom-Grenzen das Seiten-Scrollen nicht blockieren
      if ((zoomingIn && span <= 2) || (!zoomingIn && span >= n)) return;
      e.preventDefault();
      const focusIdx = selectedDate
        ? chartData.findIndex((d) => d.date === selectedDate)
        : -1;
      const factor = zoomingIn ? 0.7 : 1.4;
      setBrushRange((prev) => {
        const b = prev ?? { startIndex: 0, endIndex: n - 1 };
        const s = b.endIndex - b.startIndex + 1;
        const focus =
          focusIdx >= 0
            ? focusIdx
            : Math.round((b.startIndex + b.endIndex) / 2);
        const newSpan = clamp(Math.round(s * factor), 2, n);
        const norm = s > 1 ? (focus - b.startIndex) / (s - 1) : 0.5;
        const newStart = clamp(
          Math.round(focus - norm * (newSpan - 1)),
          0,
          n - newSpan
        );
        return { startIndex: newStart, endIndex: newStart + newSpan - 1 };
      });
    },
    [chartData, selectedDate, brushRange]
  );

  // React bindet Wheel-Listener teils passiv — nativer Listener mit
  // { passive: false }, damit preventDefault() das Seiten-Scrollen stoppt.
  useEffect(() => {
    const el = chartWrapperRef.current;
    if (!el) return;
    el.addEventListener("wheel", handleWheelZoom, { passive: false });
    return () => el.removeEventListener("wheel", handleWheelZoom);
  }, [handleWheelZoom]);

  // Neu geladener Datumsbereich: wieder den vollen Bereich zeigen.
  // useLayoutEffect, damit der Reset vor dem Paint greift und der interne
  // Brush-Index von Recharts nicht auf dem alten Bereich stehen bleibt.
  const rangeKey =
    chartData.length > 0
      ? `${chartData[0].date}~${chartData[chartData.length - 1].date}`
      : "empty";
  useLayoutEffect(() => {
    setBrushRange({ startIndex: 0, endIndex: Math.max(0, chartData.length - 1) });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rangeKey]);

  return (
    <div className="bg-surface-card border border-hairline rounded-none p-2 sm:p-4">
      <div className="flex items-center justify-between mb-2 sm:mb-4 flex-wrap gap-2">
        <h3 className="font-display uppercase tracking-display-md text-ink text-sm sm:text-base">
          Preisverlauf — {roomType === "single" ? "Einzelzimmer" : "Doppelzimmer"} / Nacht
        </h3>
        <div
          className="flex bg-surface-soft rounded-pill p-0.5 text-xs"
          role="radiogroup"
          aria-label="Zimmertyp"
        >
          <button
            onClick={() => onRoomTypeChange("double")}
            role="radio"
            aria-checked={roomType === "double"}
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
            role="radio"
            aria-checked={roomType === "single"}
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

      {/* Quick actions for the hotel selected via line click */}
      {selectedHotel && selectedHotelColor && (
        <div className="flex items-center gap-2 mb-2 sm:mb-3 px-2 py-1.5 bg-surface-soft border border-hairline rounded-none">
          <span
            className="inline-block w-2.5 h-0.5 flex-shrink-0 rounded"
            style={{ backgroundColor: selectedHotelColor }}
          />
          <span className="truncate text-xs text-body flex-1 min-w-0">
            {selectedHotel.hotel_name}
          </span>
          <button
            onClick={() => {
              onToggleSelected(selectedHotel.hotel_id);
              setSelectedHotelId(null);
            }}
            className="px-2.5 py-0.5 rounded-pill font-mono uppercase tracking-label-sm text-xs border border-hairline-strong text-muted hover:text-body hover:border-ink transition-colors flex-shrink-0"
          >
            Ausblenden
          </button>
          <button
            onClick={() => onToggleFavorite(selectedHotel.hotel_id)}
            className={`text-sm flex-shrink-0 transition-colors ${
              favorites.has(selectedHotel.hotel_id)
                ? "text-warning hover:opacity-75"
                : "text-muted-soft hover:text-muted"
            }`}
            title={
              favorites.has(selectedHotel.hotel_id)
                ? "Favorit entfernen"
                : "Als Favorit markieren"
            }
            aria-label={
              favorites.has(selectedHotel.hotel_id)
                ? "Favorit entfernen"
                : "Als Favorit markieren"
            }
          >
            {favorites.has(selectedHotel.hotel_id) ? "⭐" : "☆"}
          </button>
          <button
            onClick={() => setSelectedHotelId(null)}
            className="text-muted hover:text-body text-lg leading-none flex-shrink-0"
            title="Auswahl aufheben"
            aria-label="Auswahl aufheben"
          >
            &times;
          </button>
        </div>
      )}

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
            <div ref={chartWrapperRef} className="flex-1 min-w-0">
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
                const isSelected = selectedHotel?.hotel_id === hotel.hotel_id;
                const isHovered = hoveredHotel === hotel.hotel_name;
                // A selected hotel takes precedence over hover highlighting
                const emphasized =
                  selectedHotel !== null ? isSelected : isHovered;
                const strokeW = emphasized ? 3 : isMany ? 1.5 : 2;
                const opacity =
                  selectedHotel !== null
                    ? isSelected
                      ? 1
                      : 0.1
                    : hoveredHotel
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
                    activeDot={{ r: emphasized ? 6 : 4 }}
                    isAnimationActive={false}
                  />
                );
              })}
              {/* Dashed bridge lines across data gaps */}
              {hotelGaps.map((gap, i) => {
                const gapName = filtered.find(
                  (h) => h.hotel_id === gap.hotelId
                )?.hotel_name;
                const gapOpacity =
                  selectedHotel !== null
                    ? gap.hotelId === selectedHotel.hotel_id
                      ? 1
                      : 0.1
                    : hoveredHotel
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
              {/* Unsichtbare dicke Hit-Linien: tragen Klick + Hover und liegen
                  ueber den sichtbaren Linien + Gap-Bruecken (fangen Klicks) */}
              {filtered.map((hotel) => (
                <Line
                  key={`hit-${hotel.hotel_id}`}
                  type="monotone"
                  dataKey={hotel.hotel_name}
                  stroke="transparent"
                  strokeWidth={10}
                  dot={false}
                  activeDot={false}
                  connectNulls={false}
                  isAnimationActive={false}
                  onClick={(
                    _data: any,
                    indexOrEvent: any,
                    maybeEvent?: any
                  ) => {
                    // Recharts 2.15.4 verdrahtet onClick am Pfad als
                    // (props, event) — Event daher robust aus Position 2
                    // oder 3 ziehen.
                    const evt = (maybeEvent ?? indexOrEvent) as
                      | { stopPropagation?: () => void }
                      | undefined;
                    evt?.stopPropagation?.(); // Datums-Selektion des Charts nicht ausloesen
                    setSelectedHotelId((prev) =>
                      prev === hotel.hotel_id ? null : hotel.hotel_id
                    );
                  }}
                  onMouseEnter={() => setHoveredHotel(hotel.hotel_name)}
                  onMouseLeave={() => setHoveredHotel(null)}
                />
              ))}
              {/* Brush: Zoom/Schieben des Datumsbereichs per Maus (controlled) */}
              <Brush
                dataKey="date"
                height={26}
                stroke="#262626"
                fill="transparent"
                travellerWidth={8}
                tickFormatter={formatDate}
                startIndex={brushRange?.startIndex}
                endIndex={brushRange?.endIndex}
                onChange={(r) => {
                  if (r.startIndex === undefined || r.endIndex === undefined)
                    return;
                  setBrushRange({
                    startIndex: r.startIndex,
                    endIndex: r.endIndex,
                  });
                }}
              />
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
                  {selectedDatePrices.map((h) => {
                    const rowSelected = h.hotel_id === selectedHotelId;
                    const isFavorite = favorites.has(h.hotel_id);
                    return (
                      <div
                        key={h.hotel_id}
                        role="button"
                        tabIndex={0}
                        onClick={() =>
                          setSelectedHotelId((prev) =>
                            prev === h.hotel_id ? null : h.hotel_id
                          )
                        }
                        onKeyDown={(e) => {
                          if (e.target !== e.currentTarget) return; // Events der inneren Buttons nicht abfangen
                          if (e.key === "Enter" || e.key === " ") {
                            e.preventDefault();
                            setSelectedHotelId((prev) =>
                              prev === h.hotel_id ? null : h.hotel_id
                            );
                          }
                        }}
                        className={`flex items-center gap-2 px-2 py-1 cursor-pointer transition-colors ${
                          rowSelected
                            ? "bg-surface-elevated"
                            : "hover:bg-surface-elevated"
                        }`}
                        style={
                          rowSelected
                            ? { boxShadow: `inset 2px 0 0 ${h.color}` }
                            : undefined
                        }
                        title={
                          rowSelected
                            ? "Auswahl aufheben"
                            : "Im Chart hervorheben"
                        }
                      >
                        <div className="flex items-center gap-1.5 min-w-0 flex-1">
                          <span
                            className="inline-block w-2.5 h-0.5 flex-shrink-0 rounded"
                            style={{ backgroundColor: h.color }}
                          />
                          <span
                            className={`truncate ${
                              rowSelected ? "text-ink" : "text-body"
                            }`}
                          >
                            {h.hotel_name}
                          </span>
                          {h.stars ? (
                            <span className="text-warning text-xs flex-shrink-0">
                              {"★".repeat(h.stars)}
                            </span>
                          ) : null}
                        </div>
                        <span className="text-ink flex-shrink-0">
                          {h.price?.toFixed(0)} €
                        </span>
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            onToggleFavorite(h.hotel_id);
                          }}
                          className={`text-sm flex-shrink-0 transition-colors ${
                            isFavorite
                              ? "text-warning hover:opacity-75"
                              : "text-muted-soft hover:text-muted"
                          }`}
                          title={
                            isFavorite
                              ? "Favorit entfernen"
                              : "Als Favorit markieren"
                          }
                          aria-label={
                            isFavorite
                              ? "Favorit entfernen"
                              : "Als Favorit markieren"
                          }
                        >
                          {isFavorite ? "⭐" : "☆"}
                        </button>
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            onToggleSelected(h.hotel_id);
                          }}
                          className="px-1.5 py-0.5 rounded-pill font-mono uppercase tracking-label-sm text-[10px] border border-hairline-strong text-muted hover:text-body hover:border-ink transition-colors flex-shrink-0"
                          title="Hotel ausblenden"
                          aria-label={`${h.hotel_name} ausblenden`}
                        >
                          Ausblenden
                        </button>
                      </div>
                    );
                  })}
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
