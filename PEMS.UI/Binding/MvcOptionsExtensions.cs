using Microsoft.AspNetCore.Mvc;

namespace PEMS.UI.Binding;

public static class MvcOptionsExtensions
{
    /// <summary>
    /// Registers the numeric and date binder once for the whole application (put first so it wins over the built-in simple-type binder).
    /// Call it inside <c>AddControllersWithViews(options => ...)</c>.
    /// </summary>
    public static MvcOptions AddPemsInputBinding(this MvcOptions options)
    {
        if (!options.ModelBinderProviders.Any(p => p is PemsNumericModelBinderProvider))
            options.ModelBinderProviders.Insert(0, new PemsNumericModelBinderProvider());
        return options;
    }
}
