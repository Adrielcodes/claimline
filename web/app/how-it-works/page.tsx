import type { Metadata } from "next";

export const metadata: Metadata = { title: "How it works" };

const decisions = [
  {
    title: "Transactional outbox",
    body: "The claim row and its claim.submitted event commit in one SQL transaction. A background dispatcher relays the outbox to Service Bus, claiming rows with UPDLOCK/READPAST so several API replicas never double-send. A claim is never saved without its event, and no event is ever sent for a claim that wasn't saved.",
  },
  {
    title: "Idempotent consumers",
    body: "Delivery is at-least-once, so every consumer records the message id in an inbox table in the same transaction as its side effects. Redelivered messages are skipped. The worker derives its outgoing message id from the incoming one, so Service Bus duplicate detection drops repeats.",
  },
  {
    title: "Out-of-order safety",
    body: "Re-running triage bumps an attempt number. If a slow result from an older attempt arrives after a newer one, the projection records it on the timeline and does not apply it.",
  },
  {
    title: "AI extracts, rules decide",
    body: "Azure OpenAI turns the policyholder's story into structured facts under a strict JSON schema. Routing, fraud scoring and reserves come from a deterministic rules engine that is unit tested and explains every point it adds.",
  },
  {
    title: "Graceful degradation",
    body: "If the model times out, is throttled, or isn't configured, the worker falls back to a heuristic analyzer. Triage gets less nuanced but never stops, and the claim records which analyzer ran.",
  },
  {
    title: "Poison messages and retries",
    body: "Transient failures are abandoned and retried by Service Bus up to MaxDeliveryCount. Permanent failures (bad payload, unknown claim) are dead-lettered immediately with a reason, so they can be inspected and replayed.",
  },
  {
    title: "Zero secrets",
    body: "Container Apps run as a user-assigned managed identity with RBAC on Service Bus, Azure SQL (Entra-only auth), Azure OpenAI and the registry. There are no connection-string passwords or API keys anywhere in Azure.",
  },
  {
    title: "Scale on queue depth",
    body: "The worker scales on subscription backlog through a KEDA Service Bus rule, down to zero when idle. Azure SQL serverless auto-pauses, and EF Core retries transparently while it resumes.",
  },
  {
    title: "One trace per claim",
    body: "OpenTelemetry follows a claim from the HTTP request through the outbox, topic and worker and back into the API. Locally it goes to the Aspire dashboard; in Azure, to Application Insights.",
  },
  {
    title: "Infrastructure as code, keyless CI/CD",
    body: "Everything is defined in Bicep. GitHub Actions tests against a real SQL Server (Testcontainers), then deploys through OIDC workload identity federation, with no stored cloud credentials.",
  },
];

export default function HowItWorksPage() {
  return (
    <div className="space-y-8">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">How it works</h1>
        <p className="mt-1 max-w-3xl text-sm text-muted">
          Claimline is a small event-driven system on Azure: a .NET 10 API owns the claim data, a stateless worker does the
          triage, and Azure Service Bus decouples them. Each service can fail, restart or scale independently without losing a
          claim.
        </p>
      </div>

      <section className="card overflow-x-auto p-4">
        <Diagram />
      </section>

      <section>
        <h2 className="text-sm font-semibold">Design decisions</h2>
        <div className="mt-3 grid gap-3 md:grid-cols-2">
          {decisions.map((d) => (
            <div key={d.title} className="card p-4">
              <h3 className="text-sm font-semibold">{d.title}</h3>
              <p className="mt-1.5 text-sm text-muted">{d.body}</p>
            </div>
          ))}
        </div>
      </section>

      <section className="card p-4 text-sm">
        <h2 className="font-semibold">Stack</h2>
        <dl className="mt-3 grid gap-x-6 gap-y-2 sm:grid-cols-2">
          {[
            ["Backend", ".NET 10, ASP.NET Core minimal APIs, EF Core 10, BackgroundService workers"],
            ["Messaging", "Azure Service Bus topic with filtered subscriptions (emulator locally)"],
            ["Data", "Azure SQL serverless (SQL Server 2022 locally)"],
            ["AI", "Azure OpenAI with structured outputs, plus a heuristic fallback"],
            ["Frontend", "Next.js 16, React 19, Tailwind CSS 4"],
            ["Hosting", "Azure Container Apps, Azure Container Registry, managed identity"],
            ["Observability", "OpenTelemetry, Application Insights, Aspire dashboard"],
            ["Delivery", "Bicep, GitHub Actions with OIDC, xUnit v3, Testcontainers"],
          ].map(([k, v]) => (
            <div key={k} className="flex gap-3">
              <dt className="w-28 shrink-0 text-muted">{k}</dt>
              <dd>{v}</dd>
            </div>
          ))}
        </dl>
      </section>
    </div>
  );
}

