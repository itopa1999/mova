
using MediatR;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.AccountWallet;

public sealed class GetWalletAnalyticsQuery
{
    public sealed class Query : IRequest<BaseResult<GetWalletAnalyticsQueryResponseDto>>
    {
        
    }

    public class GetWalletAnalyticsQueryResponseDto
    {
        
    }

}
