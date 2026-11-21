using System.Globalization;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Resources;
using PEMS.Web.Security;

namespace PEMS.Web.Services;

/// <summary>SCRUM-759: the list, invitations and clarifications of TenderController (it used the context inline), as operations.</summary>
public sealed class TenderOperations(PEMSDbContext context, IProcurementPackageService packages, ITenderInvitationService invitations, IBackgroundJobClient jobs, IStringLocalizer<SharedResource> localizer,
    IConfiguration configuration, ICurrentUserService currentUser) : ITenderOperations
{
    public async Task<List<TenderRow>> TendersAsync(int? procurementPackageId = null)
    {
        var query = context.Tenders.AsNoTracking().Include(x => x.ProcurementPackage).Include(x => x.Bids).AsQueryable();
        if (procurementPackageId.HasValue) query = query.Where(x => x.ProcurementPackageId == procurementPackageId.Value);
        return (await query.OrderByDescending(x => x.TenderId).Take(500).ToListAsync()).Select(t => new TenderRow(t.TenderId, t.TenderNumber, t.Title, t.ProcurementPackage?.PackageNumber ?? "", t.ProcurementPackageId, t.Status, t.IssueDate, t.ResponseDeadline, t.Bids.Count, t.ContractId)).ToList();
    }

    public async Task<(bool Success, string? Error)> SendInvitationsAsync(string? userName, int tenderId, int? vendorId = null)
    {
        var template = new TenderInvitationTemplate(localizer["Tinv_MailSubject"].Value, localizer["Tinv_MailBody"].Value, CultureInfo.CurrentUICulture, configuration["Letterhead:CompanyName"]);
        var result = await invitations.QueueAsync(tenderId, template, userName, vendorId.HasValue ? [vendorId.Value] : null);
        if (result.Error != null) return (false, localizer[result.Error].Value);
        foreach (var dispatchId in result.QueuedDispatchIds) jobs.Enqueue<ITenderInvitationService>(s => s.SendAsync(dispatchId));
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> RaiseClarificationAsync(int tenderId, int procurementPackageId, string type, string subject, string body, int? vendorId = null)
    {
        await packages.AddCommunicationAsync(procurementPackageId, type, subject, body, vendorId, currentUser.FullName, null, tenderId);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> RespondClarificationAsync(int procurementPackageId, int communicationId, string responseText)
    {
        await packages.RespondToCommunicationAsync(procurementPackageId, communicationId, responseText, currentUser.FullName);
        return (true, null);
    }
}
