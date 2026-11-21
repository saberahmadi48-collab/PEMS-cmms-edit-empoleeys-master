import { useMemo, useState } from "react";
import { AreaChart, Area } from "@/components/charts/area-chart";
import { BarChart } from "@/components/charts/bar-chart";
import { Bar } from "@/components/charts/bar";
import { BarXAxis } from "@/components/charts/bar-x-axis";
import { BarYAxis } from "@/components/charts/bar-y-axis";
import { Grid } from "@/components/charts/grid";
import { Legend, LegendItem, LegendLabel, LegendMarker, LegendValue } from "@/components/charts/legend";
import { PieCenter } from "@/components/charts/pie-center";
import { PieChart } from "@/components/charts/pie-chart";
import { PieSlice } from "@/components/charts/pie-slice";
import { ChartTooltip } from "@/components/charts/tooltip";
import { XAxis } from "@/components/charts/x-axis";
import { YAxis } from "@/components/charts/y-axis";
import { BUCKET_COLOR, C, CATEGORICAL } from "@/colors";
import { locale, month, num, t } from "@/format";
import { Card, ChartBox, Empty } from "@/ui/primitives";
import type { Bucket, Count, DisciplineRow, Engineering, ProjectRow } from "@/types";

// created per call: the locale is only known after initFormat() ran at start-up
const oneDecimal = (n: number) => new Intl.NumberFormat(locale(), { maximumFractionDigits: 1 }).format(n);

/** Donut + hover-linked legend (the pattern from the requested TrafficSources example). */
export function DonutWithLegend({ data, defaultLabel }: { data: { label: string; value: number; color: string }[]; defaultLabel: string }) {
  const [hovered, setHovered] = useState<number | null>(null);
  const total = data.reduce((sum, d) => sum + d.value, 0);
  const legendItems = data.map(d => ({ ...d, maxValue: total }));   // maxValue is what turns the legend value into a percentage
  // container queries (not viewport breakpoints): the same donut sits in a wide card (ring beside legend) and in a narrow one (stacked)
  return (
    <div className="@container w-full">
      <div className="mx-auto flex w-full max-w-xl flex-col items-center gap-4 @sm:flex-row @sm:justify-center @sm:gap-8">
        <ChartBox className="aspect-square w-40 shrink-0 @sm:w-52">
          <PieChart data={data} hoveredIndex={hovered} innerRadius={50} onHoverChange={setHovered}>
            {data.map((item, i) => <PieSlice hoverEffect="grow" index={i} key={item.label} />)}
            <PieCenter defaultLabel={defaultLabel} />
          </PieChart>
        </ChartBox>
        <Legend className="w-full min-w-0 @sm:w-auto @sm:flex-1" hoveredIndex={hovered} items={legendItems} onHoverChange={setHovered}>
          <LegendItem className="flex items-center gap-2">
            <LegendMarker />
            <LegendLabel className="flex-1 text-xs" />
            <LegendValue className="text-xs" showPercentage formatValue={oneDecimal} />
          </LegendItem>
        </Legend>
      </div>
    </div>
  );
}

export function StatusCard({ eng }: { eng: Engineering }) {
  const data = eng.buckets.map((b: Bucket) => ({ label: t(`Exec_Bucket_${b.key}`), value: b.value, color: BUCKET_COLOR[b.key] }));
  return (
    <Card title={t("Exec_StatusTitle")} subtitle={t("Exec_StatusSub", { total: num(eng.total) })}>
      {data.length ? <DonutWithLegend data={data} defaultLabel={t("Exec_Documents")} /> : <Empty>{t("Exec_NoData")}</Empty>}
    </Card>
  );
}

