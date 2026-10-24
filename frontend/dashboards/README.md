# PEMS dashboards (React + Bklit UI)

One small React bundle (`PEMS.Web/wwwroot/dist/dashboards/dashboards.{js,css}`) that serves two things:

1. **Executive Dashboard** (`/ExecutiveDashboard`): a React app mounted by `Views/ExecutiveDashboard/Index.cshtml`, fed by
   `ExecutiveDashboardController.Data` (real data from `IExecutiveDashboardService`).
2. **Chart islands** on ordinary Razor pages (the Report pages and the Maintenance & Cost KPI page). A page describes a chart
   with `BkChartSpec` (`PEMS.Web/Models`) and renders `<partial name="_BkChart" model="@spec" />`; the bundle finds every
   `[data-bk-chart]` element and draws it with Bklit components (`src/islands`). Kinds: `bar` (monthly or categorical),
   `hbar`, `donut`, `gauge`. The partial also emits a visually-hidden data table (accessible / no-JS copy of the numbers).

```cshtml
@{ var spec = BkChartSpec.MonthlyBars("Letters by month", Localizer["Chart_NoData"].Value, months, BkSeries.Of("Incoming", counts)); }
<partial name="_BkChart" model="@spec" />
@section Styles  { <partial name="_BkChartStyles" /> }
@section Scripts { <partial name="_BkChartScripts" /> }
```

Long category lists should go through `BkChartSpec.TopWithOthers(...)` (largest N + one "Other" row, totals preserved).

Charts are [Bklit UI](https://bklit.com) components copied into `src/components/charts` (the source is ours to edit).

## Build

```bash
cd frontend/dashboards
npm install
npm run build        # type-checks, then writes PEMS.Web/wwwroot/dist/dashboards/dashboards.{js,css}
```

The built files are **committed**, so building/deploying PEMS.Web (including Docker) needs no Node. Rebuild and commit them
whenever anything under `src/` changes. `npm run typecheck` is the quick check.

## How it fits the PEMS page

* **CSS is scoped.** `postcss.config.mjs` prefixes every Tailwind rule with `.bk-scope` (a class on the executive root and on every chart
  island) and emits utilities unlayered, so nothing leaks into the Bootstrap page and Bootstrap's reboot cannot override the charts.
* **Themes.** `src/index.css` maps the shadcn/Bklit tokens (`--background`, `--chart-1..5` ...) onto the PEMS tokens
  (`--pems-*`), so light / dark / turquoise restyle every chart automatically.
* **Texts.** Executive dashboard strings are the `Exec_*` keys in `SharedResource(.fa).resx` (shipped to the page, read with
  `t("Exec_...")`). Chart islands get their captions from the server (`aria-label`, empty text) and months are formatted in the page
  language (Jalali calendar for Persian).
* **Direction.** Charts are always laid out left-to-right (time/magnitude); text follows the page direction.

## Changes made to the copied Bklit source

* `chart-formatters.ts`: formatters became live bindings plus `configureChartFormatting(locale, monthly)` (monthly labels,
  Jalali calendar for Persian).
* `bar-y-axis.tsx`: new `labelMaxWidth` prop (the registry hard-codes 70px, too narrow for discipline/project names).

The shadcn CLI cannot reach `ui.shadcn.com` from some networks; items can be installed straight from
`https://bklit.com/r/<item>.json` (resolve `registryDependencies`, write each file to `src/components/charts/`).
