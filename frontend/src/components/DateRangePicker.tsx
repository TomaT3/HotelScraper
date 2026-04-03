interface Props {
  from: string;
  to: string;
  onChange: (from: string, to: string) => void;
}

export default function DateRangePicker({ from, to, onChange }: Props) {
  return (
    <div className="bg-white rounded-lg shadow p-4">
      <h3 className="font-semibold text-gray-700 mb-3">Zeitraum</h3>
      <div className="flex flex-wrap gap-3 items-center">
        <div className="flex-1 min-w-0">
          <label className="block text-xs text-gray-500 mb-1">Von</label>
          <input
            type="date"
            value={from}
            onChange={(e) => onChange(e.target.value, to)}
            className="border rounded px-3 py-1.5 text-sm w-full"
          />
        </div>
        <span className="text-gray-400 mt-5">—</span>
        <div className="flex-1 min-w-0">
          <label className="block text-xs text-gray-500 mb-1">Bis</label>
          <input
            type="date"
            value={to}
            onChange={(e) => onChange(from, e.target.value)}
            className="border rounded px-3 py-1.5 text-sm w-full"
          />
        </div>
      </div>
    </div>
  );
}
