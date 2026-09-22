using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.WalletTemplates;

public sealed class ListWalletTemplatesQuery
{
    public sealed class Query : IRequest<BaseResult<ListWalletTemplatesResponseDto>>
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;

        /// <summary>
        /// Optional. Case-insensitive search on name, description, and tags.
        /// </summary>
        public string? Search { get; set; }

        /// <summary>
        /// Optional. Filter by wallet category.
        /// </summary>
        public long? CategoryId { get; set; }
    }

    public sealed class ListWalletTemplatesResponseDto : BasePaginationResponse<WalletTemplateDto>
    {
    }

    public sealed class WalletTemplateDto
    {
        public long Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public long CategoryId { get; init; }

        public string CategoryName { get; init; } = string.Empty;

        public string CategoryIcon { get; init; } = string.Empty;

        public decimal DefaultTargetAmount { get; init; }

        public decimal DefaultReleaseAmount { get; init; }

        public string DefaultFrequency { get; init; } = string.Empty;

        public string DefaultFrequencyConfig { get; init; } = string.Empty;

        public string DefaultPayoutDestination { get; init; } = string.Empty;

        public string IconName { get; init; } = string.Empty;

        public List<string> Tags { get; init; } = new();

        public int SortOrder { get; init; }

        public int UsageCount { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Query, BaseResult<ListWalletTemplatesResponseDto>>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<ListWalletTemplatesResponseDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            try
            {
                var page = request.Page < 1 ? 1 : request.Page;
                var pageSize = request.PageSize < 1 ? 20 : request.PageSize;
                if (pageSize > 100) pageSize = 100;

                var query = _unitOfWork.Query<WalletTemplate>()
                    .Where(t => t.IsActive)
                    .Include(t => t.Category)
                    .AsQueryable();

                // ─── Category filter ─────────────────────────
                if (request.CategoryId.HasValue && request.CategoryId.Value > 0)
                {
                    query = query.Where(t => t.CategoryId == request.CategoryId.Value);
                }

                // ─── Search filter ───────────────────────────
                if (!string.IsNullOrWhiteSpace(request.Search))
                {
                    var term = request.Search.Trim().ToLowerInvariant();

                    query = query.Where(t =>
                        t.Name.ToLower().Contains(term) ||
                        t.Description.ToLower().Contains(term) ||
                        t.Tags.Any(tag => tag.ToLower().Contains(term)));
                }

                var totalCount = await query.CountAsync(cancellationToken);

                var templates = await query
                    .OrderBy(t => t.SortOrder)
                    .ThenBy(t => t.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync(cancellationToken);

                var items = templates.Select(t => new WalletTemplateDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    Description = t.Description,
                    CategoryId = t.CategoryId,
                    CategoryName = t.Category?.Name ?? "Other",
                    CategoryIcon = t.Category?.Icon ?? "FileText",
                    DefaultTargetAmount = t.DefaultTargetAmount.ToDecimal(),
                    DefaultReleaseAmount = t.DefaultReleaseAmount.ToDecimal(),
                    DefaultFrequency = t.DefaultFrequency.ToString(),
                    DefaultFrequencyConfig = t.DefaultFrequencyConfig,
                    DefaultPayoutDestination = t.DefaultPayoutDestination
                        .ToString()
                        .ToLowerInvariant(),
                    IconName = t.IconName,
                    Tags = t.Tags.ToList(),
                    SortOrder = t.SortOrder,
                    UsageCount = t.UsageCount,
                }).ToList();

                var totalPages = totalCount == 0
                    ? 0
                    : (int)Math.Ceiling(totalCount / (double)pageSize);

                var response = new ListWalletTemplatesResponseDto
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                    TotalPages = totalPages,
                    Items = items,
                };

                return new BaseResult<ListWalletTemplatesResponseDto>(
                    HttpStatusCode.OK,
                    "Wallet templates retrieved successfully.",
                    response);
            }
            catch (Exception)
            {
                return new BaseResult<ListWalletTemplatesResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving wallet templates. Please try again later.");
            }
        }
    }
}