export function TimelineCard({ eng }: { eng: Engineering }) {
  const rows = useMemo(() => eng.issuance.map(p => ({ date: new Date(p.date), cumulative: p.cumulative, issued: p.issued })), [eng.issuance]);
  const first = eng.issuance[0], last = eng.issuance[eng.issuance.length - 1];
  return (
    <Card title={t("Exec_TimelineTitle")}
          subtitle={first && last ? t("Exec_TimelineSub", { from: month(first.date), to: month(last.date), total: num(last.cumulative) }) : undefined}>
      {rows.length < 2 ? <Empty>{t("Exec_NoData")}</Empty> : (
        <ChartBox>
          <AreaChart data={rows} aspectRatio="16 / 6" margin={{ top: 16, right: 16, bottom: 28, left: 44 }}>
            <Grid horizontal />
            <Area dataKey="cumulative" stroke={C.inReview} fill={C.inReview} />
            <XAxis />
            <YAxis formatLargeNumbers />
            <ChartTooltip rows={p => [
              { color: C.inReview, label: t("Exec_Cumulative"), value: num(p.cumulative as number) },
              { color: C.other, label: t("Exec_IssuedInMonth"), value: num(p.issued as number) },
            ]} />
          </AreaChart>
        </ChartBox>
      )}
    </Card>
  );
}

export function Swatches({ items }: { items: { color: string; label: string }[] }) {
  return (
    <ul className="m-0 mb-3 flex list-none flex-wrap gap-x-4 gap-y-1 p-0 text-xs text-muted-foreground">
      {items.map(i => (
        <li key={i.label} className="flex items-center gap-1.5">
          <span aria-hidden="true" className="inline-block size-2.5 rounded-[3px]" style={{ background: i.color }} />{i.label}
        </li>
      ))}
    </ul>
  );
}

function stackedRows(rows: DisciplineRow[]) {
  return rows.map(r => ({
    name: r.name || t("Exec_Unassigned"),
    approved: r.approved, inReview: r.inReview, returned: r.returned, draft: r.draft,
    total: r.total, percent: r.approvedPercent,
  }));
}

export function DisciplineCard({ eng }: { eng: Engineering }) {
  const data = useMemo(() => stackedRows(eng.disciplines), [eng.disciplines]);
  return (
    <Card title={t("Exec_DisciplineTitle")} subtitle={t("Exec_DisciplineSub")}>
      {data.length === 0 ? <Empty>{t("Exec_NoData")}</Empty> : (
        <>
          <Swatches items={(["Approved", "InReview", "Returned", "Draft"] as const).map(k => ({ color: BUCKET_COLOR[k], label: t(`Exec_Bucket_${k}`) }))} />
          <ChartBox>
            <BarChart data={data} xDataKey="name" orientation="horizontal" stacked aspectRatio="5 / 4.3" barGap={0.35}
                      margin={{ top: 8, right: 24, bottom: 8, left: 150 }}>
              <Grid vertical horizontal={false} />
              <Bar dataKey="approved" fill={C.approved} />
              <Bar dataKey="inReview" fill={C.inReview} />
              <Bar dataKey="returned" fill={C.returned} />
              <Bar dataKey="draft" fill={C.draft} />
              <BarYAxis labelMaxWidth={138} />
              <ChartTooltip rows={p => [
                { color: C.approved, label: t("Exec_Bucket_Approved"), value: num(p.approved as number) },
                { color: C.inReview, label: t("Exec_Bucket_InReview"), value: num(p.inReview as number) },
                { color: C.returned, label: t("Exec_Bucket_Returned"), value: num(p.returned as number) },
                { color: C.draft, label: t("Exec_Bucket_Draft"), value: num(p.draft as number) },
              ]} />
            </BarChart>
          </ChartBox>
        </>
      )}
    </Card>
  );
}

export function ResponsesCard({ items }: { items: Count[] }) {
  const data = useMemo(() => items.map(i => ({ code: i.label, count: i.value })), [items]);
  return (
    <Card title={t("Exec_ResponsesTitle")} subtitle={t("Exec_ResponsesSub")}>
      {data.length === 0 ? <Empty>{t("Exec_NoData")}</Empty> : (
        <ChartBox>
          <BarChart data={data} xDataKey="code" aspectRatio="5 / 2.3" margin={{ top: 16, right: 12, bottom: 28, left: 44 }}>
            <Grid horizontal />
            <Bar dataKey="count" fill={C.other} lineCap="round" showValue valueLabelFormatter={num} />
            <BarXAxis />
            <ChartTooltip rows={p => [{ color: C.other, label: t("Exec_Documents"), value: num(p.count as number) }]} />
          </BarChart>
        </ChartBox>
      )}
    </Card>
  );
}

