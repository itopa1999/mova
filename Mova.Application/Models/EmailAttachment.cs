using Mova.Application.Models;
namespace Mova.Application.Models;

public sealed class EmailAttachment
{
    public required string FileName { get; init; }

    public required byte[] Data { get; init; }

    public required string ContentType { get; init; }
}