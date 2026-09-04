interface Props {
  from: string;
  to: string;
  onChange: (from: string, to: string) => void;
}

export default function DateRangePicker({ from, to, onChange }: Props) {
  return (
    <div className="bg-surface-card border border-hairline rounded-none p-4">
      <h3 className="font-display uppercase tracking-display-md text-ink mb-3">Zeitraum</h3>
      <div className="flex flex-wrap gap-3 items-center">
        <div className="flex-1 min-w-0">
          <label className="block text-xs text-muted mb-1">Von</label>
          <input
            type="date"
            value={from}
            onChange={(e) => onChange(e.target.value, to)}
            className="bg-transparent border-b border-hairline-strong rounded-none py-1.5 text-sm text-ink focus:outline-none focus:border-ink w-full"
          />
        </div>
        <span className="text-muted mt-5">—</span>
        <div className="flex-1 min-w-0">
          <label className="block text-xs text-muted mb-1">Bis</label>
          <input
            type="date"
            value={to}
            onChange={(e) => onChange(from, e.target.value)}
            className="bg-transparent border-b border-hairline-strong rounded-none py-1.5 text-sm text-ink focus:outline-none focus:border-ink w-full"
          />
        </div>
      </div>
    </div>
  );
}
