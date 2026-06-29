using Microsoft.Extensions.Logging;

namespace MailDataImporter;

public class AppConfig
{
    public GraphSettings Graph { get; set; } = new();
    public ImportSettings Import { get; set; } = new();
    public SqlSettings Sql { get; set; } = new();
    
    public class GraphSettings
    {
        public string TenantId { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";
        public string MailboxUpn { get; set; } = "";
        public string ImportedTestReportsFolderId { get; set; } = "";
        public List<string> AlertMailboxes { get; set; } = new List<string>();
    }

    public class ImportSettings
    {
        public string AllowedExtension { get; set; } = ".txt";
    }

    public class SqlSettings
    {
        public string DataSource { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string InitialCatalog { get; set; } = "";
    }

    /// <summary>
    /// Validate the application configuration in appsettings.json
    /// </summary>
    /// <param name="logger">The system logging interface to write log messages back to the system.</param>
    /// <exception cref="InvalidOperationException">Throws an error if any validated property is empty.</exception>
    public void Validate(ILogger logger)
    {
        var missing = new List<string>();
        
        if (string.IsNullOrWhiteSpace(Graph.TenantId))
            missing.Add("Graph:TenantId");
        
        if (string.IsNullOrWhiteSpace(Graph.ClientId))
            missing.Add("Graph:ClientId");

        if (string.IsNullOrWhiteSpace(Graph.MailboxUpn))
            missing.Add("Graph:MailboxUpn");
        
        if (string.IsNullOrWhiteSpace(Graph.ImportedTestReportsFolderId))
            missing.Add("Graph:ImportedTestReportsFolderId");
        
        if (string.IsNullOrWhiteSpace(Import.AllowedExtension))
            missing.Add("Import:AllowedExtension");
        
        if (string.IsNullOrWhiteSpace(Sql.DataSource))
            missing.Add("Sql:DataSource");
        
        if (string.IsNullOrWhiteSpace(Sql.Username))
            missing.Add("Sql:Username");
        
        if (string.IsNullOrWhiteSpace(Sql.InitialCatalog))
            missing.Add("Sql:InitialCatalog");

        if (missing.Count > 0)
        {
            foreach (var property in missing)
            {
                logger.LogCritical($"Property {property} is missing!");
            }
            
            throw new InvalidOperationException(
                $"Missing required config keys: {string.Join(", ", missing)}");
        }
    }
}