using MailDataImporter.Classes;
using MailDataImporter.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MailDataImporter;

class Program
{
    static async Task Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            if (OperatingSystem.IsLinux())
                builder.AddSystemdConsole();
            else
                builder.AddConsole();
        });
        ILogger logger = loggerFactory.CreateLogger("MailDataImporter");
        
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddUserSecrets<AppConfig>(optional: true)
            .AddEnvironmentVariables(prefix: "TRI_")
            .Build();

        var config = new AppConfig();
        builder.Bind(config);
        config.Validate(logger);
        
        var graphClient = new GraphClient(config);
        var serviceClient = graphClient.GenerateServiceClient();

        var mail = new ExchangeMail(serviceClient, config);

        List<Email> emails = await mail.GetMailAsync(AttachmentType.TestReport);
        
        if (emails.Count == 0)
        {
            logger.LogInformation("There are no test report emails to process.");
            return;
        }
        
        List<Attachment> attachments = await mail.GetTestReportAsync(emails);
        List<TestReport> testReports = await AttachmentData.ProcessTestReports(attachments, logger, mail);

        int testReportRecordsAdded = 0;
        using (var query = new Query(config))
        {
            testReportRecordsAdded = query.InsertTestReportData(testReports, logger);
        }
        
        logger.LogInformation($"Imported {testReportRecordsAdded} rows to cert data records.");

        if (testReportRecordsAdded > 0)
        {
            await mail.MoveProcessedEmails(emails, AttachmentType.TestReport, logger);
            logger.LogInformation($"Moved {emails.Count} to imported test reports directory.");
        }
    }
}