import type { ReactNode } from "react";
import { ArrowUpRight, FileText, Gauge as GaugeIcon, Hourglass, Layers, Package, RotateCcw, Wrench } from "lucide-react";
import { AreaSparkline } from "@/components/charts/sparkline";
import { cn } from "@/lib/utils";
import { BUCKET_COLOR, C } from "@/colors";
import { compactNum, linkFor, num, percent, t } from "@/format";
import { ChartBox, ProgressBar } from "@/ui/primitives";
import type { DashboardData } from "@/types";

interface Tile {
  id: string;
  label: string;
  value: string;
  sub?: ReactNode;
  icon: ReactNode;
  link?: string;
  tone?: "default" | "good" | "warn" | "bad";
  visual?: ReactNode;
}

const TONE: Record<NonNullable<Tile["tone"]>, string> = {
  default: "var(--pems-text, inherit)",
  good: "var(--pems-chart-positive-text, #15803d)",
  warn: "var(--pems-chart-warning-text, #b45309)",
  bad: "var(--pems-chart-danger-text, #b91c1c)",
};

function Kpi({ tile }: { tile: Tile }) {
  const body = (
    <div className="flex h-full min-h-[8.5rem] flex-col gap-2 rounded-xl border border-border bg-card p-4 transition-colors hover:bg-muted">
      <div className="flex items-center justify-between gap-2 text-muted-foreground">
        <span className="flex min-w-0 items-center gap-2 text-xs font-medium">
          <span className="grid size-6 shrink-0 place-items-center rounded-md bg-muted [&>svg]:size-3.5" aria-hidden="true">{tile.icon}</span>
          <span className="line-clamp-2 leading-tight">{tile.label}</span>
        </span>
        {tile.link && <ArrowUpRight className="size-3.5 shrink-0 opacity-60 rtl:-scale-x-100" aria-hidden="true" />}
      </div>
      <div className="text-2xl font-bold leading-none tabular-nums" style={{ color: TONE[tile.tone ?? "default"] }}>{tile.value}</div>
      {tile.sub && <div className="text-xs leading-snug text-muted-foreground">{tile.sub}</div>}
      {tile.visual && <div className="mt-auto pt-1">{tile.visual}</div>}
    </div>
  );
  return tile.link
    ? <a href={tile.link} className={cn("block h-full text-inherit no-underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring rounded-xl")}>{body}</a>
    : body;
}

export function KpiStrip({ data }: { data: DashboardData }) {
  const e = data.engineering;
  const tiles: Tile[] = [];

  if (e) {
    // the stacked mini-bar shows the whole portfolio at a glance: approved / in review / returned / draft
    const split = (
      <div className="flex h-1.5 w-full overflow-hidden rounded-full bg-muted" role="img"
           aria-label={e.buckets.map(b => `${t(`Exec_Bucket_${b.key}`)}: ${num(b.value)}`).join(", ")}>
        {e.buckets.map(b => <div key={b.key} style={{ width: `${(100 * b.value) / e.total}%`, background: BUCKET_COLOR[b.key] }} />)}
      </div>
    );
    tiles.push({ id: "docs", label: t("Exec_KpiDocuments"), value: num(e.total), icon: <FileText />, link: linkFor("Engineering"),
      sub: t("Exec_KpiDocumentsSub", { projects: num(e.projectCount) }), visual: split });

    tiles.push({ id: "approved", label: t("Exec_KpiApproved"), value: percent(e.approvedPercent), icon: <GaugeIcon />,
      tone: e.approvedPercent >= 60 ? "good" : e.approvedPercent >= 30 ? "warn" : "bad",
      sub: t("Exec_KpiApprovedSub", { approved: num(e.approved), total: num(e.total) }),
      visual: <ProgressBar value={e.approvedPercent} color={C.approved} label={t("Exec_KpiApproved")} /> });

    tiles.push({ id: "review", label: t("Exec_KpiInReview"), value: num(e.inReview), icon: <Hourglass />, link: linkFor("EngInReview"),
      sub: t("Exec_KpiInReviewSub", { draft: num(e.draft) }) });

    const rework = e.rejectedTrue + e.returnedWithComments;
    tiles.push({ id: "rework", label: t("Exec_KpiRework"), value: num(rework), icon: <RotateCcw />, link: linkFor("EngRejected"), tone: rework > 0 ? "bad" : "good",
      sub: t("Exec_KpiReworkSub", { rejected: num(e.rejectedTrue), commented: num(e.returnedWithComments) }) });

    // cumulative reads as growth; the monthly series is spiky (most revisions were issued in the early years)
    const series = e.issuance.map(p => ({ date: new Date(p.date), value: p.cumulative }));
    tiles.push({ id: "revisions", label: t("Exec_KpiRevisions"), value: num(e.totalRevisions), icon: <Layers />,
      sub: t("Exec_KpiRevisionsSub", { avg: num(e.revisionsPerDocument) }),
      visual: series.length > 2 ? (
        <ChartBox><AreaSparkline data={series} dataKey="value" height={30} animate={false} color={C.inReview} aria-label={t("Exec_KpiRevisions")} /></ChartBox>
      ) : undefined });
  }

  if (data.procurement) {
    const p = data.procurement;
    const money = p.purchaseOrderValue.filter(v => v.value > 0).map(v => `${compactNum(v.value)} ${v.currency}`).join(" · ");
    tiles.push({ id: "procurement", label: t("Exec_KpiProcurement"), value: num(p.purchaseOrders), icon: <Package />, link: linkFor("Procurement"),
      sub: money
        ? t("Exec_KpiProcurementValue", { value: money, mr: num(p.openMaterialRequisitions) })
        : t("Exec_KpiProcurementSub", { mr: num(p.openMaterialRequisitions) }) });
  }

  if (data.operations) {
    const o = data.operations;
    tiles.push({ id: "operations", label: t("Exec_KpiEquipment"), value: num(o.equipment), icon: <Wrench />, link: linkFor("Cmms"),
      tone: o.criticalWorkOrders > 0 ? "warn" : "default",
      sub: t("Exec_KpiEquipmentSub", { open: num(o.openWorkOrders), critical: num(o.criticalWorkOrders) }) });
  }

  if (tiles.length === 0) return null;
  // literal class names so Tailwind can see them
  const cols = ({ 1: "xl:grid-cols-1", 2: "xl:grid-cols-2", 3: "xl:grid-cols-3", 4: "xl:grid-cols-4", 5: "xl:grid-cols-5", 6: "xl:grid-cols-6" } as Record<number, string>)[tiles.length] ?? "xl:grid-cols-7";
  return (
    <div className={cn("grid grid-cols-2 gap-3 md:grid-cols-4", cols)} role="list">
      {tiles.map(tile => <div role="listitem" key={tile.id} className="min-w-0"><Kpi tile={tile} /></div>)}
    </div>
  );
}
