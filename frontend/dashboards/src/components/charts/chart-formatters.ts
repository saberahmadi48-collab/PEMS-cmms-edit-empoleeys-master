// PEMS: the registry hard-codes en-US month/day formats. The dashboards here are monthly series shown in the user's language
// (Persian UI uses the Jalali calendar), so the formatters are live bindings that `configureChartFormatting` swaps once at start-up.
export let shortDateFmt = new Intl.DateTimeFormat("en-US", {
  month: "short",
  day: "numeric",
});

export let weekdayDateFmt = new Intl.DateTimeFormat("en-US", {
  weekday: "short",
  month: "short",
  day: "numeric",
});

export const hmsTimeFmt = new Intl.DateTimeFormat("en-US", {
  hour: "2-digit",
  minute: "2-digit",
  second: "2-digit",
  hour12: false,
});

// `Intl.NumberFormat.prototype.format` is a bound getter — safe to extract.
export let intFmt = new Intl.NumberFormat("en-US").format;
export let valueLabelFmt = new Intl.NumberFormat("en-US", {
  maximumFractionDigits: 0,
}).format;

export function configureChartFormatting(locale: string, monthly: boolean) {
  const calendarLocale = locale.toLowerCase().startsWith("fa") ? "fa-IR-u-ca-persian" : locale;
  shortDateFmt = monthly
    ? new Intl.DateTimeFormat(calendarLocale, { year: "numeric", month: "short" })
    : new Intl.DateTimeFormat(calendarLocale, { month: "short", day: "numeric" });
  weekdayDateFmt = monthly
    ? new Intl.DateTimeFormat(calendarLocale, { year: "numeric", month: "long" })
    : new Intl.DateTimeFormat(calendarLocale, { weekday: "short", month: "short", day: "numeric" });
  intFmt = new Intl.NumberFormat(locale).format;
  valueLabelFmt = new Intl.NumberFormat(locale, { maximumFractionDigits: 0 }).format;
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value);
}

/**
 * Tooltip value for a `<Band>` row: `"low – high"` (ordered), the value itself
 * when both edges read the same key, or `null` when either edge is missing
 * (e.g. Bollinger warm-up rows).
 */
export function formatBandRange(
  point: Record<string, unknown>,
  upperKey: string,
  lowerKey: string,
  format: (value: number) => string = intFmt
): string | number | null {
  const upper = point[upperKey];
  const lower = point[lowerKey];
  if (!(isFiniteNumber(upper) && isFiniteNumber(lower))) {
    return null;
  }
  if (upperKey === lowerKey) {
    return upper;
  }
  return `${format(Math.min(lower, upper))} – ${format(Math.max(lower, upper))}`;
}
