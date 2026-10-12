using Mova.Domain.Entities;
using Mova.Domain.Enums;

namespace Mova.Application.Interfaces.Security;

public interface IPinChallenge
{
    Task<bool> CreateAsync(
        string userPublicId,
        PinChallengeType pinChallengeType,
        CancellationToken cancellationToken = default);

    Task<(bool Success, string Message)> ValidateAsync(
        string userPublicId,
        PinChallengeType pinChallengeType,
        CancellationToken cancellationToken = default);
}