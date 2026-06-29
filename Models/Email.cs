using Microsoft.Graph.Models;

namespace MailDataImporter.Models;

public sealed class Email
{
    public Email(string messageId, string subject, Recipient sender, bool hasAttachments)
    {
        Id = messageId;
        Subject = subject;
        Sender = sender.EmailAddress.Address;
        SenderName = sender.EmailAddress.Name;
        HasAttachments = hasAttachments;
    }
    
    public string Id { get; init; }
    public string Subject { get; init; }
    public string Sender { get; init; }
    public string SenderName { get; init; }
    public bool HasAttachments { get; init; }
}