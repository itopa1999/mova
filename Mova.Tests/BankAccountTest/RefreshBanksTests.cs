using System.Net;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.BanksAccount;
using Mova.Application.Interfaces.Payment;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class RefreshBanksTests : BaseTest
{
    private readonly Mock<IBankService> _bankService = new();

    private RefreshBanks.Handler CreateHandler()
    {
        return new RefreshBanks.Handler(_bankService.Object);
    }

    private RefreshBanks.Command CreateCommand()
    {
        return new RefreshBanks.Command();
    }

    // ---------------------------------------------------------
    // 1. Happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidRequest_ReturnsSuccess()
    {
        _bankService
            .Setup(x => x.RefreshBanksAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Bank Refreshed successfully.", result.Message);
    }

    [Fact]
    public async Task Handle_WithValidRequest_CallsBankServiceOnce()
    {
        _bankService
            .Setup(x => x.RefreshBanksAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _bankService.Verify(
            x => x.RefreshBanksAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_PassesCancellationToken()
    {
        using var cts = new CancellationTokenSource();

        _bankService
            .Setup(x => x.RefreshBanksAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), cts.Token);

        _bankService.Verify(
            x => x.RefreshBanksAsync(cts.Token),
            Times.Once);
    }

    // ---------------------------------------------------------
    // 2. Failure propagation
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenBankServiceThrows_PropagatesException()
    {
        _bankService
            .Setup(x => x.RefreshBanksAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Paystack unavailable"));

        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(CreateCommand(), default));
    }

    [Fact]
    public async Task Handle_WhenBankServiceThrowsHttpRequestException_Propagates()
    {
        _bankService
            .Setup(x => x.RefreshBanksAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network failure"));

        var handler = CreateHandler();

        await Assert.ThrowsAsync<HttpRequestException>(
            () => handler.Handle(CreateCommand(), default));
    }

    [Fact]
    public async Task Handle_WhenBankServiceThrows_TokenIsNotSwallowed()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _bankService
            .Setup(x => x.RefreshBanksAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var handler = CreateHandler();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => handler.Handle(CreateCommand(), cts.Token));
    }

    // ---------------------------------------------------------
    // 3. Idempotency / repeat invocation
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenCalledTwice_CallsBankServiceTwice()
    {
        _bankService
            .Setup(x => x.RefreshBanksAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();

        var first = await handler.Handle(CreateCommand(), default);
        var second = await handler.Handle(CreateCommand(), default);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);

        _bankService.Verify(
            x => x.RefreshBanksAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}