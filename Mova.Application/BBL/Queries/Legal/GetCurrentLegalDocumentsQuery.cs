using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using Mova.Shared.Common.Results;

namespace Mova.Application.BBL.Queries.Legal;

public sealed class GetCurrentLegalDocumentsQuery
{
    public sealed class Query : IRequest<BaseResult<CurrentLegalDocumentsResponse>>
    {
        public LegalDocumentType? Type { get; init; }
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<CurrentLegalDocumentsResponse>>
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

        private static readonly HashSet<LegalDocumentType> RequiredTypes = new()
        {
            LegalDocumentType.Agreement,
            LegalDocumentType.Privacy,
            LegalDocumentType.Terms,
        };

        private readonly IUnitOfWork _unitOfWork;
        private readonly ICacheService _cacheService;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            ICacheService cacheService,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _cacheService = cacheService;
            _logger = logger;
        }

        public async Task<BaseResult<CurrentLegalDocumentsResponse>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var cacheKey = CacheKeys.CurrentLegalDocuments(request.Type?.ToString());

            var dto = await _cacheService.GetOrSetAsync(
                cacheKey,
                ct => LoadFromDbAsync(request.Type, ct),
                timeout: CacheDuration,
                cancellationToken: cancellationToken);

            if (dto is null)
            {
                return new BaseResult<CurrentLegalDocumentsResponse>(
                    HttpStatusCode.InternalServerError,
                    request.Type is null
                        ? "Legal documents are not yet configured. Please contact support."
                        : $"{request.Type} document is not yet configured. Please contact support.");
            }

            if (dto.Documents.Count == 0)
            {
                return new BaseResult<CurrentLegalDocumentsResponse>(
                    HttpStatusCode.NotFound,
                    $"No current document found for type {request.Type}.");
            }

            return new BaseResult<CurrentLegalDocumentsResponse>(
                HttpStatusCode.OK,
                "Legal documents retrieved successfully.",
                dto);
        }

        private async Task<CurrentLegalDocumentsResponse?> LoadFromDbAsync(
            LegalDocumentType? type,
            CancellationToken cancellationToken)
        {
            var query = _unitOfWork.Query<LegalDocument>()
                .AsNoTracking()
                .Where(d => d.IsCurrent);

            if (type.HasValue)
            {
                query = query.Where(d => d.Type == type.Value);
            }

            var docs = await query
                .OrderBy(d => d.Type)
                .Select(d => new LegalDocumentDto
                {
                    Type = d.Type,
                    TypeName = d.Type.ToString(),
                    Version = d.Version,
                    Title = d.Title,
                    Content = d.Content,
                    PublishedAt = d.PublishedAt,
                })
                .ToListAsync(cancellationToken);

            if (type.HasValue)
            {
                if (docs.Count == 0)
                {
                    _logger.LogError(
                        "Missing current legal document for type {Type}.",
                        type.Value);

                    return null;
                }

                return new CurrentLegalDocumentsResponse { Documents = docs };
            }

            var presentTypes = docs.Select(d => d.Type).ToHashSet();
            var missing = RequiredTypes.Except(presentTypes).ToList();

            if (missing.Count > 0)
            {
                _logger.LogError(
                    "Missing current legal documents: {MissingTypes}. " +
                    "Onboarding cannot proceed until all three are published.",
                    string.Join(", ", missing));

                return null;
            }

            return new CurrentLegalDocumentsResponse { Documents = docs };
        }
    }
}

public sealed class CurrentLegalDocumentsResponse
{
    public List<LegalDocumentDto> Documents { get; set; } = new();
}

public sealed class LegalDocumentDto
{
    public LegalDocumentType Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; set; }
}