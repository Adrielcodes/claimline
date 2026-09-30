# ADR 0002: The model extracts facts; rules make decisions

**Status:** Accepted

## Context

An LLM reads free-text loss descriptions far better than keyword matching. But claim routing, fraud referral and reserves are regulated, audited decisions. They must be explainable, reproducible and testable, and they can't stop when a model endpoint is throttled.

## Decision

- `IClaimAnalyzer` only **extracts structured facts**: damage severity, injuries, vehicles involved, third party, drivability, damaged areas and a summary. The Azure OpenAI implementation uses **structured outputs with a strict JSON schema** and temperature 0.
- `TriageEngine` is a **pure function** of the claim, its policy snapshot and those facts. It emits weighted signals (coverage, fraud, severity) and derives the route and initial reserve from them. Every point shown in the UI has a code and a reason.
- `ResilientClaimAnalyzer` wraps the model with a timeout and falls back to `HeuristicClaimAnalyzer` on any failure. The claim records which analyzer ran.

## Consequences

- The rules are fully unit tested (`TriageEngineTests`) without mocks or network access.
- Swapping or upgrading the model can't silently change routing policy; it can only change the extracted facts, which are visible on the claim.
- The heuristic analyzer is deliberately simple. Its known failure modes (such as substring false positives) are covered by regression tests.
