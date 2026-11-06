namespace Mova.Infrastructure.Service;

public interface ICurrentUserService
{
    long? UserId { get; }
}