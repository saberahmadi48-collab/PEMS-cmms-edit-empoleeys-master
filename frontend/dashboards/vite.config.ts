import path from "node:path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// Bklit charts for PEMS pages: the executive dashboard app (ExecutiveDashboard/Index.cshtml) plus data-driven chart "islands"
// ([data-bk-chart], rendered by Shared/_BkChart.cshtml) on the Report and KPI pages.
// It builds to fixed file names inside the web project's wwwroot so the Razor page can reference them
// (asp-append-version adds the cache-busting hash), and the server does not need Node at runtime.
export default defineConfig({
  plugins: [react()],
  resolve: { alias: { "@": path.resolve(__dirname, "src") } },
  build: {
    outDir: path.resolve(__dirname, "../../PEMS.Web/wwwroot/dist/dashboards"),
    emptyOutDir: true,
    cssCodeSplit: false,
    sourcemap: false,
    rollupOptions: {
      input: path.resolve(__dirname, "src/main.tsx"),
      output: {
        entryFileNames: "dashboards.js",
        chunkFileNames: "dashboards-[name].js",
        assetFileNames: "dashboards[extname]",
      },
    },
  },
});
