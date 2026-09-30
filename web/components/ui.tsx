import type { ReactNode } from "react";
import { routeMeta, type SignalCategory, type TriageRoute } from "@/lib/api";

export function RouteBadge({ route, size = "sm" }: { route?: TriageRoute; size?: "sm" | "lg" }) {
  const pad = size === "lg" ? "px-3 py-1 text-sm" : "px-2 py-0.5 text-xs";
  if (!route)
    return (
      <span className={`inline-flex items-center gap-1.5 rounded-full border border-line text-muted ${pad}`}>
        <span className="pulse-dot size-1.5 rounded-full bg-accent" />
        Triaging
      </span>
    );

  const color = `var(--route-${route})`;
  return (
    <span
      className={`inline-flex items-center gap-1.5 whitespace-nowrap rounded-full font-medium ${pad}`}
      style={{ color, background: `color-mix(in srgb, ${color} 13%, transparent)` }}
    >
      <span className="size-1.5 rounded-full" style={{ background: color }} />
      {size === "lg" ? routeMeta[route].label : routeMeta[route].short}
    </span>
  );
}

export function CategoryTag({ category }: { category: SignalCategory }) {
  const color = `var(--sev-${category})`;
  return (
    <span
      className="rounded px-1.5 py-0.5 text-[11px] font-medium uppercase tracking-wide"
      style={{ color, background: `color-mix(in srgb, ${color} 12%, transparent)` }}
    >
      {category}
    </span>
  );
}

export function StatTile({ label, value, hint }: { label: string; value: ReactNode; hint?: ReactNode }) {
  return (
    <div className="card p-4">
      <div className="text-xs font-medium text-muted">{label}</div>
      <div className="tnum mt-1 text-2xl font-semibold tracking-tight">{value}</div>
      {hint && <div className="mt-1 text-xs text-faint">{hint}</div>}
    </div>
  );
}

/** 0–100 score as a labelled bar; the fill colour escalates with the value. */
export function ScoreMeter({ label, value, thresholds = [30, 50] }: { label: string; value?: number; thresholds?: [number, number] }) {
  const v = value ?? 0;
  const color = v >= thresholds[1] ? "var(--route-special-investigations)" : v >= thresholds[0] ? "var(--route-bodily-injury)" : "var(--route-fast-track)";
  return (
    <div>
      <div className="flex items-baseline justify-between text-sm">
        <span className="text-muted">{label}</span>
        <span className="tnum font-semibold">{value ?? "—"}</span>
      </div>
      <div className="mt-1.5 h-1.5 overflow-hidden rounded-full bg-surface-2" role="meter" aria-valuenow={v} aria-valuemin={0} aria-valuemax={100} aria-label={label}>
        <div className="h-full rounded-full transition-all" style={{ width: `${v}%`, background: color }} />
      </div>
    </div>
  );
}

export function Card({ title, action, children, className = "" }: { title?: ReactNode; action?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <section className={`card ${className}`}>
      {title && (
        <div className="flex items-center justify-between border-b border-line px-4 py-3">
          <h2 className="text-sm font-semibold">{title}</h2>
          {action}
        </div>
      )}
      <div className="p-4">{children}</div>
    </section>
  );
}

export function Field({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div>
      <dt className="text-xs text-muted">{label}</dt>
      <dd className="mt-0.5 text-sm">{value}</dd>
    </div>
  );
}