export function ProjectsCard({ rows }: { rows: ProjectRow[] }) {
  const data = useMemo(() => {
    const dup = new Set(rows.map(r => r.name).filter((n, i, all) => all.indexOf(n) !== i));
    return rows.map(r => ({
      // a band scale needs unique categories; projects that share a name get their id appended
      name: r.projectId === 0 ? t("Exec_OtherProjects", { count: num(r.othersCount) }) : dup.has(r.name) ? `${r.name} #${r.projectId}` : r.name,
      approved: r.approved, rest: r.total - r.approved, total: r.total,
    }));
  }, [rows]);
  return (
    <Card title={t("Exec_ProjectsTitle")} subtitle={t("Exec_ProjectsSub")}>
      {data.length === 0 ? <Empty>{t("Exec_NoData")}</Empty> : (
        <>
          <Swatches items={[{ color: C.approved, label: t("Exec_Bucket_Approved") }, { color: C.draft, label: t("Exec_NotApproved") }]} />
          <ChartBox>
            <BarChart data={data} xDataKey="name" orientation="horizontal" stacked aspectRatio="5 / 2.6" barGap={0.4}
                      margin={{ top: 8, right: 24, bottom: 8, left: 170 }}>
              <Grid vertical horizontal={false} />
              <Bar dataKey="approved" fill={C.approved} />
              <Bar dataKey="rest" fill={C.draft} />
              <BarYAxis labelMaxWidth={158} />
              <ChartTooltip rows={p => [
                { color: C.approved, label: t("Exec_Bucket_Approved"), value: num(p.approved as number) },
                { color: C.draft, label: t("Exec_NotApproved"), value: num(p.rest as number) },
              ]} />
            </BarChart>
          </ChartBox>
        </>
      )}
    </Card>
  );
}

export function VdrCard({ vdr }: { vdr: NonNullable<import("@/types").DashboardData["vdr"]> }) {
  const data = vdr.byWorkflow.map((w, i) => ({ label: t(`Exec_Wf_${w.label}`, undefined, w.label), value: w.value, color: CATEGORICAL[i % CATEGORICAL.length] }));
  return (
    <Card title={t("Exec_VdrTitle")} subtitle={t("Exec_VdrSub", { total: num(vdr.total), submissions: num(vdr.submissions) })}>
      {data.length === 0 ? <Empty>{t("Exec_NoData")}</Empty> : <DonutWithLegend data={data} defaultLabel={t("Exec_Items")} />}
    </Card>
  );
}

export function MrCard({ procurement }: { procurement: NonNullable<import("@/types").DashboardData["procurement"]> }) {
  const data = procurement.mrByStatus.map(m => ({ name: t(`Exec_Mr_${m.label}`, undefined, m.label), count: m.value }));
  return (
    <Card title={t("Exec_ProcurementTitle")} subtitle={t("Exec_ProcurementSub")}>
      <dl className="m-0 mb-3 grid grid-cols-3 gap-2 text-center">
        {[
          [t("Exec_Packages"), procurement.packages], [t("Exec_Tenders"), procurement.tenders], [t("Exec_Contracts"), procurement.contracts],
          [t("Exec_PurchaseOrders"), procurement.purchaseOrders], [t("Exec_Bids"), procurement.bids], [t("Exec_Ntps"), procurement.ntpsIssued],
        ].map(([label, value]) => (
          <div key={String(label)} className="rounded-lg bg-muted px-2 py-2">
            <dd className="m-0 text-base font-bold tabular-nums">{num(value as number)}</dd>
            <dt className="text-[11px] font-normal text-muted-foreground">{label}</dt>
          </div>
        ))}
      </dl>
      {data.length === 0 ? <Empty>{t("Exec_NoData")}</Empty> : (
        <ChartBox>
          <BarChart data={data} xDataKey="name" orientation="horizontal" aspectRatio="5 / 2" barGap={0.4} margin={{ top: 4, right: 24, bottom: 4, left: 90 }}>
            <Grid vertical horizontal={false} />
            <Bar dataKey="count" fill={C.warning} lineCap="round" />
            <BarYAxis labelMaxWidth={78} />
            <ChartTooltip rows={p => [{ color: C.warning, label: t("Exec_MaterialRequisitions"), value: num(p.count as number) }]} />
          </BarChart>
        </ChartBox>
      )}
    </Card>
  );
}

