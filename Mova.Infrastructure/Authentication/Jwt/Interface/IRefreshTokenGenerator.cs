
namespace Mova.Infrastructure.Authentication.Jwt;

public interface IRefreshTokenGenerator
{
    string Generate();
}