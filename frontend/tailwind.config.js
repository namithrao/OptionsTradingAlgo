/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        'trading-green': '#22c55e',
        'trading-red': '#ef4444',
        'trading-blue': '#3b82f6',
      },
    },
  },
  plugins: [
    require('@tailwindcss/typography'),
  ],
}