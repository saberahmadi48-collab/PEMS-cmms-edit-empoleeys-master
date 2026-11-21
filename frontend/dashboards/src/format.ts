import type { Boot } from "./types";

let boot: Boot = { dataUrl: "", links: {}, i18n: {}, locale: "en", rtl: false };
let nf = new Intl.NumberFormat("en");
let compact = new Intl.NumberFormat("en", { notation: "compact", maximumFractionDigits: 1 });
let pct = new Intl.NumberFormat("en", { style: "percent", maximumFractionDigits: 0 });
let dateTime = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" });
let monthYear = new Intl.DateTimeFormat("en", { year: "numeric", month: "short" });

export function initFormat(b: Boot) {
  boot = b;
  const loc = b.locale || "en";
  const cal = loc.toLowerCase().startsWith("fa") ? "fa-IR-u-ca-persian" : loc;
  nf = new Intl.NumberFormat(loc);
  compact = new Intl.NumberFormat(loc, { notation: "compact", maximumFractionDigits: 1 });
  pct = new Intl.NumberFormat(loc, { style: "percent", maximumFractionDigits: 0 });
  dateTime = new Intl.DateTimeFormat(cal, { dateStyle: "medium", timeStyle: "short" });
  monthYear = new Intl.DateTimeFormat(cal, { year: "numeric", month: "short" });
}

export const isRtl = () => boot.rtl;
export const linkFor = (key: string): string | undefined => boot.links[key] ?? undefined;

/** Translated text for an Exec_* key (the server ships every Exec_* resource); falls back to the fallback or the key itself. */
export function t(key: string, vars?: Record<string, string | number>, fallback?: string): string {
  let text = boot.i18n[key] ?? fallback ?? key;
  if (vars) for (const [k, v] of Object.entries(vars)) text = text.replaceAll(`{${k}}`, String(v));
  return text;
}

export const num = (n: number) => nf.format(n);
export const compactNum = (n: number) => compact.format(n);
export const percent = (n: number) => pct.format(n / 100);
export const when = (iso: string) => dateTime.format(new Date(iso));
export const month = (iso: string) => monthYear.format(new Date(iso));
export const locale = () => boot.locale || "en";
