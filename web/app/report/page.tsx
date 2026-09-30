import type { Metadata } from "next";
import { ReportForm } from "./report-form";

export const metadata: Metadata = { title: "Report a claim" };

export default function ReportPage() {
  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">Report a claim</h1>
        <p className="mt-1 text-sm text-muted">
          First notice of loss. Pick a demo policy, describe what happened in your own words, and watch the claim get triaged
          on the next screen.
        </p>
      </div>
      <ReportForm />
    </div>
  );
}
