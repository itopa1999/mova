using Microsoft.AspNetCore.Http;

namespace Mova.Application.Interfaces.Storage;

public interface IFileStorageService
{
    Task<string> UploadFileAsync(IFormFile file, string folderName, string? fileNamePrefix = null, CancellationToken cancellationToken = default);
}