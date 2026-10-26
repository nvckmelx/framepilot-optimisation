import * as React from "react";
import { ButtonLink } from "@/components/ui/button";
import { FrameTrace } from "@/components/ui/frame-trace";
import { PerformanceBento } from "@/components/ui/performance-bento";
import { CheckIcon, DownloadIcon, FileIcon, XIcon } from "@/components/ui/icons";
import { useLang } from "@/lib/i18n";
import { demoReport } from "@/lib/demo";
import { DOWNLOAD_URL } from "@/lib/config";

export function Landing() {
  const { t } = useLang();
  const demo = React.useMemo(demoReport, []);

  return (
    <main id="main">
      {/* hero */}
      <section className="relative overflow-hidden border-b border-line">
        <div className="stripes pointer-events-none absolute inset-0" aria-hidden="true" />
        <div className="relative mx-auto grid max-w-[1240px] gap-10 px-4 pt-14 pb-16 sm:px-6 lg:grid-cols-[1.05fr_1fr] lg:items-center lg:gap-14 lg:pt-20 lg:pb-24">
          <div>
            <h1 className="text-[44px] leading-[0.98] font-extrabold tracking-[-0.035em] uppercase sm:text-[64px] lg:text-[76px]">
              <span className="block">{t.hero_title_1}</span>
              <span className="block text-volt">{t.hero_title_2}</span>
            </h1>
            <p className="mt-6 max-w-[34rem] text-[17px] leading-[1.6] text-paper/75">{t.hero_lead}</p>
            <div className="mt-8 flex flex-wrap gap-3">
              <ButtonLink href={DOWNLOAD_URL} size="lg"><DownloadIcon />{t.download}</ButtonLink>
              <ButtonLink href="#/report" variant="ghost" size="lg"><FileIcon />{t.open_viewer}</ButtonLink>
            </div>
            <p className="mt-3 text-[13px] text-moss">{t.download_note}</p>
          </div>
          <div className="tile chamfer bg-panel/80 p-4 sm:p-6">
            <FrameTrace caption={t.hero_trace_caption} p99Label={t.hero_trace_p99} spikeLabel={t.hero_trace_spike} />
          </div>
        </div>
      </section>

      {/* promise */}
      <section className="mx-auto max-w-[1240px] px-4 py-20 sm:px-6 lg:py-28">
        <p className="max-w-[22ch] text-[30px] leading-[1.15] font-bold tracking-[-0.025em] sm:text-[44px]">{t.promise_title}</p>
        <p className="mt-6 max-w-[40rem] text-[17px] leading-[1.6] text-paper/65">{t.promise_sub}</p>
      </section>

      {/* steps: a real sequence, so numbered */}
      <section id="how" className="border-y border-line bg-panel/40">
        <div className="mx-auto max-w-[1240px] px-4 py-20 sm:px-6">
          <h2 className="text-[28px] font-bold tracking-[-0.02em] sm:text-[36px]">{t.steps_title}</h2>
          <ol className="mt-10 grid gap-px overflow-hidden bg-line sm:grid-cols-2 lg:grid-cols-4">
            {t.steps.map(([name, text], i) => (
              <li key={name} className="bg-charcoal p-6">
                <div className="font-mono text-[13px] text-volt tabular">{String(i + 1).padStart(2, "0")}</div>
                <div className="mt-3 text-[19px] font-bold tracking-[-0.01em]">{name}</div>
                <p className="mt-2 text-[14.5px] leading-[1.55] text-paper/65">{text}</p>
              </li>
            ))}
          </ol>
        </div>
      </section>

      {/* live bento with demo data */}
      <section className="mx-auto max-w-[1320px] px-4 py-20 sm:px-6">
        <div className="mb-8 flex flex-wrap items-end justify-between gap-4">
          <div className="max-w-[40rem]">
            <h2 className="text-[28px] font-bold tracking-[-0.02em] sm:text-[36px]">{t.viewer_cta_title}</h2>
            <p className="mt-3 text-[16px] leading-[1.6] text-paper/65">{t.viewer_cta_text}</p>
          </div>
          <ButtonLink href="#/report" variant="ghost"><FileIcon />{t.nav_report}</ButtonLink>
        </div>
        <div className="mb-3 inline-block bg-amber px-3 py-1 text-[12.5px] font-bold text-charcoal chamfer-sm">{t.v_demo_badge}</div>
        <PerformanceBento report={demo} />
      </section>

      {/* never */}
      <section className="border-y border-line">
        <div className="mx-auto grid max-w-[1240px] gap-10 px-4 py-20 sm:px-6 lg:grid-cols-[1fr_1.4fr]">
          <h2 className="text-[28px] font-bold tracking-[-0.02em] sm:text-[36px]">{t.never_title}</h2>
          <ul className="divide-y divide-line">
            {t.never.map((x) => (
              <li key={x} className="flex items-start gap-4 py-4 text-[16px] leading-[1.5]">
                <XIcon className="mt-0.5 shrink-0 text-flare" width={20} height={20} />
                {x}
              </li>
            ))}
          </ul>
        </div>
      </section>

      {/* pricing */}
      <section id="pricing" className="mx-auto max-w-[1240px] px-4 py-20 sm:px-6">
        <h2 className="text-[28px] font-bold tracking-[-0.02em] sm:text-[36px]">{t.pricing_title}</h2>
        <div className="mt-10 grid gap-3.5 md:grid-cols-3">
          {[
            { name: t.plan_free, price: t.plan_free_price, per: "", items: t.plan_free_items, featured: false },
            { name: t.plan_pro, price: t.plan_pro_price, per: t.plan_pro_per, items: t.plan_pro_items, featured: true },
            { name: t.plan_founders, price: t.plan_founders_price, per: t.plan_founders_per, items: t.plan_founders_items, featured: false },
          ].map((p) => (
            <div key={p.name} className={p.featured ? "tile chamfer bg-volt p-7 text-charcoal" : "tile chamfer bg-panel p-7"}>
              <div className="text-[15px] font-bold">{p.name}</div>
              <div className="mt-4 text-[44px] leading-none font-extralight tracking-[-0.04em] tabular">{p.price}</div>
              <div className={p.featured ? "mt-2 h-5 text-[13px] text-charcoal/65" : "mt-2 h-5 text-[13px] text-moss"}>{p.per}</div>
              <ul className="mt-6 space-y-2.5">
                {p.items.map((it) => (
                  <li key={it} className="flex gap-2.5 text-[14.5px] leading-[1.45]">
                    <CheckIcon className="mt-0.5 shrink-0" width={17} height={17} />
                    {it}
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
        <p className="mt-4 text-[13px] text-moss">{t.pricing_note}</p>
      </section>

      {/* faq */}
      <section id="faq" className="border-t border-line">
        <div className="mx-auto grid max-w-[1240px] gap-10 px-4 py-20 sm:px-6 lg:grid-cols-[1fr_1.4fr]">
          <h2 className="text-[28px] font-bold tracking-[-0.02em] sm:text-[36px]">{t.faq_title}</h2>
          <div className="divide-y divide-line border-y border-line">
            {t.faq.map(([q, a]) => (
              <details key={q} className="group py-5">
                <summary className="flex cursor-pointer list-none items-center justify-between gap-6 text-[17px] font-semibold">
                  {q}
                  <span className="text-volt transition-transform group-open:rotate-45" aria-hidden="true">+</span>
                </summary>
                <p className="mt-3 max-w-[60ch] text-[15px] leading-[1.6] text-paper/70">{a}</p>
              </details>
            ))}
          </div>
        </div>
      </section>
    </main>
  );
}
