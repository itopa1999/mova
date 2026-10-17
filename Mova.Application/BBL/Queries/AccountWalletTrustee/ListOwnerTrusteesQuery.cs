using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.AccountWalletTrustee;

public sealed class ListOwnerTrusteesQuery
{
    public sealed class Query : IRequest<BaseResult<ListOwnerTrusteesResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;
    }

    public sealed class ListOwnerTrusteesResponseDto
    {
        public IReadOnlyList<TrusteeListItemDto> Items { get; init; } = Array.Empty<TrusteeListItemDto>();
        public int PendingCount { get; init; }
        public int VerifiedCount { get; init; }
    }

    public sealed class TrusteeListItemDto
    {
        public long Id { get; init; }
        public string Email { get; init; } = string.Empty;
        public string? Name { get; init; }
        public string Status { get; init; } = string.Empty;
        public DateTimeOffset InvitedAt { get; init; }
        public DateTimeOffset? VerifiedAt { get; init; }
        public DateTimeOffset? TokenExpiresAt { get; init; }
        public int WalletCount { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Query, BaseResult<ListOwnerTrusteesResponseDto>>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<ListOwnerTrusteesResponseDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var trustees = await _unitOfWork.Query<WalletTrustee>()
                .AsNoTracking()
                .Where(x => x.OwnerPublicId == request.UserPublicId)
                .OrderByDescending(x => x.InvitedAt)
                .Select(x => new
                {
                    x.Id,
                    x.Email,
                    x.Name,
                    x.Status,
                    x.InvitedAt,
                    x.VerifiedAt,
                    x.TokenExpiresAt,
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
                .Select(t => new TrusteeListItemDto
                {
                    Id = t.Id,
                    Email = t.Email,
                    Name = t.Name,
                    Status = t.Status.ToString(),
                    InvitedAt = t.InvitedAt,
                    VerifiedAt = t.VerifiedAt,
                    TokenExpiresAt = t.TokenExpiresAt,
                    WalletCount = walletCounts.GetValueOrDefault(t.Id),
                })
                .ToList();

            var response = new ListOwnerTrusteesResponseDto
            {
                Items = items,
                PendingCount = items.Count(i =>
                    string.Equals(i.Status, nameof(WalletTrusteeStatus.Pending), StringComparison.OrdinalIgnoreCase)),
                VerifiedCount = items.Count(i =>
                    string.Equals(i.Status, nameof(WalletTrusteeStatus.Verified), StringComparison.OrdinalIgnoreCase)),
            };

            return new BaseResult<ListOwnerTrusteesResponseDto>(
                HttpStatusCode.OK,
                "Trustees retrieved.",
                response);
        }
    }
}