using Mova.Domain.Common;
using Mova.Domain.Enums;

namespace Mova.Domain.Entities;
public class LegalDocument : BaseEntity
{
    public LegalDocumentType Type { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; set; }
    public bool IsCurrent  { get; set; }
}


public class UserConsent : BaseEntity
{
    public string UserPublicId { get; set; } = string.Empty;
    public long LegalDocumentId { get; set; }
    public LegalDocument LegalDocument { get; set; } = null!;

    public LegalDocumentType DocumentType { get; set; }
    public string DocumentVersion { get; set; } = string.Empty;
    public DateTimeOffset AcceptedAt { get; set; }
}