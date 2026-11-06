using System.Text.Json.Serialization;
using Mova.Shared.Common.Results;
namespace Mova.Shared.Common.Results;

public abstract class BasePaginationResponse<T>
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
    public List<T> Items { get; set; } = new();
}