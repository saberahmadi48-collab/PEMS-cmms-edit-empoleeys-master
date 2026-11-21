using Microsoft.AspNetCore.Mvc;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-550: deleting a letter tag that is refused (it has sub-tags, it is someone else's personal tag) used to reload the
/// Tags page with the tag still there and no message. The service now says why and the page shows it.
/// </summary>
public class TagDeleteOutcomeTests
{
    private static (PEMSDbContext Db, Tag Parent, Tag Child, Tag Free, Tag Personal) Seed()
    {
        var db = PemsDbContextFactory.CreateInMemory("tags_" + Guid.NewGuid().ToString("N"));
        var parent = new Tag { Name = "Parent" };
        var free = new Tag { Name = "Free" };
        var personal = new Tag { Name = "Mine", OwnerUserId = 7 };
        db.Tags.AddRange(parent, free, personal);
        db.SaveChanges();
        var child = new Tag { Name = "Child", ParentTagId = parent.TagId, OwnerUserId = 7 };
        db.Tags.Add(child);
        db.SaveChanges();
        return (db, parent, child, free, personal);
    }

    [Fact]
    public async Task TheService_SaysWhyADeleteIsRefused()
    {
        var (db, parent, _, free, personal) = Seed();
        using var _db = db;
        var svc = new TagService(db);

        Assert.Equal(TagDeleteOutcome.HasSubTags, await svc.DeleteAsync(parent.TagId, actorUserId: 1));
        Assert.Equal(TagDeleteOutcome.NotOwner, await svc.DeleteAsync(personal.TagId, actorUserId: 1));
        Assert.Equal(TagDeleteOutcome.NotFound, await svc.DeleteAsync(999, actorUserId: 1));
        Assert.True(db.Tags.Single(t => t.TagId == parent.TagId).IsActive);
        Assert.True(db.Tags.Single(t => t.TagId == personal.TagId).IsActive);

        Assert.Equal(TagDeleteOutcome.Deleted, await svc.DeleteAsync(free.TagId, actorUserId: 1));
        Assert.False(db.Tags.Single(t => t.TagId == free.TagId).IsActive);
        Assert.Equal(TagDeleteOutcome.NotFound, await svc.DeleteAsync(free.TagId, actorUserId: 1));   // already deleted
        Assert.Equal(TagDeleteOutcome.Deleted, await svc.DeleteAsync(personal.TagId, actorUserId: 7)); // the owner may
    }

    [Fact]
    public async Task ThePage_ShowsTheReason_AsAnError_AndADeleteAsAMessage()
    {
        var (db, parent, _, free, _) = Seed();
        using var _db = db;
        var ctl = new TagController(new TagService(db), new FakeCurrentUserService { UserId = 1 }).WithTempData();

        Assert.IsType<RedirectToActionResult>(await ctl.Delete(parent.TagId));
        Assert.Equal("Tg_Err_HasSubTags", ctl.TempData["Error"]);

        ctl.TempData.Clear();
        await ctl.Delete(free.TagId);
        Assert.Equal("Tg_Deleted", ctl.TempData["Message"]);
        Assert.Null(ctl.TempData["Error"]);
    }
}
