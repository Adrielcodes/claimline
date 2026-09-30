# Deploying to Azure

Deployment is two Bicep stages driven by `.github/workflows/deploy.yml`:

1. `infra/registry.bicep` creates the Azure Container Registry.
2. CI builds and pushes `claimline-api`, `claimline-worker` and `claimline-web`, tagged with the commit SHA.
3. `infra/main.bicep` deploys everything else and points the Container Apps at the new tag.

## One-time setup

You need the Azure CLI and rights to create role assignments on the target subscription.

```bash
SUB=$(az account show --query id -o tsv)
RG=rg-claimline
LOCATION=eastus2
REPO=Adrielcodes/claimline

az group create -n $RG -l $LOCATION

# Identity that GitHub Actions signs in as
APP_ID=$(az ad app create --display-name claimline-github --query appId -o tsv)
az ad sp create --id $APP_ID

# Federated credential: trust tokens GitHub issues for this repo's production environment
az ad app federated-credential create --id $APP_ID --parameters "{
  \"name\": \"github-production\",
  \"issuer\": \"https://token.actions.githubusercontent.com\",
  \"subject\": \"repo:$REPO:environment:production\",
  \"audiences\": [\"api://AzureADTokenExchange\"]
}"

# Contributor to create resources, RBAC Administrator for the app identity's role assignments,
# AcrPush to push images
for ROLE in "Contributor" "Role Based Access Control Administrator" "AcrPush"; do
  az role assignment create --assignee $APP_ID --role "$ROLE" --scope /subscriptions/$SUB/resourceGroups/$RG
done

# Repository variables (not secrets: none of these grant access by themselves)
gh variable set AZURE_CLIENT_ID --body $APP_ID --repo $REPO
gh variable set AZURE_TENANT_ID --body $(az account show --query tenantId -o tsv) --repo $REPO
gh variable set AZURE_SUBSCRIPTION_ID --body $SUB --repo $REPO
gh variable set AZURE_RESOURCE_GROUP --body $RG --repo $REPO
gh variable set AZURE_LOCATION --body $LOCATION --repo $REPO
```

Then run the **Deploy to Azure** workflow (or push to `main`). The job summary prints the web and API URLs.

### Options

| Variable / parameter | Default | Notes |
| --- | --- | --- |
| `DEPLOY_OPENAI` (repo variable) | `true` | Set `false` to skip Azure OpenAI; the worker falls back to the heuristic analyzer. |
| `openAIModel`, `openAIModelVersion` | `gpt-4.1-mini`, `2025-04-14` | Must be available as a GlobalStandard deployment in your region and subscription. |
| `useSqlFreeLimit` | `true` | The Azure SQL free offer is one database per subscription. Set `false` if you already use it. |

## Deploying by hand

```bash
az deployment group create -g $RG -f infra/registry.bicep
ACR=$(az acr list -g $RG --query [0].name -o tsv)
az acr build -r $ACR -t claimline-api:v1 -f src/Claimline.Api/Dockerfile .
az acr build -r $ACR -t claimline-worker:v1 -f src/Claimline.Worker/Dockerfile .
az acr build -r $ACR -t claimline-web:v1 web
az deployment group create -g $RG -f infra/main.bicep -p imageTag=v1
```

## Cost

At portfolio-level traffic, the idle cost is roughly **$15–25 per month**:

| Resource | Approximate cost |
| --- | --- |
| Service Bus Standard (topics need Standard) | ~$10/month base |
| Container Registry Basic | ~$5/month |
| Container Apps (API min 1 replica, worker and web scale to zero) | Mostly covered by the monthly free grant |
| Azure SQL serverless (free offer) | $0 within the free limit; auto-pauses when idle |
| Azure OpenAI gpt-4.1-mini | Fractions of a cent per claim |
| Log Analytics / Application Insights | Low; 30-day retention |

`az group delete -n $RG` removes everything.

## Security notes

- Service Bus has `disableLocalAuth: true`, Azure OpenAI has `disableLocalAuth: true`, and Azure SQL is Entra-only, so there are no keys or passwords to leak.
- The app identity is the SQL Entra admin, which keeps the demo to a single `az deployment`. In a production system, grant it a contained database user with only `db_datareader`, `db_datawriter` and `db_ddladmin`, and run migrations from a separate identity.
- The API is public so the Scalar docs are reachable. The FNOL endpoints are rate-limited per client IP.
