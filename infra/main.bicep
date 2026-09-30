// Stage 2: everything else. Deploy after registry.bicep and after the images for `imageTag` have been pushed.
//
//   az deployment group create -g <rg> -f infra/main.bicep -p imageTag=<git sha>
//
// No secrets are created or passed: every service-to-service hop authenticates with one user-assigned
// managed identity that holds narrowly scoped RBAC roles.

targetScope = 'resourceGroup'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Short prefix used in every resource name.')
@maxLength(12)
param prefix string = 'claimline'

@description('Tag of the claimline-api, claimline-worker and claimline-web images in the registry.')
param imageTag string

@description('Provision Azure OpenAI for claim analysis. Without it the worker uses its heuristic analyzer.')
param deployOpenAI bool = true

@description('Azure OpenAI model and version. Must be available as GlobalStandard in the chosen region.')
param openAIModel string = 'gpt-4.1-mini'
param openAIModelVersion string = '2025-04-14'

@description('Use the Azure SQL free offer (one per subscription). Set false if it is already used.')
param useSqlFreeLimit bool = true

var suffix = uniqueString(resourceGroup().id)
var tags = { app: 'claimline' }

// Built-in role definition ids
var roles = {
  acrPull: '7f951dda-4ed3-4ff9-a4ec-2fc0b3a1c2b8'
  serviceBusDataSender: '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
  serviceBusDataReceiver: '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
  serviceBusDataOwner: '090c5cfd-751d-490a-894a-3ce6f1109419'
  cognitiveServicesOpenAIUser: '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
}

// ---------------------------------------------------------------- identity

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${prefix}-${suffix}'
  location: location
  tags: tags
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: take('${prefix}${suffix}', 50)
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  name: guid(registry.id, identity.id, roles.acrPull)
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull)
  }
}

// ---------------------------------------------------------------- observability

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${prefix}-${suffix}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${prefix}-${suffix}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

// ---------------------------------------------------------------- messaging

resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: 'sb-${prefix}-${suffix}'
  location: location
  tags: tags
  sku: { name: 'Standard', tier: 'Standard' } // topics need Standard
  properties: {
    disableLocalAuth: true // no SAS keys: Entra ID only
    minimumTlsVersion: '1.2'
  }
}

resource claimsTopic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: serviceBus
  name: 'claims'
  properties: {
    defaultMessageTimeToLive: 'P7D'
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
  }
}

// Each subscription only receives the event type it handles. Defining a rule named $Default replaces the
// catch-all rule Service Bus would otherwise create. Mirrors deploy/servicebus-emulator.json.
var subscriptions = [
  { name: 'triage', subject: 'claim.submitted' }
  { name: 'projection', subject: 'claim.triaged' }
  { name: 'notifications', subject: 'claim.triaged' }
]

resource topicSubscriptions 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = [
  for s in subscriptions: {
    parent: claimsTopic
    name: s.name
    properties: {
      lockDuration: 'PT1M'
      maxDeliveryCount: 5
      deadLetteringOnMessageExpiration: true
      deadLetteringOnFilterEvaluationExceptions: true
    }
  }
]

resource subscriptionFilters 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2022-10-01-preview' = [
  for (s, i) in subscriptions: {
    parent: topicSubscriptions[i]
    name: '$Default'
    properties: {
      filterType: 'CorrelationFilter'
      correlationFilter: { label: s.subject }
    }
  }
]

// Sender + receiver for the apps. Data Owner is only needed by the KEDA scaler, which reads subscription
// message counts through the management plane of the namespace.
resource serviceBusRoles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for role in [roles.serviceBusDataSender, roles.serviceBusDataReceiver, roles.serviceBusDataOwner]: {
    scope: serviceBus
    name: guid(serviceBus.id, identity.id, role)
    properties: {
      principalId: identity.properties.principalId
      principalType: 'ServicePrincipal'
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', role)
    }
  }
]

// ---------------------------------------------------------------- data

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'sql-${prefix}-${suffix}'
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    // Entra-only auth with the app identity as admin: there is no SQL password to leak or rotate.
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: identity.name
      sid: identity.properties.principalId
      tenantId: subscription().tenantId
      principalType: 'Application'
    }
  }
}

resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'claimline'
  location: location
  tags: tags
  sku: { name: 'GP_S_Gen5', tier: 'GeneralPurpose', family: 'Gen5', capacity: 2 }
  properties: {
    autoPauseDelay: 60
    minCapacity: json('0.5')
    useFreeLimit: useSqlFreeLimit
    freeLimitExhaustionBehavior: useSqlFreeLimit ? 'AutoPause' : null
    requestedBackupStorageRedundancy: 'Local'
  }
}

