import type { NextRequest } from "next/server";

// Backend-for-frontend proxy: the browser only talks to this origin, and API_URL is read at runtime so the
// same image works locally (http://localhost:5080), in docker-compose and in Azure Container Apps.
const API_URL = () => process.env.API_URL ?? "http://localhost:5080";

const FORWARDED_REQUEST_HEADERS = ["content-type", "accept", "idempotency-key"];
const FORWARDED_RESPONSE_HEADERS = ["content-type", "location", "idempotent-replayed", "retry-after"];

async function proxy(req: NextRequest, ctx: RouteContext<"/api/[...path]">) {
  const { path } = await ctx.params;
  const target = new URL(`/api/${path.map(encodeURIComponent).join("/")}`, API_URL());
  target.search = req.nextUrl.search;

  const headers = new Headers();
  for (const name of FORWARDED_REQUEST_HEADERS) {
    const value = req.headers.get(name);
    if (value) headers.set(name, value);
  }
  const clientIp = req.headers.get("x-forwarded-for");
  if (clientIp) headers.set("x-forwarded-for", clientIp);

  try {
    const upstream = await fetch(target, {
      method: req.method,
      headers,
      body: req.method === "GET" || req.method === "HEAD" ? undefined : await req.text(),
      cache: "no-store",
      signal: AbortSignal.timeout(15_000),
    });

    const responseHeaders = new Headers();
    for (const name of FORWARDED_RESPONSE_HEADERS) {
      const value = upstream.headers.get(name);
      if (value) responseHeaders.set(name, value);
    }
    return new Response(upstream.body, { status: upstream.status, headers: responseHeaders });
  } catch {
    return Response.json({ title: "The claims API is unavailable. Try again in a moment." }, { status: 503 });
  }
}

export const GET = proxy;
export const POST = proxy;
