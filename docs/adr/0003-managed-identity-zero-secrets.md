# ADR 0003: Managed identity everywhere, zero secrets

**Status:** Accepted

## Context

Connection strings with keys and passwords are the most common way cloud credentials leak: copied into app settings, logs and CI variables, and rarely rotated.

## Decision

One **user-assigned managed identity** is attached to every Container App and holds RBAC roles:

| Resource | Auth | Role |
| --- | --- | --- |
| Service Bus | `disableLocalAuth: true` | Data Sender, Data Receiver, Data Owner (KEDA reads queue depth) |
| Azure SQL | Entra-only authentication | Server Entra admin (demo simplification, see deploy notes) |
| Azure OpenAI | `disableLocalAuth: true` | Cognitive Services OpenAI User |
| Container Registry | admin user disabled | AcrPull |

Code uses `DefaultAzureCredential` (`AZURE_CLIENT_ID` selects the identity). Locally, the same code paths use emulator connection strings from `appsettings.Development.json`. CI signs in to Azure with **OIDC workload identity federation**, so GitHub stores no cloud secret.

## Consequences

- Nothing in the repository, the pipeline or the Container App configuration is sensitive. The Application Insights connection string is an ingestion endpoint, not a credential.
- The identity is shared by the three apps for simplicity. Per-app identities would tighten blast radius; see the roadmap.
