using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

const int NearExpiryLeadTimeDays = 30;

IConfiguration configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

// Key Vault config
string tenantId     = GetRequired(configuration, "KeyVault:TenantId");
string clientId     = GetRequired(configuration, "KeyVault:ClientId");
string clientSecret = GetRequired(configuration, "KeyVault:ClientSecret");
string keyVaultUrl  = GetRequired(configuration, "KeyVault:Url");
string secretName   = GetRequired(configuration, "KeyVault:SecretName");

// SQL config
string sqlServer   = GetRequired(configuration, "Sql:Server");
string sqlDatabase = GetRequired(configuration, "Sql:Database");
string sqlUserName = GetRequired(configuration, "Sql:UserName");

var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
var client = new SecretClient(new Uri(keyVaultUrl), credential);

KeyVaultSecret secret = await client.GetSecretAsync(secretName);
SecretProperties props = secret.Properties;

Console.WriteLine($"Secret name   : {secretName}");
Console.WriteLine($"Version       : {props.Version}");
Console.WriteLine($"Last rotated  : {Format(props.UpdatedOn ?? props.CreatedOn)}");
Console.WriteLine($"Expires on    : {Format(props.ExpiresOn)}");
Console.WriteLine($"Near-expiry   : {Format(props.ExpiresOn?.AddDays(-NearExpiryLeadTimeDays))} ({NearExpiryLeadTimeDays} days before expiry)");

var connectionString = new SqlConnectionStringBuilder
{
    DataSource = sqlServer,
    InitialCatalog = sqlDatabase,
    UserID = sqlUserName,
    Password = secret.Value,
    Encrypt = true,
    TrustServerCertificate = false,
    IntegratedSecurity = false,
    PersistSecurityInfo = false
}.ConnectionString;

// Uncomment to validate credentials against the database:
// await using var connection = new SqlConnection(connectionString);
// await connection.OpenAsync();

Console.WriteLine($"\nDB connection string built successfully: {connectionString}");

static string GetRequired(IConfiguration config, string key)
{
    string? value = config[key];
    return !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException($"Missing required configuration value: '{key}'");
}

static string Format(DateTimeOffset? value) => value?.ToString("u") ?? "N/A";