"use client";

import Link from "next/link";
import { useState } from "react";
import { RouteBadge, StatTile } from "@/components/ui";
import { usePoll } from "@/lib/use-poll";
import {
  lossTypeLabel,
  money,
  relative,
  routeMeta,
  routeOrder,
  type ClaimStats,
  type ClaimSummary,
  type TriageRoute,
} from "@/lib/api";

export default function QueuePage() {
  const [route, setRoute] = useState<TriageRoute | "pending" | null>(null);
  const [search, setSearch] = useState("");

  const params = new URLSearchParams();
  if (route === "pending") params.set("status", "submitted");
  else if (route) params.set("route", route);
  if (search.trim()) params.set("q", search.trim());

  const stats = usePoll<ClaimStats>("/api/stats", 4000);
  const claims = usePoll<ClaimSummary[]>(`/api/claims?${params}`, 4000);
  const s = stats.data;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Claims queue</h1>
          <p className="mt-1 max-w-2xl text-sm text-muted">
            Every first notice of loss is stored, published to Azure Service Bus, analyzed by AI, scored by auditable rules and
            routed to the right desk in seconds. This page refreshes live.
          </p>
        </div>
        <Link href="/report" className="rounded-lg bg-accent px-4 py-2 text-sm font-medium text-accent-ink hover:opacity-90">
          Report a claim
        </Link>
      </div>

      {(stats.error || claims.error) && (
        <div className="card px-4 py-3 text-sm text-danger">{stats.error?.message ?? claims.error?.message}. Retrying automatically.</div>
      )}

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-5">
        <StatTile label="Claims" value={s?.total ?? "—"} hint={s ? `${s.pending} awaiting triage` : undefined} />
        <StatTile
          label="Median time to triage"
          value={s?.medianTriageSeconds != null ? `${s.medianTriageSeconds.toFixed(1)}s` : "—"}
          hint={s?.p95TriageSeconds != null ? `p95 ${s.p95TriageSeconds.toFixed(1)}s` : "FNOL to routing decision"}
        />
        <StatTile
          label="Fast-tracked"
          value={s ? pct(s.byRoute.find((r) => r.route === "fast-track")?.count ?? 0, s.triaged) : "—"}
          hint="Straight-through processing"
        />
        <StatTile label="SIU referrals" value={s?.siuReferrals ?? "—"} hint="Fraud score of 50 or more" />
        <StatTile label="Case reserves" value={money(s?.totalReserves)} hint="Initial, set at triage" />
      </div>

      {s && s.triaged > 0 && <RouteMix stats={s} />}

      <section className="card">
        <div className="flex flex-wrap items-center gap-2 border-b border-line p-3">
          <FilterChip active={route === null} onClick={() => setRoute(null)}>
            All
          </FilterChip>
          {routeOrder.map((r) => (
            <FilterChip key={r} active={route === r} onClick={() => setRoute(route === r ? null : r)}>
              {routeMeta[r].short}
            </FilterChip>
          ))}
          <FilterChip active={route === "pending"} onClick={() => setRoute(route === "pending" ? null : "pending")}>
            Awaiting triage
          </FilterChip>
          <input
            type="search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search claim, policy, name…"
            aria-label="Search claims"
            className="field !w-full !py-1.5 sm:ml-auto sm:!w-64"
          />
        </div>

        <div className="overflow-x-auto">
          <table className="w-full min-w-[760px] text-sm">
            <thead>
              <tr className="border-b border-line text-left text-xs text-muted">
                <th className="px-4 py-2 font-medium">Claim</th>
                <th className="px-4 py-2 font-medium">Insured</th>
                <th className="px-4 py-2 font-medium">What happened</th>
                <th className="px-4 py-2 font-medium">Route</th>
                <th className="px-4 py-2 text-right font-medium">Severity</th>
                <th className="px-4 py-2 text-right font-medium">Fraud</th>
                <th className="px-4 py-2 text-right font-medium">Reserve</th>
              </tr>
            </thead>
            <tbody>
              {claims.data?.map((c) => (
                <tr key={c.claimNumber} className="border-b border-line last:border-0 hover:bg-surface-2">
                  <td className="px-4 py-3 align-top">
                    <Link href={`/claims/${c.claimNumber}`} className="font-medium text-accent hover:underline">
                      {c.claimNumber}
                    </Link>
                    <div className="text-xs text-faint">{relative(c.reportedAt)}</div>
                  </td>
                  <td className="px-4 py-3 align-top">
                    <div>{c.insuredName}</div>
                    <div className="text-xs text-faint">{c.vehicle}</div>
                  </td>
                  <td className="max-w-sm px-4 py-3 align-top">
                    <div className="text-xs font-medium text-muted">{lossTypeLabel[c.lossType]}</div>
                    <div className="line-clamp-2 text-muted">{c.summary}</div>
                  </td>
                  <td className="px-4 py-3 align-top">
                    <RouteBadge route={c.route} />
                  </td>
                  <td className="tnum px-4 py-3 text-right align-top">{c.severityScore ?? "—"}</td>
                  <td className="tnum px-4 py-3 text-right align-top">{c.fraudScore ?? "—"}</td>
                  <td className="tnum px-4 py-3 text-right align-top">{money(c.reserveAmount)}</td>
                </tr>
              ))}
              {claims.data?.length === 0 && (
                <tr>
                  <td colSpan={7} className="px-4 py-10 text-center text-muted">
                    No claims match.{" "}
                    <Link href="/report" className="text-accent hover:underline">
                      Report one
                    </Link>
                  </td>
                </tr>
              )}
              {claims.loading && !claims.data && (
                <tr>
                  <td colSpan={7} className="px-4 py-10 text-center text-muted">
                    Loading claims…
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
}

function RouteMix({ stats }: { stats: ClaimStats }) {
  const rows = stats.byRoute.filter((r) => r.count > 0);
  return (
    <section className="card p-4">
      <div className="flex items-baseline justify-between">
        <h2 className="text-sm font-semibold">Where claims were routed</h2>
        <span className="text-xs text-faint">{stats.triaged} triaged</span>
      </div>
      <div className="mt-3 flex h-2.5 gap-0.5 overflow-hidden rounded-full" role="img" aria-label="Claims by route">
        {rows.map((r) => (
          <div key={r.route} style={{ flexGrow: r.count, background: `var(--route-${r.route})` }} title={`${routeMeta[r.route].label}: ${r.count}`} />
        ))}
      </div>
      <ul className="mt-3 grid grid-cols-2 gap-x-6 gap-y-2 text-sm sm:grid-cols-3 lg:grid-cols-6">
        {stats.byRoute.map((r) => (
          <li key={r.route} className="flex items-start gap-2">
            <span className="mt-1.5 size-2 shrink-0 rounded-full" style={{ background: `var(--route-${r.route})` }} />
            <span>
              <span className="block text-muted">{routeMeta[r.route].short}</span>
              <span className="tnum font-medium">{r.count}</span>
              <span className="tnum text-xs text-faint"> · {money(r.reserves)}</span>
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}

function FilterChip({ active, onClick, children }: { active: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={`rounded-full border px-3 py-1 text-xs transition-colors ${
        active ? "border-accent bg-accent-soft font-medium text-accent" : "border-line text-muted hover:text-ink"
      }`}
    >
      {children}
    </button>
  );
}

const pct = (n: number, d: number) => (d === 0 ? "—" : `${Math.round((n / d) * 100)}%`);
