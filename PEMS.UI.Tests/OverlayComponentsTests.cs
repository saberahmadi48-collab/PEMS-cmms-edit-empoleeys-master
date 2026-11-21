using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using PEMS.UI.Abstractions;
using PEMS.UI.Components;
using PEMS.UI.Rendering;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-338 / #324: overlays and navigation. Texts are inputs; focus, Escape and stacking come from the native dialog, accessibility from the markup.</summary>
public sealed class OverlayComponentsTests : IAsyncLifetime
{
    private ServiceProvider _provider = default!;
    private IPemsComponentRenderer _renderer = default!;

    public Task InitializeAsync()
    {
        _provider = new ServiceCollection().AddPemsUi().BuildServiceProvider();
        _renderer = _provider.CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private static TranslatableText T(string fa, string en) => TranslatableText.Of(("fa", fa), ("en", en));
    private static void Has(string expected, string actual) => Assert.Contains(expected, actual, StringComparison.Ordinal);
    private static void HasNot(string unexpected, string actual) => Assert.DoesNotContain(unexpected, actual, StringComparison.Ordinal);

    private async Task<string> Render(string key, object options, string culture) =>
        WebUtility.HtmlDecode(await _renderer.RenderAsync(key, new Dictionary<string, object?> { ["Options"] = options }, new CultureInfo(culture)));

    // ---- alert
    [Fact]
    public async Task Alert_danger_is_an_alert_with_trace_id_and_a_copy_button_and_info_is_a_status()
    {
        var o = new AlertOptions { Skin = ComponentSkin.Pems, Title = T("ذخیره نشد", "Could not save"), Message = T("دوباره تلاش کنید", "Try again"), Tone = AlertTone.Danger, TraceId = "00-abc123", TraceLabel = T("شناسه", "Trace"), CopyLabel = T("کپی", "Copy"), DismissLabel = T("بستن", "Dismiss") };
        var en = await Render("alert", o, "en");
        Has("role=\"alert\"", en); Has("pems-alert--danger", en); Has(">Could not save<", en); Has("<code dir=\"ltr\">00-abc123</code>", en);
        Has("data-pems-copy=\"00-abc123\"", en); Has(">Copy</button>", en); Has("aria-label=\"Dismiss\"", en);
        Has(">ذخیره نشد<", await Render("alert", o, "fa"));
        var info = await Render("alert", new AlertOptions { Skin = ComponentSkin.Pems, Title = T("ثبت شد", "Saved") }, "en");
        Has("role=\"status\"", info); HasNot("pems-alert__trace", info); HasNot("data-pems-dismiss", info);
    }

    // ---- modal, drawer, confirm
    [Fact]
    public async Task Modal_is_a_labelled_native_dialog_with_a_close_button_in_each_language()
    {
        var o = new ModalOptions { Skin = ComponentSkin.Pems, Id = "m1", Title = T("جزئیات", "Details"), CloseLabel = T("بستن", "Close"), Body = T("متن", "Body text"), Size = OverlaySize.Large };
        var en = await Render("modal", o, "en");
        Has("<dialog id=\"m1\"", en); Has("pems-modal--large", en); Has("aria-labelledby=\"m1-title\"", en); Has("id=\"m1-title\"", en);
        Has("data-pems-close", en); Has("aria-label=\"Close\"", en); Has(">Body text<", en);
        var fa = await Render("modal", o, "fa");
        Has("aria-label=\"بستن\"", fa); Has(">جزئیات<", fa); HasNot("Details", fa);
    }

    [Fact]
    public async Task Drawer_sits_on_the_requested_side_and_is_a_dialog_too()
    {
        var o = new DrawerOptions { Skin = ComponentSkin.Pems, Id = "d1", Title = T("فیلترها", "Filters"), CloseLabel = T("بستن", "Close"), Side = DrawerSide.Start };
        var html = await Render("drawer", o, "en");
        Has("<dialog id=\"d1\"", html); Has("pems-drawer--start", html); Has("aria-labelledby=\"d1-title\"", html);
        Has("pems-drawer--end", await Render("drawer", o with { Side = DrawerSide.End }, "en"));
    }

    [Fact]
    public async Task Confirm_dialog_with_a_reason_requires_it_and_posts_the_row_key_and_token()
    {
        var o = new ConfirmDialogOptions
        {
            Skin = ComponentSkin.Pems,
            Id = "c1", Action = "/orders/cancel", Title = T("لغو", "Cancel order"), Message = T("مطمئن هستید؟", "Are you sure?"), ConfirmLabel = T("بله", "Yes"), CancelLabel = T("خیر", "No"),
            ReasonLabel = T("دلیل", "Reason"), RequestToken = "tok123"
        };
        var en = await Render("confirm-dialog", o, "en");
        Has("action=\"/orders/cancel\"", en); Has("method=\"post\"", en); Has("name=\"__RequestVerificationToken\" value=\"tok123\"", en);
        Has("name=\"key\" data-pems-row-key-target", en); Has("required=\"required\"", en); Has("name=\"reason\"", en);
        Has("pems-btn--danger", en); Has(">Yes</button>", en); Has(">No</button>", en); Has("aria-describedby=\"c1-message\"", en);
        var without = await Render("confirm-dialog", o with { ReasonLabel = null, RequestToken = null }, "en");
        HasNot("<textarea", without); HasNot("__RequestVerificationToken", without);
    }

    [Fact]
    public async Task A_button_can_open_a_dialog_for_a_row_and_stays_byte_identical_without_it()
    {
        var plain = await Render("button", new ButtonOptions { Label = T("ذخیره", "Save") }, "en");
        HasNot("data-pems-open", plain); HasNot("aria-haspopup", plain);
        var open = await Render("button", new ButtonOptions { Label = T("لغو", "Cancel"), Opens = "c1", RowKey = "42" }, "en");
        Has("data-pems-open=\"#c1\"", open); Has("data-pems-row-key=\"42\"", open); Has("aria-haspopup=\"dialog\"", open);
    }

    // ---- navigation
    [Fact]
    public async Task Tabs_follow_the_aria_pattern_with_only_the_active_panel_visible()
    {
        var o = new TabsOptions
        {
            Skin = ComponentSkin.Pems,
            Id = "t1", Label = T("بخش‌ها", "Sections"), Active = "b",
            Items = [new TabItem { Key = "a", Label = T("عمومی", "General"), Content = T("اول", "First") }, new TabItem { Key = "b", Label = T("یادداشت", "Notes"), Content = T("دوم", "Second") }]
        };
        var html = await Render("tabs", o, "en");
        Has("role=\"tablist\"", html); Has("aria-label=\"Sections\"", html);
        Has("id=\"t1-tab-b\"", html); Has("aria-selected=\"true\"", html); Has("tabindex=\"0\"", html); Has("tabindex=\"-1\"", html);
        Assert.Equal(1, Regex.Matches(html, "hidden").Count);                       // only panel a is hidden
        Assert.Matches(new Regex("id=\"t1-panel-a\"[^>]*hidden"), html);
        Has("aria-controls=\"t1-panel-a\"", html);
        var unknown = await Render("tabs", o with { Active = "zzz" }, "en");         // an unknown active key falls back to the first tab
        Assert.Matches(new Regex("id=\"t1-panel-b\"[^>]*hidden"), unknown);
    }

    [Fact]
    public async Task Breadcrumb_marks_the_current_page_and_links_the_rest()
    {
        var o = new BreadcrumbOptions { Skin = ComponentSkin.Pems, Label = T("مسیر", "Path"), Items = [new Crumb { Label = T("خانه", "Home"), Href = "/" }, new Crumb { Label = T("سفارش‌ها", "Orders"), Href = "/o" }, new Crumb { Label = T("سفارش ۱", "Order 1"), Href = "/o/1" }] };
        var html = await Render("breadcrumb", o, "en");
        Has("<nav class=\"pems-breadcrumb\" aria-label=\"Path\"", html); Has("<a href=\"/\">Home</a>", html);
        Has("aria-current=\"page\">Order 1</span>", html); HasNot("href=\"/o/1\"", html);
    }

    [Fact]
    public async Task Stepper_marks_done_current_and_upcoming_steps()
    {
        var o = new StepperOptions { Label = T("مراحل", "Stages"), Steps = [new Step { Label = T("پیش‌نویس", "Draft"), State = StepState.Done }, new Step { Label = T("بررسی", "Review"), State = StepState.Current }, new Step { Label = T("تأیید", "Approved") }] };
        var html = await Render("stepper", o, "en");
        Has("pems-stepper__step--done", html); Has("pems-stepper__step--current", html); Has("pems-stepper__step--upcoming", html);
        Assert.Equal(1, Regex.Matches(html, "aria-current=\"step\"").Count);
    }

    [Fact]
    public async Task Menu_is_a_disclosure_of_links()
    {
        var html = await Render("menu", new MenuOptions { Label = T("عملیات", "Actions"), Items = [new MenuItem { Label = T("ویرایش", "Edit"), Href = "/x/edit" }] }, "en");
        Has("<details class=\"pems-menu\" data-pems-menu", html); Has(">Actions</summary>", html); Has("<a class=\"pems-menu__item\" href=\"/x/edit\">Edit</a>", html);
    }

    [Fact]
    public async Task Side_panel_nav_shows_counts_in_the_locale_digits_and_the_active_folder()
    {
        var o = new SidePanelNavOptions { Label = T("پوشه‌ها", "Folders"), Items = [new NavItem { Label = T("دریافتی", "Inbox"), Href = "/l", Count = 1250, Active = true }, new NavItem { Label = T("ارسالی", "Sent"), Href = "/s" }] };
        var en = await Render("side-panel-nav", o, "en");
        Has("pems-sidenav__item--active", en); Has("aria-current=\"page\"", en); Has(">1,250<", en); Assert.Equal(1, Regex.Matches(en, "aria-current").Count);
        Has("۱٬۲۵۰", await Render("side-panel-nav", o, "fa"));
    }

    // ---- the as-is skin (SCRUM-480): the default renders the Bootstrap markup the pages write today
    [Fact]
    public async Task Default_skin_is_bootstrap_for_alert_and_dismiss_uses_the_bootstrap_close_button()
    {
        var html = await Render("alert", new AlertOptions { Title = T("ثبت شد", "Saved"), Tone = AlertTone.Success, DismissLabel = T("بستن", "Close") }, "en");
        Has("class=\"alert alert-success alert-dismissible fade show\"", html); Has("data-bs-dismiss=\"alert\"", html); HasNot("pems-", html);
        Has("class=\"alert alert-danger\" role=\"alert\"", await Render("alert", new AlertOptions { Title = T("خطا", "Error"), Tone = AlertTone.Danger }, "en"));
    }

    [Fact]
    public async Task Default_skin_tabs_are_bootstrap_nav_tabs_with_the_first_tab_active()
    {
        var o = new TabsOptions { Id = "t", Label = T("بخش‌ها", "Sections"), Items = [new TabItem { Key = "a", Label = T("الف", "Alpha") }, new TabItem { Key = "b", Label = T("ب", "Beta") }], Active = "b" };
        var html = await Render("tabs", o, "en");
        Has("class=\"nav nav-tabs mb-3\"", html); Has("data-bs-toggle=\"tab\"", html); Has("data-bs-target=\"#t-panel-b\"", html);
        Has("class=\"nav-link active\"", html); Has("class=\"tab-pane fade show active\"", html); Has("class=\"tab-content\"", html); HasNot("pems-", html);
    }

    [Fact]
    public async Task Default_skin_breadcrumb_marks_the_last_item_active()
    {
        var o = new BreadcrumbOptions { Label = T("مسیر", "Path"), Items = [new Crumb { Label = T("خانه", "Home"), Href = "/" }, new Crumb { Label = T("اکنون", "Now") }] };
        var html = await Render("breadcrumb", o, "en");
        Has("<ol class=\"breadcrumb mb-0 small\">", html); Has("class=\"breadcrumb-item active\"", html); Has("<a href=\"/\">Home</a>", html); HasNot("pems-", html);
    }

    [Fact]
    public async Task Default_skin_modal_drawer_and_confirm_are_bootstrap_markup()
    {
        var modal = await Render("modal", new ModalOptions { Id = "m", Title = T("جزئیات", "Details"), CloseLabel = T("بستن", "Close"), Size = OverlaySize.Large }, "en");
        Has("class=\"modal fade\"", modal); Has("class=\"modal-dialog modal-lg\"", modal); Has("data-bs-dismiss=\"modal\"", modal); HasNot("<dialog", modal);
        var drawer = await Render("drawer", new DrawerOptions { Id = "d", Title = T("فیلتر", "Filters"), CloseLabel = T("بستن", "Close"), Side = DrawerSide.Start }, "en");
        Has("class=\"offcanvas offcanvas-start\"", drawer); Has("data-bs-dismiss=\"offcanvas\"", drawer); HasNot("<dialog", drawer);
        var confirm = await Render("confirm-dialog", new ConfirmDialogOptions { Id = "c", Action = "/x", Title = T("؟", "Sure?"), Message = T("پیام", "Message"), ConfirmLabel = T("بله", "Yes"), CancelLabel = T("خیر", "No") }, "en");
        Has("class=\"modal fade\"", confirm); Has("class=\"btn btn-danger\"", confirm); Has("data-bs-dismiss=\"modal\"", confirm); Has("data-pems-row-key-target", confirm); HasNot("<dialog", confirm);
    }


    [Fact]
    public void The_overlay_script_and_stylesheet_are_offline_and_idempotent()
    {
        var js = File.ReadAllText(Find("PEMS.UI/wwwroot/pems-overlay.js"));
        Assert.DoesNotContain("http", js); Assert.DoesNotContain("onclick", js); Assert.DoesNotContain("alert(", js);
        Assert.Contains("dataset.pemsInit", js);
        var css = File.ReadAllText(Find("PEMS.UI/wwwroot/pems-ui.css"));
        Assert.DoesNotContain("z-index: 9", css);
    }

    private static string Find(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, relative))) dir = dir.Parent;
        return dir is null ? throw new FileNotFoundException(relative) : Path.Combine(dir.FullName, relative);
    }
}
