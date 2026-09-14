using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Commands.FeatureFlags;

public sealed class ToggleFeatureFlag
{
    public sealed class Command
        : IRequest<BaseResult<ToggleFeatureFlagDto>>
    {
        public long Id { get; init; }

        public bool IsEnabled { get; init; }
    }

    public sealed class ToggleFeatureFlagDto
    {
        public long Id { get; set; }
        public FeatureFlagName Name { get; set; }
        public bool IsEnabled { get; set; }
        public DateTimeOffset? ModifiedAt { get; set; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<ToggleFeatureFlagDto>>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<ToggleFeatureFlagDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            var flag = await _unitOfWork.Query<FeatureFlag>()
                .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);

            if (flag is null)
            {
                return new BaseResult<ToggleFeatureFlagDto>(
                    HttpStatusCode.NotFound,
                    $"Feature flag '{request.Id}' not found.");
            }

            if (flag.IsEnabled == request.IsEnabled)
            {
                return new BaseResult<ToggleFeatureFlagDto>(
                    HttpStatusCode.OK,
                    $"Feature flag is already {(request.IsEnabled ? "enabled" : "disabled")}.",
                    new ToggleFeatureFlagDto
                    {
                        Id = flag.Id,
                        Name = flag.Name,
                        IsEnabled = flag.IsEnabled,
                        ModifiedAt = flag.ModifiedAt,
                    });
            }

            flag.IsEnabled = request.IsEnabled;

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new BaseResult<ToggleFeatureFlagDto>(
                HttpStatusCode.OK,
                $"Feature flag {(request.IsEnabled ? "enabled" : "disabled")} successfully.",
                new ToggleFeatureFlagDto
                {
                    Id = flag.Id,
                    Name = flag.Name,
                    IsEnabled = flag.IsEnabled,
                    ModifiedAt = flag.ModifiedAt,
                });
        }
    }
}