// ---------------------------------------------------------------- AI

resource openAI 'Microsoft.CognitiveServices/accounts@2024-10-01' = if (deployOpenAI) {
  name: 'oai-${prefix}-${suffix}'
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: { name: 'S0' }
  properties: {
    customSubDomainName: 'oai-${prefix}-${suffix}'
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}

resource openAIDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = if (deployOpenAI) {
  parent: openAI
  name: openAIModel
  sku: { name: 'GlobalStandard', capacity: 20 }
  properties: {
    model: { format: 'OpenAI', name: openAIModel, version: openAIModelVersion }
  }
}

resource openAIUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployOpenAI) {
  scope: openAI
  name: guid(openAI.id, identity.id, roles.cognitiveServicesOpenAIUser)
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.cognitiveServicesOpenAIUser)
  }
}

// ---------------------------------------------------------------- compute

resource environment 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: 'cae-${prefix}-${suffix}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

var serviceBusFqdn = '${serviceBus.name}.servicebus.windows.net'
var commonEnv = [
  { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
  { name: 'ServiceBus__Namespace', value: serviceBusFqdn }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
]
var registries = [{ server: registry.properties.loginServer, identity: identity.id }]
var userIdentity = {
  type: 'UserAssigned'
  userAssignedIdentities: { '${identity.id}': {} }
}

resource api 'Microsoft.App/containerApps@2025-01-01' = {
  name: 'ca-${prefix}-api'
  location: location
  tags: tags
  identity: userIdentity
  dependsOn: [acrPull, serviceBusRoles, sqlAllowAzure]
  properties: {
    environmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: registries
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${registry.properties.loginServer}/claimline-api:${imageTag}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat(commonEnv, [
            {
              name: 'ConnectionStrings__Claims'
              value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${database.name};Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};Encrypt=True;Connect Timeout=60;'
            }
          ])
          probes: [
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 }, periodSeconds: 30 }
            // Generous: the first request after Azure SQL auto-pause waits for the database to resume.
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 }, periodSeconds: 15, timeoutSeconds: 10, failureThreshold: 8 }
          ]
        }
      ]
      // The API hosts the outbox dispatcher and the projection consumer, so it never scales to zero.
      scale: {
        minReplicas: 1
        maxReplicas: 3
        rules: [{ name: 'http', http: { metadata: { concurrentRequests: '50' } } }]
      }
    }
  }
}

resource worker 'Microsoft.App/containerApps@2025-01-01' = {
  name: 'ca-${prefix}-worker'
  location: location
  tags: tags
  identity: userIdentity
  dependsOn: [acrPull, serviceBusRoles, openAIUser]
  properties: {
    environmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: registries
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: '${registry.properties.loginServer}/claimline-worker:${imageTag}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat(commonEnv, [
            { name: 'AzureOpenAI__Endpoint', value: deployOpenAI ? openAI!.properties.endpoint : '' }
            { name: 'AzureOpenAI__Deployment', value: openAIModel }
          ])
        }
      ]
      // Scales on backlog in the triage subscription, down to zero when idle. KEDA authenticates with the
      // same managed identity, so the scaler needs no connection string either.
      scale: {
        minReplicas: 0
        maxReplicas: 5
        rules: [
          {
            name: 'triage-backlog'
            custom: {
              type: 'azure-servicebus'
              identity: identity.id
              metadata: {
                namespace: serviceBus.name
                topicName: claimsTopic.name
                subscriptionName: 'triage'
                messageCount: '5'
              }
            }
          }
        ]
      }
    }
  }
}

resource web 'Microsoft.App/containerApps@2025-01-01' = {
  name: 'ca-${prefix}-web'
  location: location
  tags: tags
  identity: userIdentity
  dependsOn: [acrPull]
  properties: {
    environmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: registries
      ingress: {
        external: true
        targetPort: 3000
        transport: 'auto'
      }
    }
    template: {
      containers: [
        {
          name: 'web'
          image: '${registry.properties.loginServer}/claimline-web:${imageTag}'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [{ name: 'API_URL', value: 'https://${api.properties.configuration.ingress.fqdn}' }]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
        rules: [{ name: 'http', http: { metadata: { concurrentRequests: '50' } } }]
      }
    }
  }
}

output webUrl string = 'https://${web.properties.configuration.ingress.fqdn}'
output apiUrl string = 'https://${api.properties.configuration.ingress.fqdn}'
output serviceBusNamespace string = serviceBusFqdn
output sqlServer string = sqlServer.properties.fullyQualifiedDomainName
output identityClientId string = identity.properties.clientId
