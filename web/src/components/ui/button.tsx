import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { cn } from "@/lib/utils";

/* Brand buttons: height ≥ 48 px, radius 4 px, padding 20 px, 16 px bold. Primary = accent with ink text;
   secondary = transparent with a contrasting border. Hover lifts 2 px in 180 ms. */
const buttonVariants = cva(
  "inline-flex items-center justify-center gap-3 whitespace-nowrap rounded-[4px] border font-bold leading-[1.2] cursor-pointer transition-[transform,background-color] duration-[180ms] ease-[cubic-bezier(.2,.8,.2,1)] hover:-translate-y-0.5 motion-reduce:hover:translate-y-0 disabled:pointer-events-none disabled:opacity-40",
  {
    variants: {
      variant: {
        primary: "bg-accent text-ink border-transparent",
        outline: "bg-transparent border-current",
        ink: "bg-ink text-paper border-ink",
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
