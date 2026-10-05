using System.ComponentModel.DataAnnotations;
using MediatR;
using Mova.Application.BBL.Commands.AccountWallet;
using Mova.Application.BBL.Commands.TransactionPin;
using Mova.Application.BBL.Queries.BanksAccount;
using Mova.Application.Behaviors;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Tests.Validation;

public sealed class RequestValidationBehaviorTests
{
    private readonly RequestValidationBehavior<ValidationRequest, int> _behavior = new();

    [Fact]
    public async Task Handle_WhenRequestDoesNotMeetAnnotations_ThrowsValidationException()
    {
        var request = new ValidationRequest
        {
            Name = string.Empty,
            Amount = 0
        };

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _behavior.Handle(
                request,
                () => Task.FromResult(1),
                CancellationToken.None));

        Assert.Contains(nameof(ValidationRequest.Name), exception.Message);
        Assert.Contains(nameof(ValidationRequest.Amount), exception.Message);
    }

    [Fact]
    public async Task Handle_WhenRequestIsValid_InvokesNextBehavior()
    {
        var request = new ValidationRequest
        {
            Name = "Valid",
            Amount = 10
        };
        var nextCalled = false;

        var result = await _behavior.Handle(
            request,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(7);
            },
            CancellationToken.None);

        Assert.True(nextCalled);
        Assert.Equal(7, result);
    }

    [Fact]
    public async Task Handle_WhenPinIsNotSixDigits_RejectsCommandBeforeHandler()
    {
        var request = new VerifyPinCommand.Command { Pin = "12ab56" };
        var behavior = new RequestValidationBehavior<VerifyPinCommand.Command, BaseResult>();

        await Assert.ThrowsAsync<ValidationException>(() =>
            behavior.Handle(
                request,
                () => Task.FromResult(new BaseResult()),
                CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenWalletTargetIsBelowMinimum_RejectsCommandBeforeHandler()
    {
        var request = new CreateWalletCommand.Command
        {
            Name = "Savings",
            CategoryId = 1,
            TargetAmount = 1_999m,
            Frequency = ReleaseFrequency.Daily,
            FrequencyConfig = "{}",
            AmountToBeReleased = 100m,
            StartDate = DateTimeOffset.UtcNow,
            PayoutDestination = "bank"
        };
        var behavior = new RequestValidationBehavior<
            CreateWalletCommand.Command,
            BaseResult<CreateWalletCommand.CreateWalletResponseDto>>();

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            behavior.Handle(
                request,
                () => Task.FromResult(
                    new BaseResult<CreateWalletCommand.CreateWalletResponseDto>()),
                CancellationToken.None));

        Assert.Contains(nameof(CreateWalletCommand.Command.TargetAmount), exception.Message);
    }

    [Fact]
    public async Task Handle_WhenTransactionDateRangeIsReversed_RejectsQueryBeforeHandler()
    {
        var request = new GetTransactions.Query
        {
            FromDate = DateTimeOffset.UtcNow,
            ToDate = DateTimeOffset.UtcNow.AddDays(-1)
        };
        var behavior = new RequestValidationBehavior<
            GetTransactions.Query,
            BaseResult<GetTransactions.PaginatedTransactionsDto>>();

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            behavior.Handle(
                request,
                () => Task.FromResult(
                    new BaseResult<GetTransactions.PaginatedTransactionsDto>()),
                CancellationToken.None));

        Assert.Contains("start date", exception.Message);
    }

    private sealed class ValidationRequest : IRequest<int>
    {
        [Required, MaxLength(20)]
        public string Name { get; init; } = string.Empty;

        [Range(1, 100)]
        public int Amount { get; init; }
    }
}
