using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Mova.Application.Interfaces.Identity;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.TransactionPin;

public sealed class GetIfPinIsSetQuery
{
    public sealed class Query : IRequest<BaseResult<GetIfPinIsSetQueryDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;
    }

    public sealed class GetIfPinIsSetQueryDto
    {
        public bool HasPinSet { get; set; }
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<GetIfPinIsSetQueryDto>>
    {
        private readonly IIdentityService _identityService;

        public Handler(IIdentityService identityService)
        {
            _identityService = identityService;
        }

        public async Task<BaseResult<GetIfPinIsSetQueryDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                return new BaseResult<GetIfPinIsSetQueryDto>(
                    HttpStatusCode.BadRequest,
                    "User ID is required.",
                    default);
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user == null)
            {
                return new BaseResult<GetIfPinIsSetQueryDto>(
                    HttpStatusCode.NotFound,
                    "User not found.",
                    default);
            }

            var pin = new GetIfPinIsSetQueryDto
            {
                HasPinSet=!string.IsNullOrWhiteSpace(user.TransactionPinHash)
            };

            return new BaseResult<GetIfPinIsSetQueryDto>(
                HttpStatusCode.OK,
                "pin check retrieved successfully.",
                pin);
        }
    }
}