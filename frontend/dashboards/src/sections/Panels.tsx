import { AlertTriangle, ArrowUpRight, CircleAlert, Info } from "lucide-react";
import { SEVERITY_COLOR } from "@/colors";
import { linkFor, num, percent, t } from "@/format";
import { Card, Empty, ProgressBar } from "@/ui/primitives";
import type { Attention, Completeness, DashboardData, RevisionPressure } from "@/types";

const SEVERITY_ICON = { High: CircleAlert, Medium: AlertTriangle, Info } as const;

export function AttentionCard({ items }: { items: Attention[] }) {
  return (
    <Card title={t("Exec_AttentionTitle")} subtitle={t("Exec_AttentionSub")} className="h-full">
      {items.length === 0 ? <Empty>{t("Exec_AttentionNone")}</Empty> : (
        <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
          {items.map(item => {
            const Icon = SEVERITY_ICON[item.severity];
            const href = linkFor(item.key) ?? linkFor(item.module);
            const inner = (
              <>
                <Icon className="size-4 shrink-0" style={{ color: SEVERITY_COLOR[item.severity] }} aria-hidden="true" />
                <span className="min-w-0 flex-1 text-xs leading-snug">{t(`Exec_Attn_${item.key}`, { count: num(item.count) })}</span>
                <span className="shrink-0 text-sm font-bold tabular-nums" style={{ color: SEVERITY_COLOR[item.severity] }}>{num(item.count)}</span>
                {href && <ArrowUpRight className="size-3.5 shrink-0 opacity-50 rtl:-scale-x-100" aria-hidden="true" />}
              </>
            );
            const cls = "flex items-center gap-2.5 rounded-lg border border-border px-3 py-2 text-inherit no-underline";
            return (
              <li key={item.key}>
                {href
                  ? <a href={href} className={`${cls} hover:bg-muted focus-visible:outline-2 focus-visible:outline-ring`}>{inner}</a>
                  : <div className={cls}>{inner}</div>}
              </li>
            );
          })}
        </ul>
      )}
    </Card>
  );
}

export function PressureCard({ rows }: { rows: RevisionPressure[] }) {
  const max = Math.max(1, ...rows.map(r => r.revisions));
  return (
    <Card title={t("Exec_PressureTitle")} subtitle={t("Exec_PressureSub")} className="h-full">
      {rows.length === 0 ? <Empty>{t("Exec_NoData")}</Empty> : (
        <ol className="m-0 flex list-none flex-col gap-2 p-0">
          {rows.map(r => (
            <li key={r.documentId}>
              <div className="flex items-baseline justify-between gap-3">
                <span className="min-w-0 truncate text-xs font-medium" dir="ltr" title={`${r.documentNo} — ${r.title}`}>{r.documentNo}</span>
                <span className="shrink-0 text-xs font-bold tabular-nums">{num(r.revisions)}</span>
              </div>
              <ProgressBar value={(100 * r.revisions) / max} color="var(--pems-chart-secondary, #a855f7)" className="mt-1" label={r.documentNo} />
            </li>
          ))}
        </ol>
      )}
    </Card>
  );
}

export function QualityCard({ completeness }: { completeness: Completeness }) {
  const rows: [string, number][] = [
    [t("Exec_QDiscipline"), completeness.withDisciplinePercent],
    [t("Exec_QIssued"), completeness.withIssuedRevisionPercent],
    [t("Exec_QResponse"), completeness.withClientResponsePercent],
    [t("Exec_QPlanned"), completeness.withPlannedDatePercent],
  ];
  return (
    <Card title={t("Exec_QualityTitle")} subtitle={t("Exec_QualitySub")} className="h-full">
      <ul className="m-0 flex list-none flex-col gap-3 p-0">
        {rows.map(([label, value]) => (
          <li key={label}>
            <div className="mb-1 flex items-baseline justify-between gap-2 text-xs">
              <span>{label}</span>
              <span className="font-bold tabular-nums">{percent(value)}</span>
            </div>
            <ProgressBar value={value} label={label}
              color={value >= 80 ? "var(--pems-chart-positive, #22c55e)" : value >= 40 ? "var(--pems-chart-warning, #f59e0b)" : "var(--pems-chart-negative, #ef4444)"} />
          </li>
        ))}
      </ul>
      {completeness.withPlannedDatePercent === 0 && (
        <p className="m-0 mt-3 flex gap-2 rounded-lg bg-muted p-2.5 text-xs leading-snug text-muted-foreground">
          <Info className="mt-0.5 size-3.5 shrink-0" aria-hidden="true" />{t("Exec_QPlannedNote")}
        </p>
      )}
    </Card>
  );
}

export function SideStats({ data }: { data: DashboardData }) {
  const rows: [string, string][] = [];
  if (data.transmittals) rows.push([t("Exec_Transmittals"), num(data.transmittals.sent)]);
  if (data.correspondence) {
    rows.push([t("Exec_Letters"), num(data.correspondence.total)]);
    rows.push([t("Exec_ResponsesOverdue"), num(data.correspondence.overdueResponses + data.correspondence.overdueReferrals)]);
  }
  if (data.operations) rows.push([t("Exec_OpenWorkOrders"), num(data.operations.openWorkOrders)]);
  if (data.vdr) rows.push([t("Exec_VdrOverdue"), num(data.vdr.overdue)]);
  if (rows.length === 0) return null;
  return (
    <Card title={t("Exec_OtherTitle")} className="h-full">
      <dl className="m-0 flex flex-col divide-y divide-border">
        {rows.map(([label, value]) => (
          <div key={label} className="flex items-baseline justify-between gap-3 py-2 first:pt-0 last:pb-0">
            <dt className="text-xs font-normal text-muted-foreground">{label}</dt>
            <dd className="m-0 text-sm font-bold tabular-nums">{value}</dd>
          </div>
        ))}
      </dl>
    </Card>
  );
}
