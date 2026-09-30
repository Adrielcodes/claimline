import type { Metadata } from "next";
import { ClaimView } from "./claim-view";

export async function generateMetadata({ params }: PageProps<"/claims/[claimNumber]">): Promise<Metadata> {
  const { claimNumber } = await params;
  return { title: claimNumber };
}

export default async function ClaimPage({ params }: PageProps<"/claims/[claimNumber]">) {
  const { claimNumber } = await params;
  return <ClaimView claimNumber={claimNumber} />;
}
