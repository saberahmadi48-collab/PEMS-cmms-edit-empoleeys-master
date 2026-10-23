import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { configureChartFormatting } from "@/components/charts/chart-formatters";
import { initFormat, locale } from "@/format";
import { ChartIsland } from "./ChartIsland";
import type { ChartSpec } from "./spec";

/** Hydrates every `[data-bk-chart]` element (rendered by Views/Shared/_BkChart.cshtml) with a Bklit chart. Safe to call twice. */
export function mountIslands(root: ParentNode = document) {
  const targets = Array.from(root.querySelectorAll<HTMLElement>("[data-bk-chart]:not([data-bk-mounted])"));
  if (targets.length === 0) return;

  // islands live on ordinary PEMS pages: language/direction come from <html>, not from a boot block
  const html = document.documentElement;
  const lang = html.lang || "en";
  initFormat({ dataUrl: "", links: {}, i18n: {}, locale: lang, rtl: html.dir === "rtl" });
  configureChartFormatting(lang, true);

  for (const el of targets) {
    el.setAttribute("data-bk-mounted", "1");
    try {
      const spec = JSON.parse(el.getAttribute("data-bk-chart") || "{}") as ChartSpec;
      createRoot(el).render(<StrictMode><ChartIsland spec={spec} locale={locale()} /></StrictMode>);
    } catch {
      el.textContent = el.getAttribute("aria-label") ?? "";
    }
  }
}
