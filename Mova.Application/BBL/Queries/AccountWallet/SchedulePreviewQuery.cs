using System.Net;
using MediatR;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.SchedulePreview;

public sealed class SchedulePreviewQuery
{
    public sealed class Query : IRequest<BaseResult<SchedulePreviewResponseDto>>
    {
        public decimal TargetAmount { get; set; }
        public decimal ReleaseAmount { get; set; }
        public ReleaseFrequency FrequencyType { get; set; }
        public string FrequencyConfig { get; set; } = string.Empty;
        public DateTimeOffset StartDate { get; set; }
        public int MaxReleases { get; set; } = 50;
    }

    public sealed class ReleaseDatePreviewDto
    {
        public DateTimeOffset Date { get; set; }
        public decimal Amount { get; set; }
        public int ReleaseNumber { get; set; }
        public decimal CumulativeAmount { get; set; }
    }

    public sealed class SchedulePreviewResponseDto
    {
        public bool IsValid { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal TargetAmount { get; set; }
        public decimal ReleaseAmount { get; set; }
        public decimal RegularReleaseAmount { get; set; }
        public decimal FinalReleaseAmount { get; set; }
        public int TotalReleases { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTimeOffset FirstReleaseDate { get; set; }
        public DateTimeOffset ComputedEndDate { get; set; }
        public int WeeksToReachTarget { get; set; }
        public int MonthsToReachTarget { get; set; }
        public ReleaseFrequency FrequencyType { get; set; }
        public List<ReleaseDatePreviewDto> SampleReleaseDates { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }
    public sealed class Handler : IRequestHandler<Query, BaseResult<SchedulePreviewResponseDto>>
    {
        private readonly ISchedulePreviewService _previewService;

        public Handler(ISchedulePreviewService previewService)
        {
            _previewService = previewService;
        }

        public async Task<BaseResult<SchedulePreviewResponseDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            try
            {
                var preview = await _previewService.PreviewScheduleAsync(
                    request.TargetAmount,
                    request.ReleaseAmount,
                    request.FrequencyType,
                    request.FrequencyConfig,
                    request.StartDate,
                    request.MaxReleases,
                    cancellationToken);

                var response = new SchedulePreviewResponseDto
                {
                    IsValid = preview.IsSuccess && preview.Errors.Count == 0,
                    Description = preview.Description,
                    TargetAmount = preview.TargetAmount,
                    ReleaseAmount = preview.ReleaseAmount,
                    RegularReleaseAmount = preview.RegularReleaseAmount,
                    FinalReleaseAmount = preview.FinalReleaseAmount,
                    TotalReleases = preview.TotalReleases,
                    TotalAmount = preview.TotalAmount,
                    FirstReleaseDate = preview.FirstReleaseDate,
                    ComputedEndDate = preview.ComputedEndDate,
                    WeeksToReachTarget = preview.WeeksToReachTarget,
                    MonthsToReachTarget = preview.MonthsToReachTarget,
                    FrequencyType = preview.FrequencyType,
                    Errors = preview.Errors,
                    Warnings = preview.Warnings
                };

                foreach (var date in preview.SampleReleaseDates)
                {
                    response.SampleReleaseDates.Add(new ReleaseDatePreviewDto
                    {
                        Date = date.Date,
                        Amount = date.Amount,
                        ReleaseNumber = date.ReleaseNumber,
                        CumulativeAmount = date.CumulativeAmount
                    });
                }

                if (!response.IsValid)
                {
                    return new BaseResult<SchedulePreviewResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Schedule preview failed. Please check your configuration.",
                        response);
                }

                return new BaseResult<SchedulePreviewResponseDto>(
                    HttpStatusCode.OK,
                    "Schedule preview generated successfully.",
                    response);
            }
            catch (Exception ex)
            {
                return new BaseResult<SchedulePreviewResponseDto>(
                    HttpStatusCode.BadRequest,
                    $"Error generating schedule preview: {ex.Message}",
                    null);
            }
        }
    }
}