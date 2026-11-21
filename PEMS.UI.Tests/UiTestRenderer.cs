using Microsoft.Extensions.DependencyInjection;
using PEMS.UI.Rendering;

namespace PEMS.UI.Tests;

/// <summary>The one renderer the tag helper tests build their helpers with (no request, the same registry the app uses).</summary>
internal static class UiTestRenderer
{
    public static readonly IPemsComponentRenderer Instance = new ServiceCollection().AddPemsUi().BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();
}
