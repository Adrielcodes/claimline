"use client";

import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { api, ApiError, lossTypeLabel, type DemoPolicy, type LossType, type SubmitClaimResponse } from "@/lib/api";

interface FormState {
  policyNumber: string;
  lastName: string;
  lossType: LossType;
  lossDate: string;
  lossLocation: string;
  description: string;
  estimatedAmount: string;
  injuriesReported: boolean;
  policeReportFiled: boolean;
}

const stories: { label: string; patch: Partial<FormState> }[] = [
  {
    label: "Parking lot scrape",
    patch: {
      lossType: "collision",
      lossLocation: "Target parking lot, Doral, FL",
      description: "Pulling into a spot I scraped a concrete pole. Scratches and a small dent on the front bumper. No one was hurt and the car drives fine.",
      estimatedAmount: "700",
      injuriesReported: false,
      policeReportFiled: false,
    },
  },
  {
    label: "Highway crash, injuries",
    patch: {
      lossType: "collision",
      lossLocation: "I-95 southbound near Aventura, FL",
      description: "Traffic stopped suddenly and the other driver rear-ended me at highway speed, pushing me into the car ahead. Three-car accident. Airbags deployed, the car was towed, and I was taken to the ER with neck and back pain.",
      estimatedAmount: "",
      injuriesReported: true,
      policeReportFiled: true,
    },
  },
  {
    label: "Stolen overnight",
    patch: {
      lossType: "theft",
      lossLocation: "Street parking, Wynwood, Miami, FL",
      description: "Parked on the street overnight. In the morning the car was gone.",
      estimatedAmount: "",
      injuriesReported: false,
      policeReportFiled: false,
    },
  },
  {
    label: "Flooded in a storm",
    patch: {
      lossType: "weather",
      lossLocation: "Home driveway, Fort Myers, FL",
      description: "During the hurricane the street flooded and the car was submerged up to the dashboard. It will not start and water is inside the cabin.",
      estimatedAmount: "",
      injuriesReported: false,
      policeReportFiled: false,
    },
  },
  {
    label: "Cracked windshield",
    patch: {
      lossType: "glass",
      lossLocation: "Florida Turnpike near Orlando, FL",
      description: "A rock from a gravel truck hit the windshield and the chip has spread into a long crack across the passenger side.",
      estimatedAmount: "400",
      injuriesReported: false,
      policeReportFiled: false,
    },
  },
];

function yesterdayLocal() {
  const d = new Date(Date.now() - 24 * 3600 * 1000);
  d.setMinutes(d.getMinutes() - d.getTimezoneOffset());
  return d.toISOString().slice(0, 16);
}

const empty: FormState = {
  policyNumber: "",
  lastName: "",
  lossType: "collision",
  lossDate: "",
  lossLocation: "",
  description: "",
  estimatedAmount: "",
  injuriesReported: false,
  policeReportFiled: false,
};

