"use client";

import Link from "next/link";
import { useState } from "react";
import { Card, CategoryTag, Field, RouteBadge, ScoreMeter } from "@/components/ui";
import { usePoll } from "@/lib/use-poll";
import { api, dateShort, dateTime, lossTypeLabel, money, routeMeta, type ClaimDetail, type TimelineEntry } from "@/lib/api";

export function ClaimView({ claimNumber }: { claimNumber: string }) {
  const { data: claim, error, refresh } = usePoll<ClaimDetail>(
    `/api/claims/${encodeURIComponent(claimNumber)}`,
    1000,
    (c) => c.status === "triaged",
  );
  const [retriaging, setRetriaging] = useState(false);

  if (error?.status === 404)
    return (
      <div className="card p-8 text-center">
        <p className="font-medium">Claim {claimNumber} not found.</p>
        <Link href="/" className="mt-2 inline-block text-sm text-accent hover:underline">
          Back to the queue
        </Link>
      </div>
    );

  if (!claim) return <div className="card p-8 text-center text-sm text-muted">{error ? `${error.message}. Retrying…` : "Loading claim…"}</div>;

  async function retriage() {
    setRetriaging(true);
    try {
      await api(`/api/claims/${encodeURIComponent(claimNumber)}/retriage`, { method: "POST" });
      refresh();
    } finally {
      setRetriaging(false);
    }
  }

  const pending = claim.status === "submitted";
  const signalsByWeight = [...claim.signals].sort((a, b) => b.weight - a.weight);

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <Link href="/" className="text-xs text-muted hover:text-ink">
            ← Claims queue
          </Link>
          <h1 className="mt-1 flex flex-wrap items-center gap-3 text-xl font-semibold tracking-tight">
            <span className="font-mono">{claim.claimNumber}</span>
            <RouteBadge route={pending ? undefined : claim.route} size="lg" />
          </h1>
          <p className="mt-1 text-sm text-muted">
            {lossTypeLabel[claim.lossType]} · {claim.policy.insuredName} · {claim.policy.vehicle} · reported {dateTime(claim.reportedAt)}
          </p>
        </div>
        <button
          type="button"
          onClick={retriage}
          disabled={pending || retriaging}
          className="rounded-lg border border-line bg-surface px-3 py-1.5 text-sm hover:bg-surface-2 disabled:opacity-50"
          title="Publishes claim.submitted again. Results from older attempts are ignored."
        >
          Re-run triage
        </button>
      </div>

      {pending ? (
        <PipelineProgress timeline={claim.timeline} />
      ) : (
        <div className="grid gap-4 lg:grid-cols-3">
          <Card title="Decision" className="lg:col-span-1">
            <div className="space-y-4">
              {claim.route && <p className="text-sm text-muted">{routeMeta[claim.route].description}</p>}
              <ScoreMeter label="Severity" value={claim.severityScore} />
              <ScoreMeter label="Fraud indicators" value={claim.fraudScore} thresholds={[20, 50]} />
              <dl className="grid grid-cols-2 gap-3 border-t border-line pt-4">
                <Field label="Initial reserve" value={<span className="tnum font-semibold">{money(claim.reserveAmount)}</span>} />
                <Field label="Time to triage" value={<span className="tnum">{claim.triageSeconds?.toFixed(2)}s</span>} />
                <Field label="Analyzer" value={<code className="text-xs">{claim.analyzer}</code>} />
                <Field label="Attempt" value={claim.triageAttempt} />
              </dl>
            </div>
          </Card>

          <Card title="What the AI extracted" className="lg:col-span-2" action={<span className="text-xs text-faint">facts only; rules make the decision</span>}>
            {claim.analysis && (
              <div className="space-y-4">
                <p className="text-sm">{claim.analysis.summary}</p>
                <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3">
                  <Field label="Damage" value={<span className="capitalize">{claim.analysis.damageSeverity.replace("-", " ")}</span>} />
                  <Field label="Drivable" value={claim.analysis.drivable ? "Yes" : "No"} />
                  <Field label="Injuries mentioned" value={claim.analysis.injuriesMentioned ? "Yes" : "No"} />
                  <Field label="Vehicles involved" value={claim.analysis.vehiclesInvolved} />
                  <Field label="Other party involved" value={claim.analysis.thirdPartyInvolved ? "Yes" : "No"} />
                  <Field label="Damaged areas" value={claim.analysis.damagedAreas.length ? claim.analysis.damagedAreas.join(", ") : "—"} />
                </dl>
              </div>
            )}
          </Card>

          <Card title="Why it was routed here" className="lg:col-span-3">
            <ul className="divide-y divide-line">
              {signalsByWeight.map((s) => (
                <li key={s.code} className="flex items-start gap-3 py-2.5 first:pt-0 last:pb-0">
                  <CategoryTag category={s.category} />
                  <div className="min-w-0 flex-1">
                    <div className="text-sm">{s.description}</div>
                    <code className="text-[11px] text-faint">{s.code}</code>
                  </div>
                  <span className="tnum text-sm font-medium text-muted">+{s.weight}</span>
                </li>
              ))}
            </ul>
          </Card>
        </div>
      )}

      <div className="grid gap-4 lg:grid-cols-3">
        <Card title="Loss details" className="lg:col-span-2">
          <p className="whitespace-pre-line text-sm">{claim.description}</p>
          <dl className="mt-4 grid grid-cols-2 gap-3 border-t border-line pt-4 sm:grid-cols-4">
            <Field label="Date of loss" value={dateTime(claim.lossDate)} />
            <Field label="Location" value={claim.lossLocation} />
            <Field label="Estimate" value={money(claim.estimatedAmount)} />
            <Field label="Police report" value={claim.policeReportFiled ? "Filed" : "None"} />
          </dl>
        </Card>
        <Card title="Policy">
          <dl className="grid grid-cols-2 gap-3">
            <Field label="Policy" value={<span className="font-mono">{claim.policy.policyNumber}</span>} />
            <Field label="State" value={claim.policy.state} />
            <Field label="Term" value={`${dateShort(claim.policy.effectiveDate)} – ${dateShort(claim.policy.expirationDate)}`} />
            <Field label="Prior claims" value={claim.policy.priorClaims} />
            <Field label="Vehicle value" value={money(claim.policy.vehicleValue)} />
            <Field label="Deductible" value={money(claim.policy.collisionDeductible)} />
            <div className="col-span-2">
              <Field label="Coverages" value={claim.policy.coverages.join(", ")} />
            </div>
          </dl>
        </Card>
      </div>

      <Card title="Event timeline" action={<span className="text-xs text-faint">audit trail written by each service</span>}>
        <Timeline entries={claim.timeline} />
      </Card>
    </div>
  );
}

