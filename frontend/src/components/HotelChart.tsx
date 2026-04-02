import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  ResponsiveContainer,
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

export default function HotelChart({ data, selectedIds }: Props) {
  const filtered = data.filter((h) => selectedIds.has(h.hotel_id));

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

  // Custom tooltip
  const CustomTooltip = ({ active, payload, label }: any) => {
    if (!active || !payload?.length) return null;
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
        {payload
          .sort((a: any, b: any) => (a.value ?? 0) - (b.value ?? 0))
          .map((entry: any, i: number) => (
            <div key={i} className="flex justify-between gap-4">
              <span style={{ color: entry.color }} className="truncate">
                {entry.name}
              </span>
              <span className="font-medium">{entry.value?.toFixed(0)} €</span>
            </div>
          ))}
      </div>
    );
  };

  return (
    <div className="bg-white rounded-lg shadow p-4">
      <h3 className="font-semibold text-gray-700 mb-4">
        Preisverlauf — Doppelzimmer / Nacht
      </h3>
      <ResponsiveContainer width="100%" height={500}>
        <LineChart data={chartData}>
          <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
          <XAxis
            dataKey="date"
            tickFormatter={formatDate}
            tick={{ fontSize: 11 }}
            interval="preserveStartEnd"
            minTickGap={40}
          />
          <YAxis
            tick={{ fontSize: 11 }}
            tickFormatter={(v) => `${v} €`}
            width={70}
          />
          <Tooltip content={<CustomTooltip />} />
          <Legend
            wrapperStyle={{ fontSize: "12px", paddingTop: "8px" }}
            iconType="plainline"
          />
          {filtered.map((hotel, i) => (
            <Line
              key={hotel.hotel_id}
              type="monotone"
              dataKey={hotel.hotel_name}
              stroke={COLORS[i % COLORS.length]}
              strokeWidth={2}
              dot={false}
              connectNulls
              activeDot={{ r: 4 }}
            />
          ))}
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}