export function ReportForm() {
  const router = useRouter();
  const [policies, setPolicies] = useState<DemoPolicy[]>([]);
  const [form, setForm] = useState<FormState>(empty);
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [formError, setFormError] = useState<string>();
  const [submitting, setSubmitting] = useState(false);
  // One key per version of the form: a double click, or a retry after a network error, can't create two claims.
  // Editing the form starts a new key, because that is a different submission.
  const idempotencyKey = useRef<string | null>(null);

  useEffect(() => {
    // The default loss date depends on the browser's clock and time zone, so it is filled in client-side.
    const ready = (list: DemoPolicy[]) => {
      setPolicies(list);
      setForm((f) => ({ ...f, lossDate: f.lossDate || yesterdayLocal() }));
    };
    api<DemoPolicy[]>("/api/demo/policies").then(ready, () => ready([]));
  }, []);

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) => {
    setForm((f) => ({ ...f, [key]: value }));
    idempotencyKey.current = null;
  };

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setErrors({});
    setFormError(undefined);
    try {
      const res = await api<SubmitClaimResponse>("/api/claims", {
        method: "POST",
        headers: { "Idempotency-Key": (idempotencyKey.current ??= crypto.randomUUID()) },
        body: JSON.stringify({
          ...form,
          lossDate: new Date(form.lossDate).toISOString(),
          estimatedAmount: form.estimatedAmount ? Number(form.estimatedAmount) : null,
        }),
      });
      router.push(`/claims/${res.claimNumber}`);
    } catch (err) {
      if (err instanceof ApiError && err.problem?.errors) setErrors(err.problem.errors);
      else if (err instanceof ApiError && err.status === 429) setFormError("Too many claims from this address. Wait a minute and try again.");
      else setFormError(err instanceof Error ? err.message : "Something went wrong.");
      setSubmitting(false);
    }
  }

  const err = (field: string) => errors[field]?.[0];

  return (
    <form onSubmit={submit} className="space-y-6" noValidate>
      <section className="card p-4">
        <h2 className="text-sm font-semibold">1. Choose a demo policy</h2>
        <p className="mt-1 text-xs text-muted">Each policy sets up a different triage scenario.</p>
        <div className="mt-3 grid gap-2 sm:grid-cols-2">
          {policies.map((p) => {
            const selected = form.policyNumber === p.policyNumber;
            return (
              <button
                type="button"
                key={p.policyNumber}
                onClick={() => {
                  set("policyNumber", p.policyNumber);
                  set("lastName", p.lastName);
                }}
                aria-pressed={selected}
                className={`rounded-lg border p-3 text-left transition-colors ${
                  selected ? "border-accent bg-accent-soft" : "border-line hover:bg-surface-2"
                }`}
              >
                <div className="flex items-baseline justify-between gap-2 text-sm">
                  <span className="font-medium">{p.lastName}</span>
                  <span className="font-mono text-xs text-muted">{p.policyNumber}</span>
                </div>
                <div className="text-xs text-faint">{p.vehicle}</div>
                <div className="mt-1 text-xs text-muted">{p.scenario}</div>
              </button>
            );
          })}
          {policies.length === 0 && <p className="text-sm text-muted">Loading policies…</p>}
        </div>
        <div className="mt-4 grid gap-3 sm:grid-cols-2">
          <Labeled label="Policy number" error={err("policyNumber")}>
            <input className="field font-mono" value={form.policyNumber} onChange={(e) => set("policyNumber", e.target.value)} aria-invalid={!!err("policyNumber")} />
          </Labeled>
          <Labeled label="Policyholder last name" error={err("lastName")}>
            <input className="field" value={form.lastName} onChange={(e) => set("lastName", e.target.value)} aria-invalid={!!err("lastName")} />
          </Labeled>
        </div>
      </section>

      <section className="card p-4">
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <h2 className="text-sm font-semibold">2. Tell us what happened</h2>
          <div className="flex flex-wrap gap-1.5">
            <span className="text-xs text-faint">Try:</span>
            {stories.map((s) => (
              <button
                type="button"
                key={s.label}
                onClick={() => {
                  setForm((f) => ({ ...f, ...s.patch }));
                  idempotencyKey.current = null;
                }}
                className="rounded-full border border-line px-2 py-0.5 text-xs text-muted hover:text-ink"
              >
                {s.label}
              </button>
            ))}
          </div>
        </div>

        <div className="mt-4 grid gap-3 sm:grid-cols-2">
          <Labeled label="Type of loss" error={err("lossType")}>
            <select className="field" value={form.lossType} onChange={(e) => set("lossType", e.target.value as LossType)}>
              {(Object.keys(lossTypeLabel) as LossType[]).map((t) => (
                <option key={t} value={t}>
                  {lossTypeLabel[t]}
                </option>
              ))}
            </select>
          </Labeled>
          <Labeled label="When did it happen?" error={err("lossDate")}>
            <input type="datetime-local" className="field" value={form.lossDate} onChange={(e) => set("lossDate", e.target.value)} aria-invalid={!!err("lossDate")} />
          </Labeled>
          <div className="sm:col-span-2">
            <Labeled label="Where?" error={err("lossLocation")}>
              <input className="field" placeholder="Street, city, state" value={form.lossLocation} onChange={(e) => set("lossLocation", e.target.value)} aria-invalid={!!err("lossLocation")} />
            </Labeled>
          </div>
          <div className="sm:col-span-2">
            <Labeled label="Describe what happened" error={err("description")} hint="Plain language is fine. The AI analyzer extracts damage, injuries and parties involved.">
              <textarea
                rows={5}
                className="field resize-y"
                value={form.description}
                onChange={(e) => set("description", e.target.value)}
                aria-invalid={!!err("description")}
              />
            </Labeled>
          </div>
          <Labeled label="Estimated damage (optional)" error={err("estimatedAmount")}>
            <input type="number" min={0} inputMode="decimal" className="field" placeholder="$" value={form.estimatedAmount} onChange={(e) => set("estimatedAmount", e.target.value)} />
          </Labeled>
          <div className="flex flex-col justify-end gap-2 pb-1 text-sm">
            <label className="flex items-center gap-2">
              <input type="checkbox" checked={form.injuriesReported} onChange={(e) => set("injuriesReported", e.target.checked)} className="size-4 accent-[var(--accent)]" />
              Someone was injured
            </label>
            <label className="flex items-center gap-2">
              <input type="checkbox" checked={form.policeReportFiled} onChange={(e) => set("policeReportFiled", e.target.checked)} className="size-4 accent-[var(--accent)]" />
              A police report was filed
            </label>
          </div>
        </div>
      </section>

      {formError && <p className="text-sm text-danger">{formError}</p>}

      <div className="flex items-center justify-end gap-3">
        <span className="text-xs text-faint">Your claim is saved before it is queued for triage, so nothing is lost if a service is down.</span>
        <button
          type="submit"
          disabled={submitting}
          className="shrink-0 rounded-lg bg-accent px-5 py-2.5 text-sm font-medium text-accent-ink hover:opacity-90 disabled:opacity-60"
        >
          {submitting ? "Submitting…" : "Submit claim"}
        </button>
      </div>
    </form>
  );
}

function Labeled({ label, error, hint, children }: { label: string; error?: string; hint?: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-medium text-muted">{label}</span>
      {children}
      {error ? <span className="mt-1 block text-xs text-danger">{error}</span> : hint ? <span className="mt-1 block text-xs text-faint">{hint}</span> : null}
    </label>
  );
}
