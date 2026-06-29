namespace MailDataImporter.Models;

public class Attachment
{
    public Attachment(string? id, string? name, string? emailSubject, byte[]? content)
    {
        Id = id;
        Name = name;
        EmailSubject = emailSubject;
        Content = content;
    }
    
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? EmailSubject { get; init; }
    public byte[]? Content { get; init; }
}