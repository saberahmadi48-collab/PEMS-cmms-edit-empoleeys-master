// Semantic colours come from the PEMS theme tokens (site.css --pems-chart-*), so every theme restyles the charts.
export const C = {
  approved: "var(--pems-chart-positive, #22c55e)",
  inReview: "var(--pems-chart-primary, #3b82f6)",
  returned: "var(--pems-chart-negative, #ef4444)",
  draft: "var(--pems-chart-neutral, #94a3b8)",
  other: "var(--pems-chart-tertiary, #14b8a6)",
  accent: "var(--pems-chart-secondary, #a855f7)",
  warning: "var(--pems-chart-warning, #f59e0b)",
} as const;

export const BUCKET_COLOR: Record<string, string> = {
  Approved: C.approved,
  InReview: C.inReview,
  Returned: C.returned,
  Draft: C.draft,
  Other: C.other,
};

export const SEVERITY_COLOR: Record<string, string> = {
  High: "var(--pems-chart-danger-text, #b91c1c)",
  Medium: "var(--pems-chart-warning-text, #b45309)",
  Info: "var(--pems-text-muted, #5c6f85)",
};

/** Categorical palette for charts that need N distinct colours (theme tokens, then repeats). */
export const CATEGORICAL = [C.inReview, C.other, C.accent, C.warning, C.returned, C.approved, C.draft];
