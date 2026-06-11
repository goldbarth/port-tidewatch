targetScope = 'subscription'

@minLength(1)
@description('Name of the azd environment — used to derive resource names.')
param environmentName string

@minLength(1)
@description('Primary location for all resources.')
param location string

@description('Location for the Static Web App (limited region set).')
param staticWebAppLocation string = 'westeurope'

var resourceToken = toLower(uniqueString(subscription().id, environmentName, location))
var tags = { 'azd-env-name': environmentName }

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

module resources 'resources.bicep' = {
  name: 'resources'
  scope: rg
  params: {
    location: location
    staticWebAppLocation: staticWebAppLocation
    tags: tags
    resourceToken: resourceToken
  }
}

output AZURE_CONTAINER_REGISTRY_ENDPOINT string = resources.outputs.registryLoginServer
output INGESTION_FQDN string = resources.outputs.ingestionFqdn
output DASHBOARD_URL string = resources.outputs.dashboardUrl
