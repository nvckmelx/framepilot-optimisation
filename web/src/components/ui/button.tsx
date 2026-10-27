import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { cn } from "@/lib/utils";

/* TuneFPS buttons: height ≥ 44 px, radius 8 px. Primary = Deep Blue #0066D6 with white text (white on Tune Blue
   #007BFF fails contrast for text); secondary = transparent with the strong border. Hover lifts 2 px in 140 ms. */
const buttonVariants = cva(
  "inline-flex items-center justify-center gap-3 whitespace-nowrap rounded-[8px] border font-semibold leading-[1.2] cursor-pointer transition-[transform,background-color] duration-[140ms] ease-[cubic-bezier(.2,0,0,1)] hover:-translate-y-0.5 motion-reduce:hover:translate-y-0 disabled:pointer-events-none disabled:opacity-40",
  {
    variants: {
      variant: {
        primary: "bg-cta text-fg border-transparent hover:bg-cta-hover active:bg-cta-pressed",
        outline: "bg-transparent border-line-strong text-fg hover:bg-raised",
        ink: "bg-sunken text-fg border-line-strong",
      },
      size: {
        md: "min-h-12 px-5 text-base",
        sm: "min-h-11 px-4 text-[15px]",
      },
    },
    defaultVariants: { variant: "primary", size: "md" },
  },
);

type Common = VariantProps<typeof buttonVariants> & { className?: string; children?: React.ReactNode };

export function Button({ className, variant, size, ...props }: Common & React.ButtonHTMLAttributes<HTMLButtonElement>) {
  return <button className={cn(buttonVariants({ variant, size }), className)} {...props} />;
}

export function ButtonLink({ className, variant, size, ...props }: Common & React.AnchorHTMLAttributes<HTMLAnchorElement>) {
  return <a className={cn(buttonVariants({ variant, size }), className)} {...props} />;
}

export { buttonVariants };
