# Building Automated Secret Rotation Using Azure Key Vault and .NET

> Securely Manage and Automatically Rotate Secrets Using Azure Key Vault Service

---

## Introduction

In cloud-native systems, secrets such as database passwords and API keys carry expiration policies. Manual rotation across distributed services introduces operational risk and is a common cause of unplanned outages.

This repository demonstrates an event-driven, fully automated secret rotation pipeline built on Azure. When a secret approaches expiry, Azure Key Vault emits a `Microsoft.KeyVault.SecretNearExpiry` event that triggers an Azure Function to regenerate and update the secret — with zero manual intervention and no application downtime.

**Stack:**

- **Azure Key Vault** — Centralized secret storage with versioning
- **Azure Event Grid** — Event-driven trigger on near-expiry
- **Azure Functions (.NET isolated)** — Rotation logic execution
- **.NET 10 Client App** — Dynamically retrieves the latest secret version at runtime

---

## Prerequisites

Before starting, ensure you have:

- An active Azure Subscription
- [.NET 10 SDK](https://dotnet.microsoft.com/download) installed
- [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) installed
- Basic understanding of Azure Functions

---

## Architecture Overview

The automated secret rotation flow works as follows:

1. **Secret Near Expiry Event** — Azure Key Vault detects a secret approaching its expiration date and emits a `Microsoft.KeyVault.SecretNearExpiry` event (~30 days before expiry).
2. **Event Routing via Event Grid** — The event is published to Azure Event Grid, which acts as a central event router.
3. **Triggering Azure Function** — The Azure Function App, subscribed to this event, gets triggered automatically.
4. **Password Regeneration** — The function securely generates a new strong password.
5. **Updating Key Vault** — The new password is stored in Azure Key Vault as a new secret version.
6. **Seamless Application Access** — Applications always fetch the latest version dynamically from Key Vault without any manual updates.

---

## Solution Structure

```
SecretRotationFunction (Solution)
│
├── SecretRotationFunction/         # Azure Function App (Core Rotation Logic)
│   ├── SecretRotationFunction.cs   # Event Grid + HTTP trigger functions
│   ├── Program.cs                  # Function app startup configuration
│   ├── host.json                   # Function runtime configuration
│   └── local.settings.json         # Local environment variables
│
└── KeyVaultSecretReader/           # .NET Client Application
    ├── Program.cs                  # Fetches latest secret from Key Vault
    └── appsettings.json            # Configuration (Client ID, Secret, KV URL)
```

---

## Step-by-Step Setup

### Step 1: Create a Resource Group

```bash
az group create \
  --name secret-rotation-demo \
  --location centralindia
```

### Step 2: Create an Azure Key Vault

```bash
az keyvault create \
  --name kv-secret-rotation-demo \
  --resource-group secret-rotation-demo \
  --location centralindia \
  --sku standard
```

### Step 3: Retrieve Your User Object ID

```bash
az ad signed-in-user show --query id -o tsv
```

### Step 4: Grant Key Vault Access Permissions

```bash
az role assignment create \
  --role "Key Vault Secrets Officer" \
  --assignee <YOUR_OBJECT_ID> \
  --scope /subscriptions/<SUB_ID>/resourceGroups/secret-rotation-demo/providers/Microsoft.KeyVault/vaults/kv-secret-rotation-demo
```

### Step 5: Store a Secret with an Expiration Date

```bash
az keyvault secret set \
  --vault-name kv-secret-rotation-demo \
  --name DbPassword \
  --value "Password@123" \
  --expires 2026-03-08T00:00:00Z
```

### Step 6: Create a Storage Account

```bash
az storage account create \
  --name kvrotationstorage123 \
  --resource-group secret-rotation-demo \
  --location centralindia \
  --sku Standard_LRS
```

### Step 7: Configure local.settings.json

```json
{
    "IsEncrypted": false,
    "Values": {
        "AzureWebJobsStorage": "UseDevelopmentStorage=true",
        "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
        "KEY_VAULT_URI": "<KEY_VAULT_URL>"
    }
}
```

### Step 8: Configure appsettings.json (KeyVaultSecretReader)

```json
{
  "KeyVault": {
    "TenantId": "<TENANT_ID>",
    "ClientId": "<CLIENT_ID>",
    "ClientSecret": "<CLIENT_SECRET>",
    "Url": "<KEY_VAULT_URL>",
    "SecretName": "<SECRET_NAME>"
  },
  "Sql": {
    "Server": "your-server-name.database.windows.net",
    "Database": "your-database-name",
    "UserName": "your-sql-login"
  }
}
```

### Step 9: Enable Managed Identity for the Function App

```bash
az functionapp identity assign \
  --name kv-rotation-function-demo \
  --resource-group secret-rotation-demo
```

Save the returned `principalId`.

### Step 10: Grant Function Access to Key Vault

```bash
az role assignment create \
  --role "Key Vault Secrets Officer" \
  --assignee <principalId> \
  --scope /subscriptions/<SUB_ID>/resourceGroups/secret-rotation-demo/providers/Microsoft.KeyVault/vaults/kv-secret-rotation-demo
```

### Step 11: Create an App Registration

```bash
az ad app create --display-name keyvault-demo-app
```

Save the returned `appId` as `CLIENT_ID`.

### Step 12: Create a Service Principal

```bash
az ad sp create --id <CLIENT_ID>
```

### Step 13: Generate a Client Secret

```bash
az ad app credential reset \
  --id <CLIENT_ID> \
  --append
```

Save `CLIENT_SECRET` and `TENANT_ID` from the output.

### Step 14: Grant Application Access to Key Vault

```bash
az role assignment create \
  --role "Key Vault Secrets User" \
  --assignee <CLIENT_ID> \
  --scope /subscriptions/<SUB_ID>/resourceGroups/secret-rotation-demo/providers/Microsoft.KeyVault/vaults/kv-secret-rotation-demo
```

### Step 15: Create and Deploy the Azure Function App

**Create the Function App:**

```bash
az functionapp create \
  --name kv-rotation-function-demo \
  --resource-group secret-rotation-demo \
  --storage-account kvrotationstorage123 \
  --consumption-plan-location centralindia \
  --runtime dotnet \
  --functions-version 4
```

**Login and publish:**

```bash
az login
cd SecretRotationFunction
func azure functionapp publish kv-rotation-function-demo
```

**Configure application settings:**

```bash
az functionapp config appsettings set \
  --name kv-rotation-function-demo \
  --resource-group secret-rotation-demo \
  --settings KEY_VAULT_URI=<YOUR_KEY_VAULT_URI>/
```

**Verify deployment (stream logs):**

```bash
func azure functionapp logstream kv-rotation-function-demo
```

### Step 16: Configure Event Grid Subscription

**Get the Key Vault Resource ID:**

```bash
az keyvault show \
  --name kv-secret-rotation-demo \
  --query id -o tsv
```

**Create the Event Grid subscription:**

```bash
az eventgrid event-subscription create \
  --name kv-secret-expiry-event \
  --source-resource-id <VAULT_ID> \
  --endpoint-type azurefunction \
  --endpoint /subscriptions/<SUB_ID>/resourceGroups/secret-rotation-demo/providers/Microsoft.Web/sites/kv-rotation-function-demo/functions/SecretRotationFunction \
  --included-event-types Microsoft.KeyVault.SecretNearExpiry
```

> **Note:** The `Microsoft.KeyVault.SecretNearExpiry` event is emitted **30 days before** the secret's expiration date. When testing, ensure the secret's expiry is more than 30 days in the future.

### Step 17: Verify Secret Rotation

```bash
az keyvault secret show \
  --vault-name kv-secret-rotation-demo \
  --name DbPassword \
  --query value -o tsv
```

A successful rotation will create a new version of the secret with an updated password.

---

## Blog Post

Read the full article for a detailed walkthrough: _[Building Automated Secret Rotation Using Azure Key Vault and .NET](#)_

---

## License

This project is provided as a proof-of-concept for educational purposes.

