using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.MovaAPIs;

public sealed class GetFeatureFlags
{
    public sealed class Query
        : IRequest<BaseResult<List<FeatureFlagDto>>>
    {
    }
    public sealed class FeatureFlagDto
    {
        public long Id { get; set; }
        public FeatureFlagName Name { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public string? Metadata { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? ModifiedAt { get; set; }
    }

    public sealed class Handler
        : IRequestHandler<Query, BaseResult<List<FeatureFlagDto>>>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<List<FeatureFlagDto>>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var flags = await _unitOfWork.Query<FeatureFlag>()
                .AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new FeatureFlagDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Description = x.Description,
                    IsEnabled = x.IsEnabled,
                    Metadata = x.Metadata,
                    CreatedAt = x.CreatedAt,
                    ModifiedAt = x.ModifiedAt,
                })
                .ToListAsync(cancellationToken);

            return new BaseResult<List<FeatureFlagDto>>(
                HttpStatusCode.OK,
                "Feature flags retrieved successfully.",
                flags);
        }
    }
}