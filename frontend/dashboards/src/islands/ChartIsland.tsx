import { useMemo } from "react";
import { Bar } from "@/components/charts/bar";
import { BarChart } from "@/components/charts/bar-chart";
import { BarXAxis } from "@/components/charts/bar-x-axis";
import { BarYAxis } from "@/components/charts/bar-y-axis";
import { Gauge } from "@/components/charts/gauge";
import { Grid } from "@/components/charts/grid";
import { YAxis } from "@/components/charts/y-axis";
import { ChartTooltip } from "@/components/charts/tooltip";
import { C, CATEGORICAL } from "@/colors";
import { month } from "@/format";
import { DonutWithLegend, Swatches } from "@/sections/Charts";
import { ChartBox, Empty } from "@/ui/primitives";
import { type ChartSpec, type SeriesSpec, isEmpty, parseMonth } from "./spec";

const MAX_ROWS = 20;
const seriesColor = (s: SeriesSpec, i: number) => s.color ?? CATEGORICAL[i % CATEGORICAL.length];

function fmt(decimals: number | undefined, locale: string) {
  const nf = new Intl.NumberFormat(locale, { maximumFractionDigits: decimals ?? 0, minimumFractionDigits: decimals ?? 0 });
  return (n: number) => nf.format(n);
}

function useLabels(spec: ChartSpec) {
  return useMemo(() => spec.labels.map(l => {
    if (!spec.monthly) return l;
    const d = parseMonth(l);
    return d ? month(d.toISOString()) : l;
  }), [spec.labels, spec.monthly]);
}

/** rows for BarChart: one object per category, one numeric field per series (s0, s1, ...). */
function rowsOf(labels: string[], series: SeriesSpec[]) {
  return labels.map((label, i) => {
    const row: Record<string, string | number> = { label };
    series.forEach((s, k) => { row[`s${k}`] = s.values[i] ?? 0; });
    return row;
  });
}

function Bars({ spec, horizontal, locale }: { spec: ChartSpec; horizontal: boolean; locale: string }) {
  const labels = useLabels(spec);
  // a tall list of categories is a table, not a chart: the server already folds the tail into "Other"; this is the safety net
  const rows = useMemo(() => rowsOf(labels, spec.series).slice(0, MAX_ROWS), [labels, spec.series]);
  const f = fmt(spec.decimals, locale);
  const multi = spec.series.length > 1;

  const tooltip = (p: Record<string, unknown>) => spec.series.map((s, k) => ({ color: seriesColor(s, k), label: s.name, value: f(Number(p[`s${k}`] ?? 0)) }));
  const swatches = multi && <Swatches items={spec.series.map((s, k) => ({ color: seriesColor(s, k), label: s.name }))} />;

  if (horizontal) {
    const longest = Math.max(...labels.map(l => l.length));
    const left = Math.max(84, Math.min(200, Math.round(longest * 6.4) + 24));
    return (
      <>
        {swatches}
        <ChartBox>
          <BarChart data={rows} xDataKey="label" orientation="horizontal" stacked={spec.stacked} barGap={0.35}
                    aspectRatio={`640 / ${rows.length * 36 + 36}`} margin={{ top: 8, right: 36, bottom: 8, left }}>
            <Grid vertical horizontal={false} />
            {spec.series.map((s, k) => <Bar key={k} dataKey={`s${k}`} fill={seriesColor(s, k)} lineCap="round" showValue={!multi} valueLabelFormatter={f} />)}
            <BarYAxis labelMaxWidth={left - 14} />
            <ChartTooltip rows={tooltip} />
          </BarChart>
        </ChartBox>
      </>
    );
  }

  return (
    <>
      {swatches}
      <ChartBox>
        <BarChart data={rows} xDataKey="label" stacked={spec.stacked} aspectRatio="16 / 7" margin={{ top: 16, right: 12, bottom: 30, left: 44 }}>
          <Grid horizontal />
          {spec.series.map((s, k) => <Bar key={k} dataKey={`s${k}`} fill={seriesColor(s, k)} lineCap="round" showValue={!multi && rows.length <= 12} valueLabelFormatter={f} />)}
          <YAxis formatLargeNumbers />
          <BarXAxis />
          <ChartTooltip rows={tooltip} />
        </BarChart>
      </ChartBox>
    </>
  );
}

function GaugeView({ spec, locale }: { spec: ChartSpec; locale: string }) {
  const value = Math.max(0, Math.min(100, spec.value ?? 0));
  const color = value >= 80 ? C.approved : value >= 50 ? C.warning : C.returned;
  return (
    <ChartBox className="mx-auto max-w-64">
      <Gauge value={value} centerValue={value} activeFill={color} defaultLabel={spec.centerLabel ?? ""} suffix={spec.unit ?? "%"} minWidth={190}
             formatOptions={{ locales: locale, maximumFractionDigits: spec.decimals ?? 0 } as never} />
    </ChartBox>
  );
}

export function ChartIsland({ spec, locale }: { spec: ChartSpec; locale: string }) {
  if (isEmpty(spec)) return <Empty>{spec.emptyText}</Empty>;

  switch (spec.kind) {
    case "bar": return <Bars spec={spec} horizontal={false} locale={locale} />;
    case "hbar": return <Bars spec={spec} horizontal locale={locale} />;
    case "gauge": return <GaugeView spec={spec} locale={locale} />;
    case "donut": {
      const s = spec.series[0];
      const data = spec.labels.map((label, i) => ({ label, value: s.values[i] ?? 0, color: spec.colors?.[i] ?? CATEGORICAL[i % CATEGORICAL.length] }))
        .filter(d => d.value > 0);
      return <DonutWithLegend data={data} defaultLabel={spec.centerLabel ?? ""} />;
    }
  }
}
