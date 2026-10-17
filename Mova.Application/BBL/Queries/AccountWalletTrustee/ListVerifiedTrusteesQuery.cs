using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.AccountWalletTrustee;

public sealed class ListVerifiedTrusteesQuery
{
    public sealed class Query : IRequest<BaseResult<ListVerifiedTrusteesResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;
    }

    public sealed class ListVerifiedTrusteesResponseDto
    {
        public IReadOnlyList<VerifiedTrusteeDto> Items { get; init; }
            = Array.Empty<VerifiedTrusteeDto>();

        public int TotalCount { get; init; }
    }

    public sealed class VerifiedTrusteeDto
    {
        public long Id { get; init; }
        public string Email { get; init; } = string.Empty;
        public string? Name { get; init; }
        public int WalletCount { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Query, BaseResult<ListVerifiedTrusteesResponseDto>>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<ListVerifiedTrusteesResponseDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var trustees = await _unitOfWork.Query<WalletTrustee>()
                .AsNoTracking()
                .Where(x =>
                    x.OwnerPublicId == request.UserPublicId &&
                    x.Status == WalletTrusteeStatus.Verified)
                .OrderByDescending(x => x.VerifiedAt)
                .Select(x => new
                {
                    x.Id,
                    x.Email,
                    x.Name,
                })
                .ToListAsync(cancellationToken);

            var trusteeIds = trustees.Select(t => t.Id).ToList();

            var walletCounts = trusteeIds.Count == 0
                ? new Dictionary<long, int>()
                : await _unitOfWork.Query<Wallet>()
                    .AsNoTracking()
                    .Where(w =>
                        w.WalletTrusteeId != null &&
                        trusteeIds.Contains(w.WalletTrusteeId.Value) &&
                        w.Type == WalletType.Trustee &&
                        w.Status == WalletStatus.Active)
                    .GroupBy(w => w.WalletTrusteeId!.Value)
                    .Select(g => new { TrusteeId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.TrusteeId, x => x.Count, cancellationToken);

            var items = trustees
                .Select(t => new VerifiedTrusteeDto
                {
                    Id = t.Id,
                    Email = t.Email,
                    Name = t.Name,
                    WalletCount = walletCounts.GetValueOrDefault(t.Id),
                })
                .ToList();

            return new BaseResult<ListVerifiedTrusteesResponseDto>(
                HttpStatusCode.OK,
                items.Count == 0
                    ? "No verified trustees yet."
                    : "Verified trustees retrieved.",
                new ListVerifiedTrusteesResponseDto
                {
                    Items = items,
                    TotalCount = items.Count,
                });
        }
    }
}