# ADR 0001: Transactional outbox for claim events

**Status:** Accepted

## Context

Submitting a claim must do two things: persist the claim in Azure SQL and publish `claim.submitted` to Service Bus. The two systems share no transaction. If the API writes the row and then crashes before publishing, the claim is never triaged. If it publishes first and the insert fails, the worker triages a claim that doesn't exist.

## Decision

The API writes the claim, its first timeline entry and an `Outbox` row in **one SQL transaction** (a single `SaveChanges`). A `BackgroundService` (`OutboxDispatcher`) then:

- claims up to 50 unsent rows with `SELECT … WITH (UPDLOCK, READPAST, ROWLOCK)`, so concurrent API replicas each take a different batch,
- publishes each row with its outbox id as the Service Bus `MessageId`,
- marks rows sent (or records the attempt and error) and commits.

The request handler signals the dispatcher right after commit, so the typical publish delay is milliseconds rather than a poll interval.

## Consequences

- Delivery is **at-least-once**: a crash between publish and commit re-sends. Every consumer is therefore idempotent (inbox table in the API; deterministic outgoing ids in the worker plus Service Bus duplicate detection).
- The outbox table grows; a cleanup job for sent rows older than N days is a small follow-up.
- Rejected alternatives: two-phase commit (not available across SQL and Service Bus) and change data capture (heavier to operate for one event type).
