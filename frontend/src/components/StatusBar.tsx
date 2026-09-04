import type { Status } from "../api/types";

interface Props {
  status: Status | null;
  loading: boolean;
  onFetch?: () => void;
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
      <div className="bg-surface-card border border-hairline rounded-none p-4 animate-pulse">
        <div className="h-4 bg-surface-elevated rounded w-1/3"></div>
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
    <div className="bg-surface-card border border-hairline rounded-none p-4">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div className="flex flex-wrap gap-6 text-sm">
          <div>
            <span className="text-muted">Hotels:</span>{" "}
            <span className="text-body-strong">
              {status.active_hotels}/{status.total_hotels}
            </span>
          </div>
          <div>
            <span className="text-muted">Preise:</span>{" "}
            <span className="text-body-strong">
              {(status.total_prices ?? 0).toLocaleString("de-DE")}
            </span>
          </div>
          <div>
            <span className="text-muted">Abdeckung:</span>{" "}
            <span className="text-body-strong">{status.coverage_pct}%</span>
            <span className="text-muted ml-1">
              ({status.dates_covered}/{status.dates_total} Tage)
            </span>
          </div>
          <div>
            <span className="text-muted">Letzter Abruf:</span>{" "}
            <span className="text-body">{lastFetch}</span>
          </div>
          <div>
            <span className="text-muted">Nächster Abruf:</span>{" "}
            <span className="text-body">{nextRun}</span>
          </div>
          <div>
            <span className="text-muted">Scheduler:</span>{" "}
            <span
              className={`${
                status.scheduler_running ? "text-success" : "text-danger"
              }`}
            >
              {status.scheduler_running ? "Aktiv" : "Inaktiv"}
            </span>
          </div>
        </div>
        {onFetch && (
          <button
            onClick={onFetch}
            disabled={fetching}
            className="px-4 py-2 border border-ink text-ink rounded-pill font-mono uppercase tracking-label text-sm hover:bg-ink hover:text-canvas transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
          >
            {fetching ? "Lädt..." : "Jetzt abrufen"}
          </button>
        )}
      </div>
    </div>
  );
}
