import * as React from "react";
import { LangProvider, useLang } from "@/lib/i18n";
import { Landing } from "@/pages/Landing";
import { ReportViewer } from "@/pages/ReportViewer";
import { ButtonLink } from "@/components/ui/button";
import { DOWNLOAD_URL, REPO_URL } from "@/lib/config";
import { cn } from "@/lib/utils";

/* Hash routing keeps the site fully static on shared hosting: #/ and #/report. */
function useRoute() {
  const get = () => (location.hash.startsWith("#/report") ? "report" : "home");
  const [route, setRoute] = React.useState(get);
  React.useEffect(() => {
    const on = () => {
      setRoute(get());
      if (!location.hash.startsWith("#/") || location.hash === "#/report" || location.hash === "#/") window.scrollTo({ top: 0 });
    };
    window.addEventListener("hashchange", on);
    return () => window.removeEventListener("hashchange", on);
  }, []);
  return route;
}

function Header({ route }: { route: string }) {
  const { t, lang, setLang } = useLang();
  return (
    <header className="sticky top-0 z-40 border-b border-line bg-charcoal/85 backdrop-blur-md">
      <div className="mx-auto flex h-16 max-w-[1320px] items-center gap-6 px-4 sm:px-6">
        <a href="#/" className="shrink-0" aria-label="FramePilot">
          <img src="/assets/logo-wordmark.png" alt="FramePilot" width={168} height={21} className="h-[21px] w-auto" />
        </a>
        <nav className="hidden flex-1 items-center gap-6 text-[14px] text-paper/70 md:flex" aria-label="Main">
          <a className="hover:text-paper" href="#how" onClick={(e) => { if (route !== "home") { e.preventDefault(); location.hash = "#/"; } }}>{t.nav_product}</a>
          <a className={cn("hover:text-paper", route === "report" && "text-paper")} href="#/report">{t.nav_report}</a>
          <a className="hover:text-paper" href="#pricing" onClick={(e) => { if (route !== "home") { e.preventDefault(); location.hash = "#/"; } }}>{t.nav_pricing}</a>
          <a className="hover:text-paper" href="#faq" onClick={(e) => { if (route !== "home") { e.preventDefault(); location.hash = "#/"; } }}>{t.nav_faq}</a>
        </nav>
        <div className="ml-auto flex items-center gap-3">
          <div className="flex text-[13px] font-semibold" role="group" aria-label="Language">
            {(["ru", "en"] as const).map((l) => (
              <button key={l} type="button" aria-pressed={lang === l} onClick={() => setLang(l)}
                className={cn("h-8 w-9 cursor-pointer uppercase", lang === l ? "bg-paper text-charcoal" : "text-paper/60 hover:text-paper")}>{l}</button>
            ))}
          </div>
          <ButtonLink href={DOWNLOAD_URL} size="sm" className="hidden sm:inline-flex">{t.download}</ButtonLink>
        </div>
      </div>
    </header>
  );
}

function Footer() {
  const { t } = useLang();
  return (
    <footer className="border-t border-line">
      <div className="mx-auto flex max-w-[1320px] flex-wrap items-center gap-x-8 gap-y-3 px-4 py-10 text-[13px] text-moss sm:px-6">
        <img src="/assets/mark-32.png" alt="" width={24} height={24} />
        <span>{t.footer_privacy}</span>
        <span>{t.footer_name}</span>
        <a href={REPO_URL} className="ml-auto hover:text-paper">GitHub</a>
      </div>
    </footer>
  );
}

function Shell() {
  const route = useRoute();
  const { t } = useLang();
  return (
    <>
      <a href="#main" className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-50 focus:bg-volt focus:px-3 focus:py-2 focus:text-charcoal">
        {t.nav_product}
      </a>
      <Header route={route} />
      {route === "report" ? <ReportViewer /> : <Landing />}
      <Footer />
    </>
  );
}

export default function App() {
  return (
    <LangProvider>
      <Shell />
    </LangProvider>
  );
}
