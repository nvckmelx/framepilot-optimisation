import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { cn } from "@/lib/utils";

const buttonVariants = cva(
  "inline-flex items-center justify-center gap-2 whitespace-nowrap font-semibold transition-[background-color,color,transform] duration-200 active:translate-y-px disabled:pointer-events-none disabled:opacity-40 cursor-pointer",
  {
    variants: {
      variant: {
        primary: "bg-volt text-charcoal hover:bg-[#d8ff75] chamfer-sm",
        ghost: "bg-white/[0.06] text-paper ring-1 ring-inset ring-white/12 hover:bg-white/[0.1] chamfer-sm",
        link: "text-volt underline-offset-4 hover:underline px-0",
      },
      size: {
        md: "h-11 px-5 text-[15px]",
        lg: "h-13 px-7 text-base",
        sm: "h-9 px-3.5 text-sm",
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
