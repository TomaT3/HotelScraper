/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{js,ts,jsx,tsx}"],
  theme: {
    extend: {
      colors: {
        canvas: "#000000",
        surface: {
          soft: "#0d0d0d",
          card: "#141414",
          elevated: "#1f1f1f",
        },
        hairline: {
          DEFAULT: "#262626",
          strong: "#3a3a3a",
        },
        ink: "#ffffff",
        body: {
          DEFAULT: "#cccccc",
          strong: "#e6e6e6",
        },
        muted: {
          DEFAULT: "#999999",
          soft: "#808080",
        },
        link: "#c3d9f3",
        warning: "#d4a017",
        success: "#5fa657",
        danger: "#e05252",
      },
      fontFamily: {
        display: ['"Saira Condensed"', "sans-serif"],
        serif: ['"EB Garamond"', "Georgia", "serif"],
        mono: ['"IBM Plex Mono"', "ui-monospace", "SFMono-Regular", "monospace"],
      },
      borderRadius: {
        none: "0px",
        pill: "9999px",
        full: "9999px",
      },
      letterSpacing: {
        wordmark: "6px",
        display: "4px",
        "display-lg": "3px",
        "display-md": "2px",
        "display-sm": "1.5px",
        "display-xs": "1px",
        label: "2.5px",
        "label-sm": "2px",
      },
    },
  },
  plugins: [],
};
