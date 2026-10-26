import * as React from "react";
import { Button } from "@/components/ui/button";
import { PerformanceBento } from "@/components/ui/performance-bento";
import { FileIcon, LockIcon, PlayIcon } from "@/components/ui/icons";
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
      <main id="main" className="mx-auto max-w-[1320px] px-4 pt-8 pb-16 sm:px-6">
        <div className="mb-6 flex flex-wrap items-center gap-x-4 gap-y-3">
          <h1 className="flex-1 text-[28px] font-bold tracking-[-0.03em]">
            {t.v_title}
            <span className="mt-1 block text-[14px] font-medium tracking-normal text-moss sm:ml-3 sm:mt-0 sm:inline">
              {t.v_generated} {new Date(report.generatedAt).toLocaleString(lang === "ru" ? "ru-RU" : "en-US", { dateStyle: "medium", timeStyle: "short" })} · v{report.appVersion}
            </span>
          </h1>
          <Button variant="ghost" size="sm" onClick={() => { setReport(null); setError(null); }}>{t.v_close}</Button>
        </div>
        {report.isDemo && <div className="mb-3 inline-block bg-amber px-3 py-1 text-[12.5px] font-bold text-charcoal chamfer-sm">{t.v_demo_badge}</div>}
        <PerformanceBento key={report.generatedAt + report.sessions.length} report={report} />
      </main>
    );
  }

  return (
    <main id="main" className="mx-auto max-w-[880px] px-4 pt-16 pb-24 sm:px-6">
      <h1 className="text-[36px] font-bold tracking-[-0.03em] sm:text-[44px]">{t.v_title}</h1>
      <p className="mt-3 text-[16px] leading-[1.6] text-paper/65">{t.v_how}</p>
      <div
        onDragOver={(e) => { e.preventDefault(); setDrag(true); }}
        onDragLeave={() => setDrag(false)}
        onDrop={(e) => { e.preventDefault(); setDrag(false); load(e.dataTransfer.files[0]); }}
        className={cn("tile chamfer mt-8 grid place-items-center border border-dashed px-6 py-16 text-center transition-colors",
          drag ? "border-volt bg-volt/8" : "border-white/18 bg-panel")}>
        <FileIcon width={34} height={34} className="text-volt" />
        <p className="mt-4 text-[19px] font-semibold">{t.v_drop}</p>
        <p className="mt-1 text-[14px] text-moss">{t.v_drop_or}</p>
        <div className="mt-4 flex flex-wrap justify-center gap-3">
          <Button onClick={() => input.current?.click()}><FileIcon />{t.v_pick}</Button>
          <Button variant="ghost" onClick={() => setReport(demoReport())}><PlayIcon />{t.v_demo}</Button>
        </div>
        <input ref={input} type="file" accept="application/json,.json" className="sr-only" aria-label={t.v_pick}
          onChange={(e) => { load(e.target.files?.[0]); e.target.value = ""; }} />
      </div>
      {error && <p role="alert" className="mt-4 border-l-2 border-flare bg-flare/10 px-4 py-3 text-[14.5px]">{error}</p>}
      <p className="mt-5 flex items-center gap-2 text-[13.5px] text-moss"><LockIcon width={16} height={16} />{t.v_local}</p>
    </main>
  );
}
