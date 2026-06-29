using MailDataImporter.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Graph.Users.Item.Messages.Item.Move;
using Microsoft.Graph.Users.Item.SendMail;
using Attachment = MailDataImporter.Models.Attachment;

namespace MailDataImporter.Classes;

public class ExchangeMail
{
    private GraphServiceClient _serviceClient;
    private AppConfig _config;
    private readonly string _mailbox;
    private List<string> _mailRecipients;
    
    public ExchangeMail(GraphServiceClient client,  AppConfig config)
    {
        _serviceClient = client;
        _config = config;
        _mailbox = config.Graph.MailboxUpn;

        _mailRecipients = new List<string>();
        foreach (var recipient in config.Graph.AlertMailboxes)
        {
            _mailRecipients.Add(recipient);
        }
    }
    
    /// <summary>
    /// A debugging tool that gets the id for every folder in the specified mailbox. Values output to console.
    /// </summary>
    public async Task GetMailboxFolderIdsAsync()
    {
        var folders = await _serviceClient
            .Users[_mailbox]
            .MailFolders
            .GetAsync();

        foreach (var folder in folders.Value.ToArray())
        {
            Console.WriteLine($"{folder.DisplayName}: {folder.Id}");
        }
    }
    
    public async Task SendMailGenericAsync(string message)
    {
        var requestBody = new SendMailPostRequestBody
        {
            Message = new Message
            {
                Subject = $"Importing Error",
                Importance = Importance.High,
                Body = new ItemBody
                {
                    ContentType = BodyType.Text,
                    Content = message,
                },
                ToRecipients = _config.Graph.AlertMailboxes
                    .Select(address => new Recipient
                    {
                        EmailAddress = new EmailAddress { Address = address }
                    })
                    .ToList(),
            },
            SaveToSentItems = true,
        };
        
        await _serviceClient.Users[_config.Graph.MailboxUpn].SendMail.PostAsync(requestBody);
    }

    public async Task SendMailAsync(string attachmentName)
    {
        var requestBody = new SendMailPostRequestBody
        {
            Message = new Message
            {
                Subject = $"Failed to Import {attachmentName}",
                Importance = Importance.High,
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = $"An error occured when processing attachment <b>{attachmentName}</b> and the import process was cancelled. Please review and contact the supplier.",
                },
                ToRecipients = _mailRecipients
                    .Select(address => new Recipient
                    {
                        EmailAddress = new EmailAddress { Address = address}
                    })
                    .ToList(),
            },
            SaveToSentItems = true,
        };

        await _serviceClient.Users[_mailbox].SendMail.PostAsync(requestBody);
    }
    
    /// <summary>
    /// Get all inbox emails for the specified mailbox and the subject keywords using the Microsoft Graph API
    /// </summary>
    /// <param name="type">The type of email to search for</param>
    /// <returns>A list of Email objects</returns>
    public async Task<List<Email>> GetMailAsync(AttachmentType type)
    {
        List<Email> mail = new List<Email>();

        var messages = await _serviceClient
            .Users[_mailbox]
            .MailFolders["inbox"]
            .Messages
            .GetAsync((configuration) =>
            {
                // Get only the message id, sender, and subject data for up to 50 mail items that have attachments.
                configuration.QueryParameters.Select = new string[] { "id", "sender", "subject" };
                configuration.QueryParameters.Top = 50;
                configuration.QueryParameters.Filter = "hasAttachments eq true";
            });

        // If there is no mail, return an empty/new list
        if (messages.OdataCount == 0)
        {
            return mail;
        }

        foreach (var message in messages.Value
                     .Where(a => a.Subject?.Replace("\u00A0", " ").Contains("Test Report", StringComparison.OrdinalIgnoreCase) == true))
        {
            mail.Add(new Email(message.Id, message.Subject.Replace("\u00A0", " "), message.Sender, true));
        }
        
        return mail;
    }

    /// <summary>
    /// Get all test report attachments from the specified mailbox's inbox using the Microsoft Graph API
    /// </summary>
    /// <param name="emails">The list of emails to process to get test reports from</param>
    /// <returns>A list of Attachment objects</returns>
    public async Task<List<Attachment>> GetTestReportAsync(List<Email> emails)
    {
        List<Attachment> data = new List<Attachment>();
        
        foreach (var email in emails)
        {
            var attachments = await _serviceClient
                .Users[_mailbox]
                .Messages[email.Id]
                .Attachments
                .GetAsync((configuration) =>
                {
                    // Filter the email attachments to only get File objects and exclude everything else that could be attached
                    configuration.QueryParameters.Filter = "isof('microsoft.graph.fileAttachment')";
                });

            List<string> attachmentIds = new List<string>();
            
            foreach (var attachment in attachments.Value
                         .Where(a => Path.GetExtension(a.Name)!.Equals(_config.Import.AllowedExtension, StringComparison.OrdinalIgnoreCase)))
            {
                attachmentIds.Add(attachment.Id);
            }

            foreach (var attachmentId in attachmentIds)
            {
                data.Add(await GetAttachmentContentAsync(email, attachmentId));
            }
        }

        return data;
    }

    /// <summary>
    /// Get the file content from a list of attachments using the Microsoft Graph API
    /// </summary>
    /// <param name="email">The email object to get the attachment content from</param>
    /// <param name="attachmentId">The id of the attachment to process</param>
    /// <returns>A new Attachment object</returns>
    private async Task<Attachment> GetAttachmentContentAsync(Email email, string attachmentId)
    {
        var attachmentData = await _serviceClient
            .Users[_config.Graph.MailboxUpn]
            .Messages[email.Id]
            .Attachments[attachmentId]
            .GetAsync();

        var fileAttachment = attachmentData as Microsoft.Graph.Models.FileAttachment;

        return new Attachment(fileAttachment.Id, fileAttachment.Name, email.Subject, fileAttachment.ContentBytes);
    }

    /// <summary>
    /// Move processed emails from inbox to the target mailbox folder
    /// </summary>
    /// <param name="emails">The list of emails to process to get test reports from</param>
    /// <param name="emailType">The type of email to move</param>
    /// <param name="logger">The system logging interface to write log messages back to the system.</param>
    public async Task MoveProcessedEmails(List<Email> emails, AttachmentType emailType, ILogger  logger)
    {
        foreach (var email in emails)
        {
            MovePostRequestBody body;
        
            switch (emailType)
            {
                case AttachmentType.TestReport:
                    body = new MovePostRequestBody
                    {
                        DestinationId = _config.Graph.ImportedTestReportsFolderId,
                    };
                    break;
                default:
                    logger.LogError("An unexpected AttachmentType value was supplied. Mail movement cancelled.");
                    return;
            }
            
            try
            {
                var result = await _serviceClient
                    .Users[_mailbox]
                    .Messages[email.Id]
                    .Move
                    .PostAsync(body);

                if (result?.ParentFolderId != body.DestinationId)
                {
                    logger.LogWarning($"Moved message {email.Id} to wrong folder: {result.ParentFolderId}");
                }
            }
            catch (ODataError e)
            {
                var errorMessage =
                    $"Failed to move message {email.Subject} ({email.Id}): [{e.Error?.Code}] {e.Error?.Message}";
                logger.LogError(errorMessage);
                await SendMailGenericAsync(errorMessage);
            }
        }
    }
}