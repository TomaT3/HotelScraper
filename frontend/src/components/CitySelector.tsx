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
    <div className="flex items-center gap-2">
      {cities.map((city) => (
        <button
          key={city.name}
          onClick={() => onCityChange(city.name)}
          className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${
            selectedCity === city.name
              ? "bg-blue-600 text-white shadow-sm"
              : "bg-white text-gray-600 hover:bg-gray-100 border border-gray-200"
          }`}
          title={city.dest_label ?? city.name}
        >
          {city.name}
        </button>
      ))}
    </div>
  );
}
