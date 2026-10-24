using PEMS.Application.Common;
using Xunit.Abstractions;

namespace PEMS.Tests.Cmms;

/// <summary>
/// H25/H26: ماتریس واحد «بخش × فاز × مجوز» به‌صورت کد. این تست‌ها جدول مورد انتظار را به‌صورت لیترال (مستقل از پیاده‌سازی) می‌نویسند تا هر تغییر ناخواسته در سیاست
/// فوراً شکست بخورد، و ماتریس نهایی را در خروجی تست چاپ می‌کنند (برای گزارش نهایی).
/// کلیدها: H=Hidden, R=ReadOnly, E=Editable, C=Correctable.
/// </summary>
public class WorkOrderSectionPolicyTests
{
    private readonly ITestOutputHelper _out;
    public WorkOrderSectionPolicyTests(ITestOutputHelper output) => _out = output;

    private static readonly WoPhase[] Phases =
    {
        WoPhase.Created, WoPhase.Planning, WoPhase.Preparation, WoPhase.OperationsIssue, WoPhase.Released, WoPhase.Execution, WoPhase.OnHold,
        WoPhase.Completion, WoPhase.Verification, WoPhase.FinalApproval, WoPhase.Closed, WoPhase.Cancelled, WoPhase.Rejected
    };

    //                                         Cre Pln Prp Iss Rel Exe Hld Cmp Ver Fin Cls Can Rej
    private static readonly Dictionary<WoSection, string> Expected = new()
    {
        [WoSection.RequiredSkills]        = "E   E   E   R   R   R   R   R   R   R   R   R   R".Replace(" ", ""),
        [WoSection.RequiredMaterials]     = "E   E   E   R   R   R   R   R   R   R   R   R   R".Replace(" ", ""),
        [WoSection.Crew]                  = "E   E   E   R   R   R   R   R   R   R   R   R   R".Replace(" ", ""),
        [WoSection.Labor]                 = "H   H   H   H   R   E   R   C   C   R   R   R   H".Replace(" ", ""),
        [WoSection.SpareParts]            = "H   H   H   H   R   E   R   C   C   R   R   R   H".Replace(" ", ""),
        [WoSection.DisciplineReview]      = "E   E   E   E   E   E   E   R   R   R   R   R   R".Replace(" ", ""),
        [WoSection.ResponsibleDiscipline] = "H   E   E   R   R   R   R   R   R   R   R   R   R".Replace(" ", ""),
        [WoSection.ExecutorAssignment]    = "H   H   E   R   R   R   R   R   R   R   R   R   R".Replace(" ", ""),
        [WoSection.Schedule]              = "H   H   E   R   R   R   R   R   R   R   R   R   R".Replace(" ", ""),
        [WoSection.JobPlanTasks]          = "R   R   R   R   R   E   R   R   R   R   R   R   R".Replace(" ", ""),
    };

    private static SectionAccess Parse(char c) => c switch { 'H' => SectionAccess.Hidden, 'R' => SectionAccess.ReadOnly, 'E' => SectionAccess.Editable, _ => SectionAccess.Correctable };

