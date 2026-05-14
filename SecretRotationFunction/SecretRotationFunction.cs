using Azure.Identity;
using Azure.Messaging.EventGrid;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace SecretRotationFunction
{
    public class SecretRotationFunction
    {
        private readonly ILogger<SecretRotationFunction> _logger;

        public SecretRotationFunction(ILogger<SecretRotationFunction> logger)
        {
            _logger = logger;
        }

        [Function(nameof(SecretRotationFunction))]
        public async Task Run([EventGridTrigger] EventGridEvent eventGridEvent)
        {
            await RotateSecretAsync(eventGridEvent.Id, eventGridEvent.Subject);
        }

        [Function(nameof(TestRotateSecretAsync))]
        public async Task<HttpResponseData> TestRotateSecretAsync(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req)
        {
            await RotateSecretAsync(eventId: null, eventSubject: null);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
            await response.WriteStringAsync("Secret rotation test completed.");
            return response;
        }

        private async Task RotateSecretAsync(string? eventId, string? eventSubject)
        {
            _logger.LogInformation("Secret rotation started. EventId={EventId}, Subject={Subject}", eventId, eventSubject);

            try
            {
                var keyVaultUri = Environment.GetEnvironmentVariable("KEY_VAULT_URI");

                if (string.IsNullOrWhiteSpace(keyVaultUri))
                    throw new InvalidOperationException("Missing required app setting 'KEY_VAULT_URI'.");

                var client = new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential());

                string newPassword = $"Password@{Random.Shared.Next(1000, 9999)}";
                var expiresOn = DateTimeOffset.UtcNow.AddDays(50);

                var secret = new KeyVaultSecret("DbPassword", newPassword)
                {
                    Properties = { ExpiresOn = expiresOn }
                };

                await client.SetSecretAsync(secret);

                // Uncomment to also update the database user password:
                // UpdateDatabaseUserPassword(newPassword);

                _logger.LogInformation("Secret rotated successfully. Expires={ExpiresOn:O}", expiresOn);
            }
            catch (Azure.RequestFailedException ex)
            {
                _logger.LogError(ex, "Key Vault request failed. Status={Status}", ex.Status);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Secret rotation failed unexpectedly.");
                throw;
            }
        }
    }
}
