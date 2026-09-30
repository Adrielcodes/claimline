import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import { Nav } from "@/components/nav";
import "./globals.css";

const geistSans = Geist({ variable: "--font-geist-sans", subsets: ["latin"] });
const geistMono = Geist_Mono({ variable: "--font-geist-mono", subsets: ["latin"] });

export const metadata: Metadata = {
  title: { default: "Claimline", template: "%s · Claimline" },
  description: "Event-driven auto claims intake and AI triage on Azure: .NET 10, Service Bus, Container Apps, Azure OpenAI.",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col font-sans">
        <Nav />
        <main className="mx-auto w-full max-w-6xl flex-1 px-4 pb-16 pt-6 sm:px-6">{children}</main>
        <footer className="border-t border-line py-6 text-center text-xs text-faint">
          Claimline is a portfolio demo. Policies and claims are fictional.{" "}
          <a className="underline hover:text-muted" href="https://github.com/Adrielcodes/claimline">
            Source on GitHub
          </a>
        </footer>
      </body>
    </html>
  );
}
