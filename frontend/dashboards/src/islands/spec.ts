// Chart description produced by the server (PEMS.Web/Models/BkChartSpec.cs, rendered by Views/Shared/_BkChart.cshtml).
// Pages describe WHAT to draw (kind, labels, series); this folder decides HOW with Bklit components.
export type ChartKind = "bar" | "hbar" | "donut" | "gauge";

export interface SeriesSpec {
  name: string;
  values: number[];
  color?: string;
}

export interface ChartSpec {
  kind: ChartKind;
  ariaLabel: string;
  emptyText: string;
  labels: string[];
  series: SeriesSpec[];
  /** labels are "yyyy-MM" and are shown as localized month names (Jalali in Persian) */
  monthly?: boolean;
  stacked?: boolean;
  /** donut: colour per label; falls back to the theme palette */
  colors?: string[];
  /** donut centre caption (the total is the number) */
  centerLabel?: string;
  /** gauge: value 0-100 and its caption */
  value?: number;
  unit?: string;
  /** number of fraction digits shown in tooltips/value labels */
  decimals?: number;
}

export function isEmpty(spec: ChartSpec): boolean {
  if (spec.kind === "gauge") return spec.value == null;
  return spec.labels.length === 0 || spec.series.every(s => s.values.every(v => !v));
}

/** "2026-04" -> a Date at the start of that month (local), or null when the label is not a month. */
export function parseMonth(label: string): Date | null {
  const m = /^(\d{4})-(\d{2})/.exec(label);
  return m ? new Date(Number(m[1]), Number(m[2]) - 1, 1) : null;
}
