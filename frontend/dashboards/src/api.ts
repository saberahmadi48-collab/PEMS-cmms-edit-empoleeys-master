import type { DashboardData } from "./types";

export async function fetchDashboard(url: string, projectId: number | null, signal?: AbortSignal): Promise<DashboardData> {
  const query = projectId ? `${url}${url.includes("?") ? "&" : "?"}projectId=${projectId}` : url;
  const res = await fetch(query, { credentials: "same-origin", headers: { Accept: "application/json" }, signal });
  if (!res.ok) throw new Error(String(res.status));
  return (await res.json()) as DashboardData;
}
