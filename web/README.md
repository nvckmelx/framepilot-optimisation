# FramePilot web

Static site for Hostinger: landing page and a report viewer (`#/report`) that renders a FramePilot JSON report as a bento dashboard. The report file is read in the browser and never uploaded.

Stack: React 19, TypeScript, Tailwind CSS v4, shadcn project layout (`src/components/ui`, `src/lib/utils.ts`, `components.json`), esbuild. No server runtime.

```bash
cd web
npm install
npm run build      # → dist/
npm run dev        # rebuild on change; serve dist/ with any static server
npm run typecheck
```

Main pieces:
- `src/components/ui/performance-bento.tsx` — the dashboard (frame-time stream, before/after verdict, KPI tiles, distribution, run ranges, runs, recommendations, system, changes).
- `src/components/ui/frame-trace.tsx` — hero frame-time trace (synthetic, labelled).
- `src/components/ui/dot-number.tsx` — dot-matrix readout.
- `src/lib/report.ts` — report types and parsing (`framepilot.report/1`).
- `src/lib/demo.ts` — clearly labelled demo report.

## Deploying to Hostinger
Every push to `main` that touches `web/` runs `.github/workflows/web.yml`, which builds the site and force-pushes `dist/` to the `hostinger-site` branch.

One-time setup in hPanel:
1. Websites → Manage → Advanced → **GIT**.
2. Repository: `https://github.com/nvckmelx/framepilot-optimisation.git`, branch `hostinger-site`, install path empty (`public_html` must be empty first).
3. Create, then **Deploy**.
4. Optional auto-deploy: copy the webhook URL from the same page and add it in GitHub → Settings → Webhooks (content type `application/json`, push events).

## Brand
Visual identity follows `docs/brand/FRAMEPILOT_BRAND_MASTER.md` (Frame F / minimal): canonical SVG geometry in `src/components/ui/logo.tsx`, Arial, Ink / Paper / Accent palette, 4 px controls and 8 px cards, 180/280 ms motion with `cubic-bezier(.2,.8,.2,1)`.
