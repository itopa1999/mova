using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Identity;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.MovaAPIs;

public sealed class HomeQuery
{
    public sealed class Query : IRequest<BaseResult<HomeQueryDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;
    }

    public sealed class HomeQueryDto
    {
        public Balance Balance { get; set; } = new();
        public List<ReleasedSchedulesToday> TodayReleased { get; set; } = new();
        public List<Wallets> Wallets { get; set; } = new();
    }

    public sealed class Balance
    {
        public decimal UserBalance { get; set; }
        public decimal TotalAvailableAmount { get; set; }
        public decimal TotalLockedAmount { get; set; }
    }

    public sealed class ReleasedSchedulesToday
    {
        public string WalletName { get; set; } = string.Empty;
        public decimal ReleasedAmount { get; set; }
        public DateTimeOffset ReleasedAt { get; set; }
    }

    public sealed class Wallets
    {
        public string WalletName { get; set; } = string.Empty;
        public decimal TargetAmount { get; set; }
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<HomeQueryDto>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;

        public Handler(
            IUnitOfWork unitOfWork,
            IIdentityService identityService)
        {
            _unitOfWork = unitOfWork;
            _identityService = identityService;
        }

        public async Task<BaseResult<HomeQueryDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                return new BaseResult<HomeQueryDto>(
                    HttpStatusCode.BadRequest,
                    "User public ID is required.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user == null)
            {
                return new BaseResult<HomeQueryDto>(
                    HttpStatusCode.NotFound,
                    "User not found.");
            }

            try
            {
                var wallets = await _unitOfWork.Query<Wallet>()
                    .Where(w => w.UserPublicId == request.UserPublicId 
                                && w.Status == WalletStatus.Active)
                    .OrderByDescending(w => w.CreatedAt)
                    .Take(5)
                    .ToListAsync(cancellationToken);

                var today = DateTimeOffset.UtcNow.Date;

                var todayReleased = await (
                    from sr in _unitOfWork.Query<ScheduledRelease>()
                    join w in _unitOfWork.Query<Wallet>() on sr.WalletId equals w.Id
                    where sr.Status == ReleaseStatus.Released
                          && sr.ReleasedAt != null
                          && sr.ReleasedAt.Value.Date == today
                          && w.UserPublicId == request.UserPublicId
                          && w.Status == WalletStatus.Active
                    orderby sr.ReleasedAt descending
                    select new ReleasedSchedulesToday
                    {
                        WalletName = w.Name,
                        ReleasedAmount = sr.Amount.ToDecimal(),
                        ReleasedAt = sr.ReleasedAt ?? DateTimeOffset.UtcNow
                    })
                    .Take(5)
                    .ToListAsync(cancellationToken);

                var allWallets = await _unitOfWork.Query<Wallet>()
                    .Where(w => w.UserPublicId == request.UserPublicId 
                                && w.Status == WalletStatus.Active)
                    .ToListAsync(cancellationToken);

                var totalAvailableAmount = allWallets.Sum(w => w.AvailableAmount.ToDecimal());
                var totalLockedAmount = allWallets.Sum(w => w.LockedAmount.ToDecimal());

                var walletSummaries = wallets.Select(w => new Wallets
                {
                    WalletName = w.Name,
                    TargetAmount = w.TargetAmount.ToDecimal()
                }).ToList();

                var result = new HomeQueryDto
                {
                    Balance = new Balance
                    {
                        UserBalance = user.Balance.ToDecimal(),
                        TotalAvailableAmount = totalAvailableAmount,
                        TotalLockedAmount = totalLockedAmount
                    },
                    TodayReleased = todayReleased,
                    Wallets = walletSummaries
                };

                return new BaseResult<HomeQueryDto>(
                    HttpStatusCode.OK,
                    "Home data retrieved successfully.",
                    result);
            }
            catch
            {
                return new BaseResult<HomeQueryDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving your home data. Please try again later.");
            }
        }
    }
}