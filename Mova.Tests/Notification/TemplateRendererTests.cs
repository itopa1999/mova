using Mova.Infrastructure.Notification.Email;
namespace Mova.Tests.Notification;

public sealed class TemplateRendererTests
{
    [Fact]
    public async Task RenderAsync_WithMarkdownFormatting_RendersRichHtml()
    {
        var renderer = new TemplateRenderer();

        var html = await renderer.RenderAsync(
            "NotificationEmailTemplate.html",
            new Dictionary<string, string>
            {
                ["FirstName"] = "Ada",
                ["Message"] = "# Welcome\n\n**Important**\n\n• First item\n• Second item\n\nhttps://example.com"
            });

        Assert.Contains("<h1>Welcome</h1>", html);
        Assert.Contains("<strong>Important</strong>", html);
        Assert.Contains("<ul>", html);
        Assert.Contains("<li>First item</li>", html);
        Assert.Contains("<a href=\"https://example.com\" target=\"_blank\" rel=\"noopener noreferrer\">https://example.com</a>", html);
    }
}
