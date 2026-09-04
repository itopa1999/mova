using Mova.Domain.ValueObjects;

namespace Mova.Application.Interfaces.Security
{
    public interface IJwtTokenGenerator
    {
        string GenerateToken(
            long userId,
            string userPublicId,
            string firstName,
            string otherNames,
            string lastName,
            string email,
            string phoneNumber,
            decimal balance,
            string fullName,
            string platform,
            IList<string> roles);
    }
}