using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.BanksAccount;

public sealed class PaymentCallback
{
    public sealed class Query : IRequest<BaseResult<PaymentCallbackDto>>
    {
        public string? Reference { get; init; }
    }

    public sealed class PaymentCallbackDto
    {
        public string Reference { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public DateTimeOffset? CreatedAt { get; init; }
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<PaymentCallbackDto>>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<PaymentCallbackDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Reference))
            {
                return new BaseResult<PaymentCallbackDto>(
                    HttpStatusCode.BadRequest,
                    "Payment reference is required."
                );
            }

            var transaction = await _unitOfWork
                .Query<Transaction>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Reference == request.Reference,
                    cancellationToken);

            if (transaction is null)
            {
                return new BaseResult<PaymentCallbackDto>(
                    HttpStatusCode.NotFound,
                    "Transaction not found."
                );
            }

            return new BaseResult<PaymentCallbackDto>(
                HttpStatusCode.OK,
                "Transaction retrieved successfully",
                new PaymentCallbackDto
                {
                    Reference = transaction.Reference ?? request.Reference,
                    Status = transaction.Status.ToString(),
                    Amount = transaction.Amount.ToDecimal(),
                    CreatedAt = transaction.CreatedAt
                }
            );
        }
    }
}