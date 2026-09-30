import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Self-contained server.js for the container image (see Dockerfile).
  output: "standalone",
  poweredByHeader: false,
};

export default nextConfig;
