using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Mova.Application.Behaviors;

public sealed class RequestValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(request);

        if (!Validator.TryValidateObject(
                request,
                validationContext,
                validationResults,
                validateAllProperties: true))
        {
            var errors = validationResults
                .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
                    .Select(member => string.IsNullOrWhiteSpace(member)
                        ? result.ErrorMessage ?? "The request is invalid."
                        : $"{member}: {result.ErrorMessage ?? "The request is invalid."}"))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            throw new ValidationException(string.Join(" ", errors));
        }

        return next();
    }
}
