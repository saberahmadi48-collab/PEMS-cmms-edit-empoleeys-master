namespace PEMS.Web.Navigation;

/// <summary>
/// پرچم سایدبار داده‌محور. پیش‌فرض «غیرفعال» (سایدبار قدیمی _SidebarMenu.cshtml): فقط مقدار دقیق true در
/// Features:DynamicSidebar:Enabled فعال می‌کند؛ چون جدول NavigationMenuItems تا اعمال Migration روی دیتابیس مقصد وجود ندارد.
/// صفحه‌ی مدیریت منو و Seeder هم فقط وقتی پرچم فعال است کار می‌کنند.
/// </summary>
public interface IDynamicSidebarAvailability
{
    bool IsEnabled { get; }
}

public sealed class DynamicSidebarAvailability : IDynamicSidebarAvailability
{
    public const string ConfigurationKey = "Features:DynamicSidebar:Enabled";
    private readonly IConfiguration _configuration;

    public DynamicSidebarAvailability(IConfiguration configuration) => _configuration = configuration;

    public bool IsEnabled => bool.TryParse(_configuration[ConfigurationKey], out var enabled) && enabled;
}
