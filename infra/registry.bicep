// Stage 1: the container registry, deployed before images are built so CI has somewhere to push them.

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Short prefix used in every resource name.')
@maxLength(12)
param prefix string = 'claimline'

var suffix = uniqueString(resourceGroup().id)

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: take('${prefix}${suffix}', 50)
  location: location
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false // pulls use managed identity, pushes use the CI workload identity
  }
}

output name string = registry.name
output loginServer string = registry.properties.loginServer
