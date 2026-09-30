// Mirrors the Claimline API contracts (src/Claimline.Api/Claims/ClaimEndpoints.cs). Enums are kebab-case on the wire.

export type LossType = "collision" | "comprehensive" | "theft" | "glass" | "vandalism" | "weather";
export type ClaimStatus = "submitted" | "triaged";
export type TriageRoute =
  | "fast-track"
  | "standard"
  | "bodily-injury"
  | "total-loss"
  | "special-investigations"
  | "coverage-review";
export type DamageSeverity = "minor" | "moderate" | "severe" | "total-loss";
export type SignalCategory = "coverage" | "fraud" | "severity";

export interface ClaimSummary {
  claimNumber: string;
  status: ClaimStatus;
  lossType: LossType;
  lossDate: string;
  reportedAt: string;
  insuredName: string;
  vehicle: string;
  route?: TriageRoute;
  severityScore?: number;
  fraudScore?: number;
  reserveAmount?: number;
  summary: string;
}

export interface TriageSignal {
  code: string;
  category: SignalCategory;
  weight: number;
  description: string;
}

export interface ClaimAnalysis {
  damageSeverity: DamageSeverity;
  injuriesMentioned: boolean;
  vehiclesInvolved: number;
  thirdPartyInvolved: boolean;
  drivable: boolean;
  damagedAreas: string[];
  summary: string;
}

export interface TimelineEntry {
  at: string;
  type: string;
  title: string;
  detail?: string;
}

export interface ClaimDetail {
  claimNumber: string;
  status: ClaimStatus;
  lossType: LossType;
  lossDate: string;
  reportedAt: string;
  lossLocation: string;
  description: string;
  estimatedAmount?: number;
  injuriesReported: boolean;
  policeReportFiled: boolean;
  policy: {
    policyNumber: string;
    insuredName: string;
    state: string;
    effectiveDate: string;
    expirationDate: string;
    coverages: string[];
    collisionDeductible: number;
    vehicle: string;
    vehicleValue: number;
    priorClaims: number;
  };
  triageAttempt: number;
  route?: TriageRoute;
  severityScore?: number;
  fraudScore?: number;
  reserveAmount?: number;
  analyzer?: string;
  analysis?: ClaimAnalysis;
  signals: TriageSignal[];
  triagedAt?: string;
  triageSeconds?: number;
  timeline: TimelineEntry[];
}

export interface ClaimStats {
  total: number;
  triaged: number;
  pending: number;
  totalReserves: number;
  medianTriageSeconds?: number;
  p95TriageSeconds?: number;
  siuReferrals: number;
  byRoute: { route: TriageRoute; count: number; reserves: number }[];
  byAnalyzer: Record<string, number>;
}

export interface DemoPolicy {
  policyNumber: string;
  lastName: string;
  vehicle: string;
  scenario: string;
}

export interface SubmitClaimResponse {
  claimNumber: string;
  status: ClaimStatus;
  statusUrl: string;
}

export interface ValidationProblem {
  title?: string;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  constructor(
    public status: number,
    public problem?: ValidationProblem,
  ) {
    super(problem?.title ?? `Request failed (${status})`);
  }
}

export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, {
    ...init,
    headers: { "Content-Type": "application/json", ...init?.headers },
    cache: "no-store",
  });
  if (!res.ok) {
    let problem: ValidationProblem | undefined;
    try {
      problem = await res.json();
    } catch {}
    throw new ApiError(res.status, problem);
  }
  return res.json() as Promise<T>;
}

export const routeMeta: Record<TriageRoute, { label: string; short: string; description: string }> = {
  "fast-track": { label: "Fast Track", short: "Fast Track", description: "Low severity, no red flags. Settled with minimal handling." },
  standard: { label: "Standard Adjusting", short: "Standard", description: "Routine claim for a desk adjuster." },
  "bodily-injury": { label: "Bodily Injury", short: "Injury", description: "Injuries reported. Goes to an injury specialist." },
  "total-loss": { label: "Total Loss", short: "Total Loss", description: "Repair likely exceeds vehicle value." },
  "special-investigations": { label: "Special Investigations", short: "SIU", description: "Fraud indicators above threshold." },
  "coverage-review": { label: "Coverage Review", short: "Coverage", description: "Policy may not cover this loss." },
};

export const routeOrder: TriageRoute[] = [
  "fast-track",
  "standard",
  "bodily-injury",
  "total-loss",
  "special-investigations",
  "coverage-review",
];

export const lossTypeLabel: Record<LossType, string> = {
  collision: "Collision",
  comprehensive: "Other damage",
  theft: "Theft",
  glass: "Glass",
  vandalism: "Vandalism",
  weather: "Weather",
};

const currency = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", maximumFractionDigits: 0 });
export const money = (n?: number | null) => (n == null ? "—" : currency.format(n));

export const dateShort = (iso: string) =>
  new Date(iso).toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });

export const dateTime = (iso: string) =>
  new Date(iso).toLocaleString("en-US", { month: "short", day: "numeric", hour: "numeric", minute: "2-digit" });

export function relative(iso: string, now = Date.now()) {
  const seconds = Math.round((now - new Date(iso).getTime()) / 1000);
  if (seconds < 45) return "just now";
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  return `${Math.round(hours / 24)}d ago`;
}
