"use client";

import { useCallback, useEffect, useEffectEvent, useState } from "react";
import { api, ApiError } from "./api";

interface PollState<T> {
  data?: T;
  error?: ApiError;
  loading: boolean;
}

/**
 * Fetches `path`, then re-fetches every `intervalMs` while the tab is visible.
 * `stopWhen` ends polling once the data reaches a final state (e.g. a claim has been triaged).
 */
export function usePoll<T>(path: string | null, intervalMs: number, stopWhen?: (data: T) => boolean) {
  const [state, setState] = useState<PollState<T>>({ loading: true });
  const [generation, setGeneration] = useState(0);
  const isFinal = useEffectEvent((data: T) => stopWhen?.(data) ?? false);

  useEffect(() => {
    if (!path) return;
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;

    const run = async () => {
      let final = false;
      if (document.visibilityState === "visible") {
        try {
          const data = await api<T>(path);
          if (cancelled) return;
          setState({ data, loading: false });
          final = isFinal(data);
        } catch (e) {
          if (cancelled) return;
          setState((s) => ({ ...s, error: e instanceof ApiError ? e : new ApiError(0), loading: false }));
        }
      }
      if (!cancelled && !final) timer = setTimeout(run, intervalMs);
    };

    run();
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [path, intervalMs, generation]);

  const refresh = useCallback(() => setGeneration((g) => g + 1), []);
  return { ...state, refresh };
}
