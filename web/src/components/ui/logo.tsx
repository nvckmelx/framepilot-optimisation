/* TuneFPS logo — approved outlines (TuneFPS-brand-handoff/01-final/logo/svg/tunefps-logo-dark.svg,
   Archivo Black, tracking −40). Do not redraw or retype; colours follow the brand book:
   "tune" = foreground, "fps" = Steel, tile = Tune Blue, bolt = white. */

const TUNE = "M385 209V344H273V545Q273 581 285.0 597.5Q297 614 329 614H385V731Q361 739 323.0 744.0Q285 749 257 749Q169 749 121.5 717.0Q74 685 74 608V344H0V209H82L125 49H273V209Z M807 737 794 657Q763 700 712.5 724.5Q662 749 606 749Q514 749 468.0 698.0Q422 647 422 551V209H621V527Q621 561 638.5 581.5Q656 602 688 602Q725 602 748.0 578.0Q771 554 771 519V209H970V737Z M1599 395V737H1400V419Q1400 385 1382.5 364.5Q1365 344 1333 344Q1296 344 1273.0 368.0Q1250 392 1250 427V737H1051V209H1214L1227 289Q1258 247 1308.5 222.0Q1359 197 1415 197Q1507 197 1553.0 248.0Q1599 299 1599 395Z M2246 473V507H1850Q1850 564 1875.5 593.0Q1901 622 1956 622Q2006 622 2029.5 601.0Q2053 580 2053 545H2246Q2246 641 2173.0 695.0Q2100 749 1960 749Q1813 749 1732.0 680.5Q1651 612 1651 473Q1651 337 1730.0 267.0Q1809 197 1948 197Q2093 197 2169.5 265.0Q2246 333 2246 473ZM1851 413H2045Q2045 372 2021.5 348.0Q1998 324 1956 324Q1862 324 1851 413Z";
const FPS = "M2660 18V135H2614Q2582 135 2570.0 147.0Q2558 159 2558 188V209H2660V344H2558V737H2359V344H2285V209H2359V187Q2359 85 2409.0 42.5Q2459 0 2552 0Q2574 0 2607.0 5.5Q2640 11 2660 18Z M3270 474Q3270 610 3212.0 679.5Q3154 749 3047 749Q2955 749 2899 689V934H2700V209H2862L2879 284Q2937 197 3047 197Q3154 197 3212.0 267.0Q3270 337 3270 474ZM2897 456V489Q2897 539 2919.0 570.5Q2941 602 2985 602Q3071 602 3071 499V446Q3071 344 2985 344Q2941 344 2919.0 375.0Q2897 406 2897 456Z M3827 367H3642Q3642 338 3616 326Q3598 316 3571 316Q3499 316 3499 350Q3499 369 3524.0 377.0Q3549 385 3606 394Q3676 404 3723.0 417.5Q3770 431 3805.0 465.5Q3840 500 3840 562Q3840 663 3764.5 706.0Q3689 749 3567 749Q3497 749 3435.0 729.5Q3373 710 3334.0 669.0Q3295 628 3295 565H3480V569Q3481 603 3508.5 616.5Q3536 630 3567 630Q3650 630 3650 591Q3650 571 3624.0 562.0Q3598 553 3539 543Q3468 531 3422.5 517.0Q3377 503 3343.0 469.5Q3309 436 3309 376Q3309 279 3382.5 238.0Q3456 197 3574 197Q3680 197 3753.5 238.5Q3827 280 3827 367Z";
const BOLT = "37,10 17,36 30,36 25,54 47,27 34,27 39,10";
const W = 4799, H = 934;

type Props = { className?: string; height?: number; decorative?: boolean };

export function Logo({ className, height = 32, decorative }: Props) {
  return (
    <svg viewBox={`0 0 ${W} ${H}`} height={height} width={(height * W) / H} className={className}
      role={decorative ? undefined : "img"} aria-label={decorative ? undefined : "TuneFPS"} aria-hidden={decorative || undefined}>
      <g transform="translate(0.0 0.0) scale(11.5156)">
        <rect width="64" height="64" rx="14" fill="#007BFF" />
        <polygon points={BOLT} fill="#FFFFFF" />
      </g>
      <g transform="translate(958.8 0)">
        <path fill="currentColor" d={TUNE} />
        <path fill="#A3B3C6" d={FPS} />
      </g>
    </svg>
  );
}

export function Mark({ className, size = 32, decorative = true }: { className?: string; size?: number; decorative?: boolean }) {
  return (
    <svg viewBox="0 0 64 64" width={size} height={size} className={className}
      role={decorative ? undefined : "img"} aria-label={decorative ? undefined : "TuneFPS"} aria-hidden={decorative || undefined}>
      <rect width="64" height="64" rx="14" fill="#007BFF" />
      <polygon points={BOLT} fill="#FFFFFF" />
    </svg>
  );
}
