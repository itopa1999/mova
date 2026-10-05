using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.BanksAccount;

public sealed class GetWithdrawalAvailableBalancesQuery
{
    public sealed class Query : IRequest<BaseResult<Response>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;
    }

    /// <summary>Available withdrawal balance across the user's wallets.</summary>
    public sealed record Response(
        decimal TotalAvailableAmount,
        IReadOnlyList<WalletAvailableBalance> Wallets);

    /// <summary>Available withdrawal balance for one wallet.</summary>
    public sealed record WalletAvailableBalance(
        long WalletId,
        string WalletName,
        decimal AvailableAmount);

    public sealed class Handler(IUnitOfWork unitOfWork)
        : IRequestHandler<Query, BaseResult<Response>>
    {
        public async Task<BaseResult<Response>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var wallets = await unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .Where(wallet =>
                    wallet.UserPublicId == request.UserPublicId &&
                    wallet.AvailableAmount.MinorUnits > 0)
                .OrderBy(wallet => wallet.Name)
                .ThenBy(wallet => wallet.Id)
                .ToListAsync(cancellationToken);

            var walletBalances = wallets
                .Select(wallet => new WalletAvailableBalance(
                    wallet.Id,
                    wallet.Name,
                    wallet.AvailableAmount.ToDecimal()))
                .ToArray();

            var response = new Response(
                walletBalances.Sum(wallet => wallet.AvailableAmount),
                walletBalances);

            return new BaseResult<Response>(
                HttpStatusCode.OK,
                "Available withdrawal balances retrieved successfully.",
                response);
        }
    }
}
