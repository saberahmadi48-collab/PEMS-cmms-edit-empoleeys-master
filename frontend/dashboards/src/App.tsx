import { useCallback, useEffect, useRef, useState } from "react";
import { RefreshCw, TriangleAlert } from "lucide-react";
import { fetchDashboard } from "@/api";
import { isRtl, t, when } from "@/format";
import { DisciplineCard, MrCard, ProjectsCard, ResponsesCard, StatusCard, TimelineCard, VdrCard } from "@/sections/Charts";
import { KpiStrip } from "@/sections/KpiStrip";
import { AttentionCard, PressureCard, QualityCard, SideStats } from "@/sections/Panels";
import type { Boot, DashboardData } from "@/types";

export function App({ boot }: { boot: Boot }) {
  const [projectId, setProjectId] = useState<number | null>(null);
  const [data, setData] = useState<DashboardData | null>(null);
  const [projects, setProjects] = useState<DashboardData["projects"]>([]);
  const [error, setError] = useState(false);
  const [loading, setLoading] = useState(true);
  const abort = useRef<AbortController | null>(null);

  const load = useCallback((pid: number | null) => {
    abort.current?.abort();
    const controller = new AbortController();
    abort.current = controller;
    setLoading(true);
    setError(false);
    fetchDashboard(boot.dataUrl, pid, controller.signal)
      .then(d => {
        setData(d);
        // the project list is built from every project, so it is safe to refresh it from any response
        if (d.projects.length) setProjects(d.projects);
        setLoading(false);
      })
      .catch(e => { if ((e as Error).name !== "AbortError") { setError(true); setLoading(false); } });
  }, [boot.dataUrl]);

  useEffect(() => {
    load(projectId);
    return () => abort.current?.abort();
  }, [load, projectId]);

  const eng = data?.engineering ?? null;
  const unavailable = data?.failedSections ?? [];

  return (
    <div className="flex flex-col gap-4 text-foreground" dir={isRtl() ? "rtl" : "ltr"}>
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="m-0 text-xl font-bold leading-tight">{t("Exec_Title")}</h1>
          <p className="m-0 mt-1 text-xs text-muted-foreground">
            {t("Exec_Subtitle")}{data ? ` · ${t("Exec_Updated", { time: when(data.generatedAt) })}` : ""}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <label className="flex items-center gap-2 text-xs text-muted-foreground">
            <span>{t("Exec_Project")}</span>
            <select
              className="max-w-64 rounded-lg border border-border bg-card px-2.5 py-1.5 text-xs text-foreground"
              value={projectId ?? ""}
              onChange={e => setProjectId(e.target.value ? Number(e.target.value) : null)}
            >
              <option value="">{t("Exec_AllProjects")}</option>
              {projects.map(p => <option key={p.id} value={p.id}>{p.name} ({p.documents})</option>)}
            </select>
          </label>
          <button type="button" onClick={() => load(projectId)} disabled={loading}
                  className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-card px-2.5 py-1.5 text-xs text-foreground hover:bg-muted disabled:opacity-60">
            <RefreshCw className={`size-3.5 ${loading ? "animate-spin" : ""}`} aria-hidden="true" />{t("Exec_Refresh")}
          </button>
        </div>
      </header>

      {error && (
        <div role="alert" className="flex items-center gap-2 rounded-xl border border-border bg-card p-3 text-xs" style={{ color: "var(--pems-chart-danger-text, #b91c1c)" }}>
          <TriangleAlert className="size-4" aria-hidden="true" />{t("Exec_LoadError")}
        </div>
      )}

      {unavailable.length > 0 && (
        <div role="status" className="rounded-xl border border-border bg-card p-3 text-xs text-muted-foreground">
          {t("Exec_SectionsFailed", { sections: unavailable.join(", ") })}
        </div>
      )}

      {!data && loading && <div className="rounded-xl border border-border bg-card p-8 text-center text-xs text-muted-foreground">{t("Exec_Loading")}</div>}

      {data && (
        <div className={`flex flex-col gap-4 transition-opacity ${loading ? "opacity-60" : ""}`} aria-busy={loading}>
          <KpiStrip data={data} />

          {eng && eng.total > 0 ? (
            <>
              <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
                <div className="lg:col-span-2"><TimelineCard eng={eng} /></div>
                <StatusCard eng={eng} />
              </div>

              <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
                <DisciplineCard eng={eng} />
                <div className="flex min-w-0 flex-col gap-4">
                  {eng.projects.length > 0 && <ProjectsCard rows={eng.projects} />}
                  <ResponsesCard items={eng.clientResponses} />
                </div>
              </div>

              <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
                {data.vdr && data.vdr.total > 0 && <VdrCard vdr={data.vdr} />}
                {data.procurement && data.procurement.materialRequisitions + data.procurement.purchaseOrders > 0 && <MrCard procurement={data.procurement} />}
                <PressureCard rows={eng.revisionPressure} />
              </div>

              <div className="grid grid-cols-1 gap-4 lg:grid-cols-4">
                <div className="lg:col-span-2"><AttentionCard items={data.attention} /></div>
                <QualityCard completeness={eng.completeness} />
                <SideStats data={data} />
              </div>
            </>
          ) : (
            <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
              <AttentionCard items={data.attention} />
              {data.vdr && data.vdr.total > 0 && <VdrCard vdr={data.vdr} />}
              {data.procurement && <MrCard procurement={data.procurement} />}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
