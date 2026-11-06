namespace Mova.Infrastructure.Authentication.Jwt.Interface;

public interface IRefreshTokenGenerator
{
    string Generate();
}