import type { Status } from "../api/types";

interface Props {
  status: Status | null;
  loading: boolean;
  onFetch: () => void;
  fetching: boolean;
}

export default function StatusBar({
  status,
  loading,
  onFetch,
  fetching,
}: Props) {
  if (loading || !status) {
    return (
      <div className="bg-white rounded-lg shadow p-4 animate-pulse">
        <div className="h-4 bg-gray-200 rounded w-1/3"></div>
      </div>
    );
  }

  const lastFetch = status.last_fetch
    ? new Date(status.last_fetch).toLocaleString("de-DE")
    : "Noch nie";

  const nextRun = status.next_run
    ? new Date(status.next_run).toLocaleString("de-DE")
    : "—";

  return (
    <div className="bg-white rounded-lg shadow p-4">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div className="flex flex-wrap gap-6 text-sm">
          <div>
            <span className="text-gray-500">Hotels:</span>{" "}
            <span className="font-semibold">
              {status.active_hotels}/{status.total_hotels}
            </span>
          </div>
          <div>
            <span className="text-gray-500">Preise:</span>{" "}
            <span className="font-semibold">
              {status.total_prices.toLocaleString("de-DE")}
            </span>
          </div>
          <div>
            <span className="text-gray-500">Abdeckung:</span>{" "}
            <span className="font-semibold">{status.coverage_pct}%</span>
            <span className="text-gray-400 ml-1">
              ({status.dates_covered}/{status.dates_total} Tage)
            </span>
          </div>
          <div>
            <span className="text-gray-500">Letzter Abruf:</span>{" "}
            <span className="font-medium">{lastFetch}</span>
          </div>
          <div>
            <span className="text-gray-500">Nächster Abruf:</span>{" "}
            <span className="font-medium">{nextRun}</span>
          </div>
          <div>
            <span className="text-gray-500">Scheduler:</span>{" "}
            <span
              className={`font-semibold ${
                status.scheduler_running ? "text-green-600" : "text-red-500"
              }`}
            >
              {status.scheduler_running ? "Aktiv" : "Inaktiv"}
            </span>
          </div>
          <div>
            <span className="text-gray-500">Version:</span>{" "}
            <span className="font-mono text-xs font-semibold">{status.version}</span>
          </div>
        </div>
        <button
          onClick={onFetch}
          disabled={fetching}
          className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed text-sm font-medium transition-colors"
        >
          {fetching ? "Lädt..." : "Jetzt abrufen"}
        </button>
      </div>
    </div>
  );
}
