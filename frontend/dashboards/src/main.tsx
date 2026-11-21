import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "@/App";
import { configureChartFormatting } from "@/components/charts/chart-formatters";
import { initFormat } from "@/format";
import type { Boot } from "@/types";
import { mountIslands } from "@/islands/mount";
import "./index.css";

const bootEl = document.getElementById("exec-boot");
const host = document.getElementById("executive-root");

if (bootEl && host) {
  const boot = JSON.parse(bootEl.textContent || "{}") as Boot;
  initFormat(boot);
  configureChartFormatting(boot.locale || "en", true);
  createRoot(host).render(<StrictMode><App boot={boot} /></StrictMode>);
}

// chart islands on Report / KPI pages (no-op when the page has none)
// wait for the stylesheet: charts measure their container on mount
if (document.readyState === "complete") mountIslands();
else window.addEventListener("load", () => mountIslands());
