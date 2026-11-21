using Microsoft.AspNetCore.Mvc;

namespace PEMS.Tests.TestSupport;

/// <summary>A controller to render a view with (the tests used a real controller only as a host): in the Admin area, no dependencies.</summary>
[Area("Admin")]
public sealed class ViewRenderHost : Controller { }
