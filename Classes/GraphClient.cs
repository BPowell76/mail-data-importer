using Azure.Identity;
using Microsoft.Graph;

namespace MailDataImporter.Classes;

public class GraphClient
{
    private string tenantId;
    private string clientId;
    private string clientSecret;
    private string[] scopes;
    private ClientSecretCredential clientSecretCredential;
    private ClientAssertionCredentialOptions clientOptions;

    public GraphClient(AppConfig config)
    {
        scopes = new[] { "https://graph.microsoft.com/.default" };
        clientOptions = new ClientAssertionCredentialOptions()
        {
            AuthorityHost = AzureAuthorityHosts.AzurePublicCloud,
        };
        
        tenantId = config.Graph.TenantId;
        clientId = config.Graph.ClientId;
        clientSecret = config.Graph.ClientSecret;
        clientSecretCredential = new ClientSecretCredential(tenantId, clientId, clientSecret, clientOptions);
    }

    /// <summary>
    /// Create a new Microsoft Graph API Service Client object
    /// </summary>
    /// <returns>A Graph Service Client object</returns>
    public GraphServiceClient GenerateServiceClient()
    {
        return new GraphServiceClient(clientSecretCredential, scopes);  
    }
}