const steps = [
  { type: "fnol-received", label: "Saved with outbox event", detail: "Claim and claim.submitted committed in one SQL transaction" },
  { type: "event-published", label: "Published to Service Bus", detail: "Outbox dispatcher relays to the claims topic" },
  { type: "analysis", label: "AI analysis and rules", detail: "Worker extracts facts, rules engine scores and routes" },
  { type: "triaged", label: "Routed", detail: "claim.triaged consumed by the API projection" },
];

function PipelineProgress({ timeline }: { timeline: TimelineEntry[] }) {
  const done = new Set(timeline.map((t) => t.type));
  const current = done.has("event-published") ? 2 : done.has("fnol-received") ? 1 : 0;
  return (
    <section className="card p-5">
      <h2 className="text-sm font-semibold">Triaging your claim</h2>
      <p className="mt-1 text-xs text-muted">This usually takes a second or two. The page updates on its own.</p>
      <ol className="mt-4 grid gap-3 sm:grid-cols-4">
        {steps.map((step, i) => {
          const state = i < current ? "done" : i === current ? "active" : "todo";
          return (
            <li key={step.type} className={`rounded-lg border p-3 ${state === "active" ? "border-accent bg-accent-soft" : "border-line"}`}>
              <div className="flex items-center gap-2 text-sm font-medium">
                <span
                  className={`flex size-5 items-center justify-center rounded-full text-[11px] ${
                    state === "done" ? "bg-accent text-accent-ink" : state === "active" ? "pulse-dot bg-accent text-accent-ink" : "bg-surface-2 text-faint"
                  }`}
                >
                  {state === "done" ? "✓" : i + 1}
                </span>
                {step.label}
              </div>
              <p className="mt-1 text-xs text-muted">{step.detail}</p>
            </li>
          );
        })}
      </ol>
    </section>
  );
}

function Timeline({ entries }: { entries: TimelineEntry[] }) {
  if (entries.length === 0) return <p className="text-sm text-muted">No events yet.</p>;
  const start = new Date(entries[0].at).getTime();
  return (
    <ol className="relative space-y-4 border-l border-line pl-5">
      {entries.map((e, i) => (
        <li key={i} className="relative">
          <span className="absolute -left-[25px] top-1.5 size-2.5 rounded-full border-2 border-surface bg-accent" />
          <div className="flex flex-wrap items-baseline gap-x-3">
            <span className="text-sm font-medium">{e.title}</span>
            <span className="tnum font-mono text-xs text-faint">
              {i === 0 ? dateTime(e.at) : `+${((new Date(e.at).getTime() - start) / 1000).toFixed(2)}s`}
            </span>
          </div>
          {e.detail && <p className="mt-0.5 break-all text-xs text-muted">{e.detail}</p>}
        </li>
      ))}
    </ol>
  );
}
