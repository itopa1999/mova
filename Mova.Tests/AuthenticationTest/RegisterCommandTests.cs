using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.Authentication;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Constants;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class RegisterCommandTests : BaseTest
{
    private const string FirstName = "Lucky";
    private const string LastName = "Starboy";
    private const string Email = "lucky@mova.app";
    private const string Phone = "08050000000";
    private const string NormalizedPhone = "+2348050000000";
    private const string Password = "ValidPass123!";
    private const string GeneratedOtp = "123456";
    private const string NewUserPublicId = "user_new_reg";
    private const long NewUserId = 100;

    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<IOtpService> _otpService = new();
    private readonly Mock<INotificationQueue> _notifications = new();

    private RegisterCommand.Handler CreateHandler()
    {
        return new RegisterCommand.Handler(
            _identityService.Object,
            UnitOfWork,
            _otpService.Object,
            _notifications.Object,
            Mock.Of<ILogger<RegisterCommand.Handler>>());
    }

    private RegisterCommand.Command CreateCommand(
        string firstName = FirstName,
        string lastName = LastName,
        string email = Email,
        string phone = Phone,
        string password = Password)
    {
        return new RegisterCommand.Command
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            PhoneNumber = phone,
            Password = password,
        };
    }

    private void SetupEmailNotExists(bool exists = false)
    {
        _identityService
            .Setup(x => x.EmailExistsAsync(
                It.IsAny<string>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(exists);
    }

    private void SetupPhoneNotExists(bool exists = false)
    {
        _identityService
            .Setup(x => x.PhoneExistsAsync(
                It.IsAny<string>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(exists);
    }

    private void SetupUserCreationSucceeds(
        string publicId = NewUserPublicId,
        long userId = NewUserId)
    {
        _identityService
            .Setup(x => x.CreateUserAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync((true, string.Empty, publicId, userId));
    }

    private void SetupUserCreationFails(string error = "Password too weak")
    {
        _identityService
            .Setup(x => x.CreateUserAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync((false, error, string.Empty, 0L));
    }

    private void SetupRoleAssignmentSucceeds()
    {
        _identityService
            .Setup(x => x.AddToRoleAsync(
                It.IsAny<long>(),
                It.IsAny<string>()))
            .ReturnsAsync((true, string.Empty));
    }

    private void SetupRoleAssignmentFails(string error = "Role not found")
    {
        _identityService
            .Setup(x => x.AddToRoleAsync(
                It.IsAny<long>(),
                It.IsAny<string>()))
            .ReturnsAsync((false, error));
    }

    private void SetupOtpGeneration(string otp = GeneratedOtp)
    {
        _otpService
            .Setup(x => x.GenerateOtp())
            .Returns(otp);
    }

    private void SetupHappyPath()
    {
        SetupEmailNotExists();
        SetupPhoneNotExists();
        SetupUserCreationSucceeds();
        SetupRoleAssignmentSucceeds();
        SetupOtpGeneration();
    }

    // ---------------------------------------------------------
    // 1. Happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidRequest_ReturnsCreated()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.Created, result.StatusCode);
        Assert.Equal("Account created successfully.", result.Message);
    }

    [Fact]
    public async Task Handle_WithValidRequest_ReturnsUserDetails()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.NotNull(result.Data);
        Assert.Equal(NewUserPublicId, result.Data!.UserPublicId);
        Assert.Equal(Email, result.Data.Email);
        Assert.Equal("Lucky Starboy", result.Data.FullName);
        Assert.Equal("Email Verification", result.Data.NextStep);
    }

    [Fact]
    public async Task Handle_WithValidRequest_NormalizesNamesToTitleCase()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(firstName: "LUCKY", lastName: "starboy"), default);

        Assert.NotNull(result.Data);
        Assert.Equal("Lucky Starboy", result.Data!.FullName);

        _identityService.Verify(
            x => x.CreateUserAsync(
                "Lucky",
                "Starboy",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_SavesOtpRecord()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var otp = await Context.OtpVerifications
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserPublicId == NewUserPublicId);

        Assert.NotNull(otp);
        Assert.Equal(GeneratedOtp, otp!.OtpCode);
        Assert.Equal(OtpPurpose.AccountVerification, otp.Purpose);
        Assert.False(otp.IsUsed);
        Assert.True(otp.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Handle_WithValidRequest_QueuesOtpDelivery()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.QueueOtpDelivery(
                "Lucky",
                Email,
                NormalizedPhone,
                GeneratedOtp,
                It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_AssignsCustomerRole()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.AddToRoleAsync(NewUserId, Roles.Customer),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_CommitsTransactionOnce()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        Assert.Equal(1, UnitOfWork.BeginCount);
        Assert.Equal(1, UnitOfWork.CommitCount);
        Assert.Equal(0, UnitOfWork.RollbackCount);
    }

    // ---------------------------------------------------------
    // 2. Validation failures
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithInvalidPhone_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateCommand(phone: "not-a-phone"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("valid Nigerian phone", result.Message);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithExistingEmail_ReturnsBadRequest()
    {
        SetupEmailNotExists(exists: true);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Email is already in use.", result.Message);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithExistingPhone_ReturnsBadRequest()
    {
        SetupEmailNotExists();
        SetupPhoneNotExists(exists: true);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Phone number is already in use.", result.Message);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WhenUserCreationFails_ReturnsBadRequest()
    {
        SetupEmailNotExists();
        SetupPhoneNotExists();
        SetupUserCreationFails("Password does not meet requirements");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Password does not meet requirements", result.Message);
        Assert.Equal(1, UnitOfWork.BeginCount);
        Assert.Equal(1, UnitOfWork.RollbackCount);
        Assert.Equal(0, UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Handle_WhenRoleAssignmentFails_ReturnsBadRequest()
    {
        SetupEmailNotExists();
        SetupPhoneNotExists();
        SetupUserCreationSucceeds();
        SetupRoleAssignmentFails("Role assignment failed");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(1, UnitOfWork.BeginCount);
        Assert.Equal(1, UnitOfWork.RollbackCount);
        Assert.Equal(0, UnitOfWork.CommitCount);
    }

    // ---------------------------------------------------------
    // 3. Regression: notification failures must not fail the request
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenOtpQueueThrows_StillReturnsCreated()
    {
        SetupHappyPath();

        _notifications
            .Setup(x => x.QueueOtpDelivery(
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Throws(new InvalidOperationException("Hangfire unavailable"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.Created, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenOtpQueueThrows_UserStillPersisted()
    {
        SetupHappyPath();

        _notifications
            .Setup(x => x.QueueOtpDelivery(
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Throws(new Exception("boom"));

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var otp = await Context.OtpVerifications
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserPublicId == NewUserPublicId);

        Assert.NotNull(otp);
    }

    // ---------------------------------------------------------
    // 4. Regression: rollback behaviour
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenSaveChangesThrows_RollsBackAndReturnsConflict()
    {
        SetupEmailNotExists();
        SetupPhoneNotExists();
        SetupUserCreationSucceeds();
        SetupRoleAssignmentSucceeds();
        SetupOtpGeneration();

        var failingUow = new Mock<Mova.Application.Interfaces.Persistence.IUnitOfWork>();

        failingUow
            .Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        failingUow
            .Setup(x => x.AddAsync(It.IsAny<OtpVerification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        failingUow
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Simulated DB failure"));

        failingUow
            .Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new RegisterCommand.Handler(
            _identityService.Object,
            failingUow.Object,
            _otpService.Object,
            _notifications.Object,
            Mock.Of<ILogger<RegisterCommand.Handler>>());

        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, result.StatusCode);
        failingUow.Verify(
            x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }
}