using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Exceptions;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-539: an empty Projects create form was saved (no code, no name), and Vendor create showed no validation box for
/// a server-side error. The project code and name are required on the server and in the browser; Vendor create has the
/// validation summary like the other create forms.
/// </summary>
public class ProjectAndVendorCreateValidationTests
{
    private static string View(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, "PEMS.Web", "Views", .. parts]));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "Name")]
    [InlineData("PRJ-1", "")]
    public void AProjectWithoutCodeOrName_IsNotValid(string code, string name)
    {
        var p = new Project { ProjectCode = code, ProjectName = name };
        Assert.False(Validator.TryValidateObject(p, new ValidationContext(p), new List<ValidationResult>(), validateAllProperties: true));
    }

    [Fact]
    public void AProjectWithCodeAndName_IsValid()
    {
        var p = new Project { ProjectCode = "PRJ-1", ProjectName = "Name" };
        Assert.True(Validator.TryValidateObject(p, new ValidationContext(p), new List<ValidationResult>(), validateAllProperties: true));
    }

    [Fact]
    public void VendorCreate_ShowsServerSideErrors()
        => Assert.Contains("<pems-validation-summary", View("Vendor", "Create.cshtml"));

    // ------------------------------------------------------------ SCRUM-558: the project code is unique

    private static PEMSDbContext NewDb() => new(new DbContextOptionsBuilder<PEMSDbContext>()
        .UseInMemoryDatabase("prj_" + Guid.NewGuid().ToString("N")).Options);

    [Fact]
    public async Task SCRUM558_A_code_that_exists_is_refused_on_create_and_edit_ignoring_case_and_spaces()
    {
        await using var db = NewDb();
        var service = new ProjectService(db);
        await service.AddAsync(new Project { ProjectCode = "PRJ-1", ProjectName = "One" });
        await service.AddAsync(new Project { ProjectCode = " PRJ-2 ", ProjectName = "Two" });

        Assert.True(await service.CodeExistsAsync(" prj-1 "));
        Assert.False(await service.CodeExistsAsync("PRJ-3"));
        var dup = await Assert.ThrowsAsync<ConflictException>(() => service.AddAsync(new Project { ProjectCode = "prj-1 ", ProjectName = "Copy" }));
        Assert.Equal("unique", dup.Code);
        Assert.Equal("PRJ-2", db.Projects.Single(p => p.ProjectName == "Two").ProjectCode);   // saved trimmed

        var two = db.Projects.Single(p => p.ProjectName == "Two");
        Assert.False(await service.CodeExistsAsync("PRJ-2", two.ProjectId));            // its own code is fine
        two.ProjectCode = "PRJ-1";
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(two));
        Assert.Equal(2, db.Projects.Count());
    }

}
