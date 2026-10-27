import * as React from "react";
import { Button } from "@/components/ui/button";
import { PerformanceBento } from "@/components/ui/performance-bento";
import { FileIcon, LockIcon, PlayIcon, XIcon } from "@/components/ui/icons";
import { useLang } from "@/lib/i18n";
import { demoReport } from "@/lib/demo";
import { parseReport, ReportError, type DiagnosticReport } from "@/lib/report";
import { cn } from "@/lib/utils";

export function ReportViewer() {
  const { t, lang } = useLang();
  const [report, setReport] = React.useState<DiagnosticReport | null>(null);
  const [error, setError] = React.useState<string | null>(null);
  const [drag, setDrag] = React.useState(false);
  const input = React.useRef<HTMLInputElement>(null);

  const load = async (file: File | undefined) => {
    if (!file) return;
    setError(null);
    try {
      const text = await file.text();
      setReport(parseReport(text));
      window.scrollTo({ top: 0 });
    } catch (e) {
      const code = e instanceof ReportError ? e.message : "not-json";
      setError(code === "schema" ? t.v_err_schema : code === "not-framepilot" ? t.v_err_not_framepilot : t.v_err_not_json);
    }
  };

  if (report) {
    return (
      <main id="main" className="bg-sunken text-fg"><div className="wrap pt-10 pb-20">
        <div className="mb-6 flex flex-wrap items-center gap-x-4 gap-y-3">
          <h1 className="flex-1 text-[clamp(28px,4vw,40px)]">
            {t.v_title}
            <span className="mt-1 block text-[14px] font-normal tracking-normal text-muted sm:ml-3 sm:mt-0 sm:inline">
              {t.v_generated} {new Date(report.generatedAt).toLocaleString(lang === "ru" ? "ru-RU" : "en-US", { dateStyle: "medium", timeStyle: "short" })} · v{report.appVersion}
            </span>
          </h1>
          <Button variant="outline" size="sm" onClick={() => { setReport(null); setError(null); }}>{t.v_close}</Button>
        </div>
        {report.isDemo && <p className="label mb-4 inline-block rounded-[4px] border border-dashed border-warn px-2.5 py-1 text-warn">{t.v_demo_badge}</p>}
        <PerformanceBento key={report.generatedAt + report.sessions.length} report={report} />
      </div></main>
    );
  }

  return (
    <main id="main" className="wrap max-w-[960px] pt-16 pb-24">
      <h1 className="text-[clamp(36px,5vw,56px)]">{t.v_title}</h1>
      <p className="mt-4 text-[18px] leading-[1.6] text-muted">{t.v_how}</p>
      <div
        onDragOver={(e) => { e.preventDefault(); setDrag(true); }}
        onDragLeave={() => setDrag(false)}
        onDrop={(e) => { e.preventDefault(); setDrag(false); load(e.dataTransfer.files[0]); }}
        className={cn("mt-10 grid place-items-center rounded-[8px] border-2 border-dashed px-6 py-16 text-center transition-colors duration-[180ms]",
          drag ? "border-line-strong bg-link/25" : "border-line")}>
        <FileIcon width={36} height={36} />
        <p className="mt-4 text-[20px] font-bold">{t.v_drop}</p>
        <p className="mt-1 text-[15px] text-muted">{t.v_drop_or}</p>
        <div className="mt-4 flex flex-wrap justify-center gap-3">
          <Button onClick={() => input.current?.click()}><FileIcon />{t.v_pick}</Button>
          <Button variant="outline" onClick={() => setReport(demoReport())}><PlayIcon />{t.v_demo}</Button>
        </div>
        <input ref={input} type="file" accept="application/json,.json" className="sr-only" aria-label={t.v_pick}
          onChange={(e) => { load(e.target.files?.[0]); e.target.value = ""; }} />
      </div>
      {error && <p role="alert" className="mt-5 flex items-start gap-3 rounded-[4px] border-2 border-line-strong px-4 py-3 text-[16px]"><XIcon className="mt-0.5 shrink-0" width={20} height={20} strokeWidth={2.4} />{error}</p>}
      <p className="mt-5 flex items-center gap-2 text-[15px] text-muted"><LockIcon width={16} height={16} />{t.v_local}</p>
    </main>
  );
}
