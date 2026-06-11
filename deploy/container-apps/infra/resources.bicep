@description('Location for all resources.')
param location string

@description('Location for the Static Web App (limited region set).')
param staticWebAppLocation string

@description('Tags applied to every resource (carries the azd-env-name).')
param tags object

@description('Short unique token used to name resources.')
param resourceToken string

// Placeholder until azd deploys the real images; lets the infra provision standalone.
var placeholderImage = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${resourceToken}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// User-assigned identity that the container apps use to pull from ACR.
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${resourceToken}'
  location: location
  tags: tags
}

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  // resourceToken is a 13-char uniqueString at runtime, so the name is always >= 5.
  #disable-next-line BCP334
  name: 'acr${resourceToken}'
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
  }
}

// AcrPull for the identity, scoped to the registry.
var acrPullRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '7f951dda-4ed3-4680-a7ca-43fe172d538d'
)
resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, identity.id, acrPullRoleId)
  scope: acr
  properties: {
    roleDefinitionId: acrPullRoleId
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource caEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${resourceToken}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// Broker — internal TCP, public image (not built by us, so not an azd service).
resource rabbitmq 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'rabbitmq'
  location: location
  tags: tags
  properties: {
    managedEnvironmentId: caEnv.id
    configuration: {
      ingress: {
        external: false
        transport: 'tcp'
        targetPort: 5672
        exposedPort: 5672
      }
    }
    template: {
      containers: [
        {
          name: 'rabbitmq'
          image: 'rabbitmq:3.11'
          resources: { cpu: json('0.5'), memory: '1Gi' }
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

// Ingestion — external HTTP API + consumer. azd deploys the built image here.
resource ingestion 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ingestion'
  location: location
  tags: union(tags, { 'azd-service-name': 'ingestion' })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    managedEnvironmentId: caEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
      }
      registries: [
        { server: acr.properties.loginServer, identity: identity.id }
      ]
    }
    template: {
      containers: [
        {
          name: 'ingestion'
          image: placeholderImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'RabbitMq__HostName', value: 'rabbitmq' }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

// Simulator — no ingress. azd deploys the built image here.
resource simulator 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'simulator'
  location: location
  tags: union(tags, { 'azd-service-name': 'simulator' })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    managedEnvironmentId: caEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        { server: acr.properties.loginServer, identity: identity.id }
      ]
    }
    template: {
      containers: [
        {
          name: 'simulator'
          image: placeholderImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'RABBITMQ_HOST', value: 'rabbitmq' }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

// Dashboard — Azure Static Web App (Free). azd builds the Angular app and deploys here.
resource dashboard 'Microsoft.Web/staticSites@2023-12-01' = {
  name: 'swa-${resourceToken}'
  location: staticWebAppLocation
  tags: union(tags, { 'azd-service-name': 'dashboard' })
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

output registryLoginServer string = acr.properties.loginServer
output ingestionFqdn string = ingestion.properties.configuration.ingress.fqdn
output dashboardUrl string = 'https://${dashboard.properties.defaultHostname}'
