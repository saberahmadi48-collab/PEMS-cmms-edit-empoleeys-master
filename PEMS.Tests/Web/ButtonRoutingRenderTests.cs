using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.Models;

namespace PEMS.Tests.Web;

/// <summary>SCRUM-53: a <c>&lt;pems-button as="a" asp-controller asp-action&gt;</c> is written as an anchor with the button classes and none of the routing attributes left over. The test host registers no routes, so the href value itself (made by the built-in AnchorTagHelper this helper derives from) is not asserted here.</summary>
public class ButtonRoutingRenderTests
{
    [Fact]
    public async Task StatusPage_home_link_is_an_anchor_with_the_button_classes()
    {
        var host = new HomeController() { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var result = Assert.IsType<ViewResult>(host.StatusPage(404));
        var html = await RazorViewRenderer.RenderAsync(host, result, "~/Views/Home/StatusPage.cshtml");

        Assert.Contains("<a class=\"btn btn-pems-solid\"", html);
        Assert.DoesNotContain("asp-action", html);
        Assert.DoesNotContain("pems-button", html);
    }
}
