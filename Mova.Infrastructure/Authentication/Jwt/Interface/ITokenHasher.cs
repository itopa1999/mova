namespace Mova.Infrastructure.Authentication.Jwt.Interface;

public interface ITokenHasher
{
    string HashToken(string token);
}