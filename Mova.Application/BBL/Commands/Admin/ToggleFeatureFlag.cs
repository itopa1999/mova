using System.ComponentModel.DataAnnotations;
using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.Admin;

public sealed class ToggleFeatureFlag
{
    public sealed class Command
        : IRequest<BaseResult<ToggleFeatureFlagDto>>
    {
        [Range(1, long.MaxValue)]
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
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<BaseResult<ToggleFeatureFlagDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "ToggleFeatureFlag",
                ("FeatureFlagId", request.Id),
                ("IsEnabled", request.IsEnabled));

            var flag = await _unitOfWork.Query<FeatureFlag>()
                .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);

            if (flag is null)
            {
                op.Warning($"Feature flag not found: {request.Id}");
                return new BaseResult<ToggleFeatureFlagDto>(
                    HttpStatusCode.NotFound,
                    $"Feature flag '{request.Id}' not found.");
            }

            if (flag.IsEnabled == request.IsEnabled)
            {
                op.Success(
                    $"Feature flag '{flag.Name}' is already " +
                    $"{(request.IsEnabled ? "enabled" : "disabled")}.");

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

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            flag.IsEnabled = request.IsEnabled;

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            op.Success(
                $"Feature flag '{flag.Name}' " +
                $"{(request.IsEnabled ? "enabled" : "disabled")}.");

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