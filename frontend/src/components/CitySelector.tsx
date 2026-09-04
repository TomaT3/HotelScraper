import type { City } from "../api/types";

interface Props {
  cities: City[];
  selectedCity: string;
  onCityChange: (city: string) => void;
}

export default function CitySelector({
  cities,
  selectedCity,
  onCityChange,
}: Props) {
  if (cities.length <= 1) return null;

  return (
    <div
      className="flex items-center gap-2"
      role="radiogroup"
      aria-label="Stadt auswählen"
    >
      {cities.map((city) => (
        <button
          key={city.name}
          onClick={() => onCityChange(city.name)}
          role="radio"
          aria-checked={selectedCity === city.name}
          className={`px-4 py-1.5 text-sm font-mono uppercase tracking-label-sm rounded-pill border transition-colors ${
            selectedCity === city.name
              ? "border-ink text-ink"
              : "border-hairline-strong text-muted hover:text-body"
          }`}
          title={city.dest_label ?? city.name}
        >
          {city.name}
        </button>
      ))}
    </div>
  );
}