    public static IEnumerable<object[]> Cells()
    {
        foreach (var section in Enum.GetValues<WoSection>())
            for (var i = 0; i < Phases.Length; i++)
                yield return new object[] { section, Phases[i], Expected[section][i] };
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void Access_matrix_matches_the_documented_table(WoSection section, WoPhase phase, char expected)
        => Assert.Equal(Parse(expected), WorkOrderSectionPolicy.AccessFor(section, phase));

    public static IEnumerable<object[]> Decisions()
    {
        foreach (var section in Enum.GetValues<WoSection>())
            foreach (var phase in Phases)
                foreach (var action in Enum.GetValues<WoSectionAction>())
                    foreach (var hasPerm in new[] { true, false })
                        foreach (var hasCorrection in new[] { true, false })
                            foreach (var reason in new string?[] { null, "  ", "fix typo" })
                                yield return new object[] { section, action, phase, hasPerm, hasCorrection, reason! };
    }

    /// <summary>ماتریس پارامتریک کامل (بخش × عمل × فاز × مجوز × مجوز اصلاح × دلیل): قواعد ناوردا در هر سلول.</summary>
    [Theory]
    [MemberData(nameof(Decisions))]
    public void Decide_never_allows_what_the_matrix_forbids_and_always_explains_a_denial(WoSection section, WoSectionAction action, WoPhase phase, bool hasPerm, bool hasCorrection, string? reason)
    {
        var access = WorkOrderSectionPolicy.AccessFor(section, phase);
        var d = WorkOrderSectionPolicy.Decide(section, action, phase, hasPerm, hasCorrection, reason);
        var terminal = phase is WoPhase.Closed or WoPhase.Cancelled or WoPhase.Rejected;

        if (d.Allowed)
        {
            Assert.True(access is SectionAccess.Editable or SectionAccess.Correctable, $"{section}/{phase} allowed although access is {access}");
            Assert.True(hasPerm, "allowed without the section permission");
            Assert.False(terminal, "a terminal Work Order must never accept child changes");
            if (access == SectionAccess.Correctable)
            {
                Assert.True(hasCorrection, "correction allowed without the correction permission");
                Assert.False(string.IsNullOrWhiteSpace(reason), "correction allowed without a reason");
            }
            return;
        }

        // هر ردی پیام مشخص فارسی و انگلیسی دارد (هرگز خالی/بی‌صدا)
        Assert.False(string.IsNullOrWhiteSpace(d.MessageFa));
        Assert.False(string.IsNullOrWhiteSpace(d.MessageEn));
        Assert.NotEqual("Allowed", d.Code);

        // و برعکس: هرچه ماتریس + مجوز اجازه می‌دهد باید مجاز باشد (رد بی‌دلیل ممنوع)
        if (access == SectionAccess.Editable) Assert.False(hasPerm, "denied although editable and permitted");
        if (access == SectionAccess.Correctable) Assert.False(hasPerm && hasCorrection && !string.IsNullOrWhiteSpace(reason), "denied although a valid correction");
        if (terminal) Assert.True(d.Code is "WorkOrderImmutable" or "SectionNotAvailable", $"terminal phase code was {d.Code}");
    }

    [Theory]
    [InlineData(WoPhase.Closed)]
    [InlineData(WoPhase.Cancelled)]
    public void Terminal_work_orders_are_immutable_in_every_section_for_every_user(WoPhase phase)
    {
        foreach (var section in Enum.GetValues<WoSection>())
            foreach (var action in Enum.GetValues<WoSectionAction>())
            {
                var d = WorkOrderSectionPolicy.Decide(section, action, phase, true, true, "any reason");
                Assert.False(d.Allowed);
                Assert.Equal("WorkOrderImmutable", d.Code);
                Assert.False(WorkOrderSectionPolicy.ShowsControls(section, phase, true, true));
            }
    }

    [Fact]
    public void Controls_are_shown_only_when_the_server_would_accept_the_post()
    {
        // «هرگز فرمی نشان نده که سرور رد می‌کند»: ShowsControls باید با Decide (بدون الزام دلیل) هم‌ارز باشد
        foreach (var section in Enum.GetValues<WoSection>())
            foreach (var phase in Phases)
                foreach (var hasPerm in new[] { true, false })
                    foreach (var hasCorrection in new[] { true, false })
                    {
                        var shown = WorkOrderSectionPolicy.ShowsControls(section, phase, hasPerm, hasCorrection);
                        var accepted = WorkOrderSectionPolicy.Decide(section, WoSectionAction.Add, phase, hasPerm, hasCorrection, "reason").Allowed;
                        Assert.Equal(accepted, shown);
                    }
    }

    [Theory]
    [InlineData("Closed", null, WoPhase.Closed)]
    [InlineData("Cancelled", null, WoPhase.Cancelled)]
    [InlineData("Rejected", null, WoPhase.Rejected)]
    [InlineData("Open", "Created", WoPhase.Created)]
    [InlineData("Open", "Maintenance Planning", WoPhase.Planning)]
    [InlineData("Assigned", "Preparation / Assignment", WoPhase.Preparation)]
    [InlineData("Assigned", "Operations Issue / Release", WoPhase.OperationsIssue)]
    [InlineData("Assigned", "Released / Execution Authorized", WoPhase.Released)]
    [InlineData("InProgress", "In Execution", WoPhase.Execution)]
    [InlineData("OnHold", "On Hold", WoPhase.OnHold)]
    [InlineData("Completed", "Completion", WoPhase.Completion)]
    [InlineData("Completed", "Originating-Discipline Verification", WoPhase.Verification)]
    [InlineData("Completed", "Final Approval", WoPhase.FinalApproval)]
    [InlineData("Closed", "Final Approval", WoPhase.Closed)]       // وضعیت پایانی بر نام مرحله مقدم است
    [InlineData("InProgress", null, WoPhase.Execution)]            // Legacy بدون Instance
    [InlineData("Open", null, WoPhase.Created)]
    public void Phase_is_resolved_from_status_and_stage(string status, string? stage, WoPhase expected)
        => Assert.Equal(expected, WorkOrderSectionPolicy.ResolvePhase(status, stage));

    [Fact]
    public void Planning_and_assignment_sections_require_their_workflow_task_permissions()
    {
        Assert.Equal("WorkOrder.Plan", WorkOrderSectionPolicy.RequiredPermission(WoSection.ResponsibleDiscipline));
        Assert.Equal("WorkOrder.Assign", WorkOrderSectionPolicy.RequiredPermission(WoSection.ExecutorAssignment));
        Assert.Equal("WorkOrder.Assign", WorkOrderSectionPolicy.RequiredPermission(WoSection.Schedule));
        Assert.Equal("WorkOrder.Edit", WorkOrderSectionPolicy.RequiredPermission(WoSection.Labor));
        Assert.Equal("WorkOrder.Edit", WorkOrderSectionPolicy.RequiredPermission(WoSection.Crew));
    }

    [Fact]
    public void Print_the_final_policy_matrix()
    {
        var header = "Section".PadRight(24) + string.Join(" ", Phases.Select(p => p.ToString()[..3]));
        _out.WriteLine(header);
        foreach (var section in Enum.GetValues<WoSection>())
            _out.WriteLine(section.ToString().PadRight(24) + string.Join("   ", Phases.Select(p => WorkOrderSectionPolicy.AccessFor(section, p).ToString()[0])));
    }
}
