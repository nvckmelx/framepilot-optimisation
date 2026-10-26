import * as React from "react";
import { ButtonLink } from "@/components/ui/button";
import { FrameTrace } from "@/components/ui/frame-trace";
import { PerformanceBento } from "@/components/ui/performance-bento";
import { CheckIcon, DownloadIcon, XIcon } from "@/components/ui/icons";
import { useLang } from "@/lib/i18n";
import { demoReport } from "@/lib/demo";
import { DOWNLOAD_URL } from "@/lib/config";

const H2 = "text-[clamp(28px,4vw,48px)] leading-[1.08] font-bold tracking-[-0.03em]";

export function Landing() {
  const { t } = useLang();
  const demo = React.useMemo(demoReport, []);

  return (
    <main id="main">
      {/* first screen */}
      <section className="border-b border-border-light">
        <div className="wrap grid gap-12 pt-16 pb-20 lg:grid-cols-12 lg:items-center lg:gap-6 lg:pt-24 lg:pb-28">
          <div className="lg:col-span-6">
            <p className="label text-muted-light">{t.hero_label}</p>
            <h1 className="mt-5 text-[clamp(36px,9vw,48px)] leading-[1.05] font-bold tracking-[-0.03em] md:text-[clamp(48px,5.5vw,80px)]">
              <span className="block">{t.hero_title_1}</span>
              <span className="block">{t.hero_title_2}</span>
            </h1>
            <p className="mt-6 max-w-[34rem] text-[18px] leading-[1.6] text-muted-light">{t.hero_lead}</p>
            <div className="mt-8 flex flex-wrap gap-3">
              <ButtonLink href={DOWNLOAD_URL}><DownloadIcon width={20} height={20} />{t.download}</ButtonLink>
              <ButtonLink href="#how" variant="outline">{t.open_viewer}</ButtonLink>
            </div>
            <p className="mt-4 text-[14px] text-muted-light">{t.download_note}</p>
          </div>
          <div className="tile p-5 sm:p-6 lg:col-span-6">
            <FrameTrace caption={t.hero_trace_caption} p99Label={t.hero_trace_p99} spikeLabel={t.hero_trace_spike} />
          </div>
        </div>
      </section>

      {/* promise */}
      <section className="wrap py-20 lg:py-28">
        <p className="max-w-[24ch] text-[clamp(28px,4vw,48px)] leading-[1.12] font-bold tracking-[-0.03em]">{t.promise_title}</p>
        <p className="mt-6 max-w-[40rem] text-[18px] leading-[1.6] text-muted-light">{t.promise_sub}</p>
      </section>

      {/* process: a real sequence, so numbered */}
      <section id="how" className="scroll-mt-20 border-t border-border-light">
        <div className="wrap py-20 lg:py-24">
          <h2 className={H2}>{t.steps_title}</h2>
          <ol className="mt-12 grid gap-6 md:grid-cols-3">
            {t.steps.map(([name, text], i) => (
              <li key={name} className="rounded-[8px] border border-border-light p-8">
                <div className="text-[14px] font-bold text-muted-light tabular">{String(i + 1).padStart(2, "0")}</div>
                <h3 className="mt-6 text-[24px]">{name}</h3>
                <p className="mt-3 text-[16px] leading-[1.6] text-muted-light">{text}</p>
              </li>
            ))}
          </ol>
        </div>
      </section>

      {/* the dashboard, on ink */}
      <section className="bg-ink text-paper">
        <div className="wrap py-20 lg:py-24">
          <div className="mb-10 grid gap-6 lg:grid-cols-12 lg:items-end">
            <div className="lg:col-span-7">
              <h2 className={H2}>{t.viewer_cta_title}</h2>
              <p className="mt-4 max-w-[40rem] text-[17px] leading-[1.6] text-muted-dark">{t.viewer_cta_text}</p>
            </div>
            <div className="lg:col-span-5 lg:text-right">
              <ButtonLink href="#/report" variant="outline">{t.nav_report}</ButtonLink>
            </div>
          </div>
          <p className="label mb-4 inline-block rounded-[4px] bg-accent px-3 py-1.5 text-ink">{t.v_demo_badge}</p>
          <PerformanceBento report={demo} />
        </div>
      </section>

      {/* never */}
      <section>
        <div className="wrap grid gap-10 py-20 lg:grid-cols-12 lg:py-24">
          <h2 className={`${H2} lg:col-span-5`}>{t.never_title}</h2>
          <ul className="divide-y divide-border-light border-y border-border-light lg:col-span-7">
            {t.never.map((x) => (
              <li key={x} className="flex items-start gap-4 py-5 text-[17px] leading-[1.5]">
                <XIcon className="mt-0.5 shrink-0" width={20} height={20} strokeWidth={2.4} />
                {x}
              </li>
            ))}
          </ul>
        </div>
      </section>

      {/* pricing */}
      <section id="pricing" className="scroll-mt-20 border-t border-border-light">
        <div className="wrap py-20 lg:py-24">
          <h2 className={H2}>{t.pricing_title}</h2>
          <div className="mt-12 grid gap-6 md:grid-cols-3">
            {[
              { name: t.plan_free, price: t.plan_free_price, per: "", items: t.plan_free_items, featured: false },
              { name: t.plan_pro, price: t.plan_pro_price, per: t.plan_pro_per, items: t.plan_pro_items, featured: true },
              { name: t.plan_founders, price: t.plan_founders_price, per: t.plan_founders_per, items: t.plan_founders_items, featured: false },
            ].map((p) => (
              <div key={p.name} className={p.featured ? "rounded-[8px] border border-ink bg-ink p-8 text-paper" : "rounded-[8px] border border-border-light p-8"}>
                <h3 className="text-[18px]">{p.name}</h3>
                <div className="mt-5 text-[44px] leading-none font-bold tracking-[-0.03em] tabular">{p.price}</div>
                <div className={p.featured ? "mt-2 min-h-6 text-[14px] text-muted-dark" : "mt-2 min-h-6 text-[14px] text-muted-light"}>{p.per}</div>
                <ul className="mt-6 space-y-3">
                  {p.items.map((it) => (
                    <li key={it} className="flex gap-3 text-[16px] leading-[1.45]">
                      <CheckIcon className={p.featured ? "mt-0.5 shrink-0 text-accent" : "mt-0.5 shrink-0"} width={18} height={18} strokeWidth={2.4} />
                      {it}
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </div>
          <p className="mt-5 text-[14px] text-muted-light">{t.pricing_note}</p>
        </div>
      </section>

      {/* faq */}
      <section id="faq" className="scroll-mt-20 border-t border-border-light">
        <div className="wrap grid gap-10 py-20 lg:grid-cols-12 lg:py-24">
          <h2 className={`${H2} lg:col-span-5`}>{t.faq_title}</h2>
          <div className="divide-y divide-border-light border-y border-border-light lg:col-span-7">
            {t.faq.map(([q, a]) => (
              <details key={q} className="group">
                <summary className="flex min-h-11 cursor-pointer list-none items-center justify-between gap-6 py-5 text-[18px] font-bold">
                  {q}
                  <span className="text-[24px] leading-none transition-transform duration-[180ms] group-open:rotate-45" aria-hidden="true">+</span>
                </summary>
                <p className="max-w-[60ch] pb-6 text-[16px] leading-[1.6] text-muted-light">{a}</p>
              </details>
            ))}
          </div>
        </div>
      </section>
    </main>
  );
}
