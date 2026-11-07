using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
namespace Mova.Application.Interfaces.Notification;

public interface INotificationQueue
{
    void QueueOtpDelivery(
        string? firstName,
        string email,
        string? phoneNumber,
        string otp,
        string purpose);

    void QueueForgotPasswordOtp(
        string email,
        string? phoneNumber,
        string otp);

    void QueueWelcomeEmail(
        string firstName,
        string email);

    void QueueNotificationEmail(
        string firstName,
        string email,
        string message,
        string subject);

    void InAppNotificationAsync(
        string UserId,
        NotificationType Type,
        string Title,
        string Message,
        string? ActionUrl,
        string? Metadata,
        CancellationToken cancellationToken = default
    );

    void QueueCreateVirtualAccount(
        string userPublicId,
        string firstName,
        string lastName,
        string email,
        string phoneNumber
    );

    void QueueCreateCircleWalletAsync(
        string UserPublicId,
        long CategoryId,
        long SavingsCircleId,
        string Name,
        string Description,
        decimal TargetAmount,
        decimal TotalUpfrontCharge,
        decimal TotalMovaFees,
        decimal DepositAmount);

    void QueueCreateCircleInvitationMemberAsync(
        long savingsCircleId,
        string organizerName,
        string inviteCode,
        List<CircleInviteData> invites);


    public class CircleInviteData
    {
        public string Identifier { get; set; } = string.Empty;
        public string? TargetUserPublicId { get; set; }
        public string? Phone { get; set; }
        public string? DisplayName { get; set; }
        public int? PayoutPosition { get; set; }
    }

}