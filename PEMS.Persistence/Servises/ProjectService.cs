using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Exceptions;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class ProjectService : IProjectService
{
    private readonly PEMSDbContext _context;

    public ProjectService(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<List<ProjectDto>> GetAllAsync()
    {
        return await _context.Projects
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => new ProjectDto
            {
                ProjectId = x.ProjectId,
                ProjectCode = x.ProjectCode,
                ProjectName = x.ProjectName,
                ClientName = x.ClientName,
                Location = x.Location,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                Status = x.Status,
                IsActive = x.IsActive,
                CreatedDate = x.CreatedDate,
                DocumentsCount = _context.EngineeringDocuments.Count(d => d.ProjectId == x.ProjectId)
            })
            .ToListAsync();
    }

    public async Task<Project?> GetEntityByIdAsync(int id)
    {
        return await _context.Projects.FirstOrDefaultAsync(x => x.ProjectId == id);
    }

    // SCRUM-558 (owner decision 2026-11-09): a project code is unique. Checked here because old databases still hold repeating codes of
    // leftover test projects, so a unique index has to wait for that clean-up (a separate issue).
    public async Task<bool> CodeExistsAsync(string? code, int? exceptProjectId = null)
    {
        var key = code?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(key)) return false;
        return await _context.Projects.AnyAsync(p => (exceptProjectId == null || p.ProjectId != exceptProjectId.Value)
                                                     && p.ProjectCode.Trim().ToUpper() == key);
    }

    private async Task EnsureUniqueCodeAsync(Project project, int? exceptProjectId)
    {
        project.ProjectCode = project.ProjectCode?.Trim() ?? string.Empty;
        if (await CodeExistsAsync(project.ProjectCode, exceptProjectId))
            throw new ConflictException("unique", $"Project code '{project.ProjectCode}' already exists.");
    }

    public async Task AddAsync(Project project)
    {
        await EnsureUniqueCodeAsync(project, null);
        project.CreatedDate = DateTime.Now;
        await _context.Projects.AddAsync(project);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Project project)
    {
        await EnsureUniqueCodeAsync(project, project.ProjectId);
        _context.Projects.Update(project);
        await _context.SaveChangesAsync();
    }

    public async Task ToggleActiveAsync(int id)
    {
        var project = await _context.Projects.FirstOrDefaultAsync(x => x.ProjectId == id);
        if (project == null) return;
        project.IsActive = !project.IsActive;
        await _context.SaveChangesAsync();
    }
}
