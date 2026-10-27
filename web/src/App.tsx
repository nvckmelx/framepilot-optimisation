import * as React from "react";
import { LangProvider, useLang } from "@/lib/i18n";
import { Landing } from "@/pages/Landing";
import { ReportViewer } from "@/pages/ReportViewer";
import { ButtonLink } from "@/components/ui/button";
import { DOWNLOAD_URL, REPO_URL } from "@/lib/config";
import { cn } from "@/lib/utils";
import { Logo } from "@/components/ui/logo";

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
  const toHome = (e: React.MouseEvent) => { if (route !== "home") { e.preventDefault(); location.hash = "#/"; } };
  return (
    <header className="sticky top-0 z-40 border-b border-line bg-app/90 backdrop-blur">
      <div className="wrap flex h-[72px] items-center gap-8">
        <a href="#/" className="shrink-0 text-fg" aria-label="TuneFPS">
          <Logo height={30} decorative />
        </a>
        <nav className="hidden flex-1 items-center gap-7 text-[15px] text-muted lg:flex" aria-label="Main">
          <a className="hover:text-fg" href="#how" onClick={toHome}>{t.nav_product}</a>
          <a className={cn("hover:text-fg", route === "report" && "text-fg")} href="#/report">{t.nav_report}</a>
          <a className="hover:text-fg" href="#pricing" onClick={toHome}>{t.nav_pricing}</a>
          <a className="hover:text-fg" href="#faq" onClick={toHome}>{t.nav_faq}</a>
        </nav>
        <div className="ml-auto flex items-center gap-3">
          <div className="flex rounded-[8px] border border-line p-0.5 text-[13px] font-bold" role="group" aria-label="Language">
            {(["ru", "en"] as const).map((l) => (
              <button key={l} type="button" aria-pressed={lang === l} onClick={() => setLang(l)}
                className={cn("h-10 w-11 cursor-pointer rounded-[6px] uppercase", lang === l ? "bg-raised text-fg" : "text-muted hover:text-fg")}>{l}</button>
            ))}
          </div>
          <ButtonLink href={DOWNLOAD_URL} size="sm" className="hidden sm:inline-flex">{t.download_short}</ButtonLink>
        </div>
      </div>
    </header>
  );
}

function Footer() {
  const { t } = useLang();
  return (
    <footer className="bg-sunken text-fg">
      <div className="wrap flex flex-wrap items-center gap-x-10 gap-y-4 py-12 text-[14px] text-muted">
        <span className="text-fg"><Logo height={30} /></span>
        <span>{t.footer_privacy}</span>
        <span>{t.footer_name}</span>
        <a href={REPO_URL} className="ml-auto hover:text-fg">GitHub</a>
      </div>
    </footer>
  );
}

function Shell() {
  const route = useRoute();
  const { t } = useLang();
  return (
    <>
      <a href="#main" className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-50 focus:rounded-[8px] focus:bg-cta focus:px-3 focus:py-2 focus:text-fg">
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
