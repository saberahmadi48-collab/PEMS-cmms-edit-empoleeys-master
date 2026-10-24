import type { CSSProperties, ReactNode } from "react";
import { cn } from "@/lib/utils";

export function Card({ title, subtitle, action, children, className, bodyClassName }: {
  title?: ReactNode; subtitle?: ReactNode; action?: ReactNode; children: ReactNode; className?: string; bodyClassName?: string;
}) {
  return (
    <section className={cn("flex min-w-0 flex-col rounded-xl border border-border bg-card p-4", className)}>
      {(title || action) && (
        <header className="mb-3 flex items-start justify-between gap-3">
          <div className="min-w-0">
            {title && <h2 className="m-0 text-sm font-semibold leading-tight text-foreground">{title}</h2>}
            {subtitle && <p className="m-0 mt-1 text-xs leading-snug text-muted-foreground">{subtitle}</p>}
          </div>
          {action}
        </header>
      )}
      <div className={cn("min-h-0 flex-1", bodyClassName)}>{children}</div>
    </section>
  );
}

export function Empty({ children }: { children: ReactNode }) {
  return <div className="flex h-full min-h-24 items-center justify-center px-3 py-6 text-center text-xs text-muted-foreground">{children}</div>;
}

export function ProgressBar({ value, color, className, label }: { value: number; color?: string; className?: string; label?: string }) {
  const v = Math.max(0, Math.min(100, value));
  return (
    <div className={cn("h-1.5 w-full overflow-hidden rounded-full bg-muted", className)} role="progressbar" aria-valuenow={v} aria-valuemin={0} aria-valuemax={100} aria-label={label}>
      <div className="h-full rounded-full" style={{ width: `${v}%`, background: color ?? "var(--primary)" } as CSSProperties} />
    </div>
  );
}

export function Dot({ color }: { color: string }) {
  return <span aria-hidden="true" className="inline-block size-2.5 shrink-0 rounded-[3px]" style={{ background: color }} />;
}

/** Charts always read left→right (time and magnitude), even on a right-to-left page. */
export function ChartBox({ children, className, style }: { children: ReactNode; className?: string; style?: CSSProperties }) {
  return <div dir="ltr" className={cn("w-full", className)} style={style}>{children}</div>;
}
