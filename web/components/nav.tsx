"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

const links = [
  { href: "/", label: "Claims queue" },
  { href: "/report", label: "Report a claim" },
  { href: "/how-it-works", label: "How it works" },
];

export function Nav() {
  const pathname = usePathname();
  const isActive = (href: string) => (href === "/" ? pathname === "/" || pathname.startsWith("/claims") : pathname.startsWith(href));

  return (
    <header className="border-b border-line bg-surface">
      <div className="mx-auto flex h-14 max-w-6xl items-center gap-6 px-4 sm:px-6">
        <Link href="/" className="flex items-center gap-2 font-semibold tracking-tight">
          <svg width="22" height="22" viewBox="0 0 24 24" aria-hidden className="text-accent">
            <rect x="2" y="2" width="20" height="20" rx="6" fill="currentColor" />
            <path d="M7 12h3l2-4 2 8 2-4h1" stroke="var(--accent-ink)" strokeWidth="2" fill="none" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
          Claimline
        </Link>
        <nav className="flex gap-1 overflow-x-auto text-sm">
          {links.map((l) => (
            <Link
              key={l.href}
              href={l.href}
              className={`whitespace-nowrap rounded-md px-3 py-1.5 transition-colors ${
                isActive(l.href) ? "bg-surface-2 font-medium text-ink" : "text-muted hover:text-ink"
              }`}
            >
              {l.label}
            </Link>
          ))}
        </nav>
      </div>
    </header>
  );
}
