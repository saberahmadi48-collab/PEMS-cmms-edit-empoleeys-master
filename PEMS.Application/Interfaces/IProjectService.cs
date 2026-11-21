using PEMS.Application.DTOs;
using PEMS.Domain.Entities;

namespace PEMS.Application.Interfaces;

public interface IProjectService
{
    Task<List<ProjectDto>> GetAllAsync();
    Task<Project?> GetEntityByIdAsync(int id);
    /// <summary>SCRUM-558: true when another project (not <paramref name="exceptProjectId"/>) already has this code; spaces trimmed, case ignored.</summary>
    Task<bool> CodeExistsAsync(string? code, int? exceptProjectId = null);
    /// <summary>Saves a new project; a code that already exists is refused with a <c>ConflictException</c> (code <c>unique</c>, SCRUM-558).</summary>
    Task AddAsync(Project project);
    /// <summary>Saves the project; a code that another project has is refused with a <c>ConflictException</c> (code <c>unique</c>, SCRUM-558).</summary>
    Task UpdateAsync(Project project);
    Task ToggleActiveAsync(int id);
}
