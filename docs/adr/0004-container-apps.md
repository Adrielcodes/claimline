# ADR 0004: Azure Container Apps for hosting

**Status:** Accepted

## Options considered

| Option | Why not (for this system) |
| --- | --- |
| AKS | Cluster operations (upgrades, node pools, ingress controllers) outweigh the benefit for three services. |
| Azure Functions | A good fit for the worker, but the API's long-running outbox dispatcher and Service Bus processor are more natural as hosted services, and one hosting model keeps local and cloud runtimes identical. |
| App Service | No scale-to-zero, and no built-in queue-depth autoscaling for the worker. |

## Decision

Run the API, worker and web as Container Apps in one environment:

- **API:** min 1 replica (it hosts the outbox dispatcher and projection consumer), HTTP scale rule, liveness and readiness probes.
- **Worker:** no ingress, **KEDA `azure-servicebus` scale rule** on the triage subscription, min 0 / max 5, authenticated with the managed identity.
- **Web:** min 0, HTTP scale rule.

## Consequences

- The same container images run in `docker compose` locally and in Azure.
- Idle cost is close to zero beyond the Service Bus and registry base fees.
- Cold starts: the first claim after an idle period waits for a worker replica, which is acceptable because the flow is already asynchronous and the UI shows progress.