function Box({ x, y, w, h = 56, title, sub, accent }: { x: number; y: number; w: number; h?: number; title: string; sub?: string; accent?: boolean }) {
  return (
    <g>
      <rect x={x} y={y} width={w} height={h} rx={10} fill={accent ? "var(--accent-soft)" : "var(--surface)"} stroke={accent ? "var(--accent)" : "var(--line)"} />
      <text x={x + w / 2} y={y + (sub ? h / 2 - 3 : h / 2 + 4)} textAnchor="middle" fontSize="13" fontWeight="600" fill="var(--ink)">
        {title}
      </text>
      {sub && (
        <text x={x + w / 2} y={y + h / 2 + 14} textAnchor="middle" fontSize="11" fill="var(--muted)">
          {sub}
        </text>
      )}
    </g>
  );
}

function Arrow({ d, label, lx, ly }: { d: string; label?: string; lx?: number; ly?: number }) {
  return (
    <g>
      <path d={d} fill="none" stroke="var(--faint)" strokeWidth="1.5" markerEnd="url(#arrow)" />
      {label && (
        <text x={lx} y={ly} textAnchor="middle" fontSize="10.5" fontFamily="var(--font-geist-mono)" fill="var(--muted)">
          {label}
        </text>
      )}
    </g>
  );
}

function Diagram() {
  return (
    <svg viewBox="0 0 980 400" className="mx-auto block w-full min-w-[760px]" role="img" aria-label="Claimline architecture diagram">
      <defs>
        <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
          <path d="M0,0 L10,5 L0,10 z" fill="var(--faint)" />
        </marker>
      </defs>

      <Box x={10} y={150} w={110} title="Browser" sub="FNOL form, queue" />
      <Box x={160} y={150} w={140} title="Next.js web" sub="BFF proxy" />
      <Box x={345} y={140} w={180} h={76} title="Claims API" sub=".NET 10 · outbox · inbox" accent />
      <Box x={345} y={300} w={180} title="Azure SQL" sub="claims · outbox · inbox" />

      {/* Service Bus topic */}
      <rect x={585} y={40} width={170} height={320} rx={12} fill="var(--surface-2)" stroke="var(--line)" />
      <text x={670} y={64} textAnchor="middle" fontSize="13" fontWeight="600" fill="var(--ink)">
        Service Bus
      </text>
      <text x={670} y={80} textAnchor="middle" fontSize="11" fill="var(--muted)" fontFamily="var(--font-geist-mono)">
        topic: claims
      </text>
      {[
        { y: 100, name: "triage", filter: "claim.submitted" },
        { y: 190, name: "projection", filter: "claim.triaged" },
        { y: 280, name: "notifications", filter: "claim.triaged" },
      ].map((s) => (
        <g key={s.name}>
          <rect x={600} y={s.y} width={140} height={48} rx={8} fill="var(--surface)" stroke="var(--line)" />
          <text x={670} y={s.y + 20} textAnchor="middle" fontSize="12" fontWeight="600" fill="var(--ink)">
            {s.name}
          </text>
          <text x={670} y={s.y + 36} textAnchor="middle" fontSize="10" fill="var(--muted)" fontFamily="var(--font-geist-mono)">
            {s.filter}
          </text>
        </g>
      ))}

      <Box x={800} y={70} w={170} h={70} title="Triage worker" sub="rules engine · KEDA scaled" accent />
      <Box x={800} y={185} w={170} title="Azure OpenAI" sub="structured outputs" />
      <Box x={800} y={276} w={170} title="Notifier" sub="policyholder message" />

      <Arrow d="M120,178 L158,178" />
      <Arrow d="M300,178 L343,178" />
      <Arrow d="M435,216 L435,298" label="1 tx" lx={458} ly={262} />
      <Arrow d="M525,160 C555,160 555,124 598,124" />
      <Arrow d="M740,118 L798,108" />
      <Arrow d="M885,140 L885,183" />
      <Arrow d="M820,140 C800,175 780,212 742,214" />
      <Arrow d="M600,222 C560,222 560,200 527,196" />
      <Arrow d="M740,304 L798,304" />

      <text x={490} y={392} textAnchor="middle" fontSize="11" fill="var(--muted)">
        Managed identity for every hop · OpenTelemetry traces across services · all services on Azure Container Apps
      </text>
    </svg>
  );
}
