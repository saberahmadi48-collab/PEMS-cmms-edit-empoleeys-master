using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class CrewService : ICrewService
{
    private readonly PEMSDbContext _context;

    public CrewService(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<(bool Success, string? Error, int? CrewId)> CreateAsync(string code, string name, int? workOrderId)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
            return (false, "کد و نام Crew الزامی هستند.", null);

        if (await _context.Crews.AnyAsync(x => x.Code == code.Trim()))
            return (false, $"Crew با کد «{code}» قبلاً وجود دارد.", null);

        if (workOrderId.HasValue && !await _context.WorkOrders.AnyAsync(x => x.WorkOrderId == workOrderId.Value))
            return (false, "Work Order انتخاب‌شده یافت نشد.", null);

        var crew = new Crew
        {
            Code = code.Trim(),
            Name = name.Trim(),
            WorkOrderId = workOrderId,
            IsActive = true,
            CreatedDate = DateTime.Now
        };

        await _context.Crews.AddAsync(crew);
        await _context.SaveChangesAsync();

        return (true, null, crew.CrewId);
    }

    public async Task AddOrReactivateMemberAsync(int crewId, int employeeId, int roleId)
    {
        var crew = await _context.Crews.FirstOrDefaultAsync(x => x.CrewId == crewId);
        if (crew == null) throw new Exception("Crew یافت نشد.");
        if (!crew.IsActive) throw new Exception("این Crew غیرفعال است و نمی‌تواند عضو جدید بگیرد.");

        var employee = await _context.Employees.FirstOrDefaultAsync(x => x.EmployeeId == employeeId);
        if (employee == null) throw new Exception("کارمند یافت نشد.");
        if (!employee.IsActive) throw new Exception("این کارمند غیرفعال است و نمی‌تواند به Crew جدید اضافه شود.");

        var existing = await _context.CrewMembers
            .FirstOrDefaultAsync(x => x.CrewId == crewId && x.EmployeeId == employeeId);

        if (existing != null && existing.IsActive)
            throw new Exception("این کارمند از قبل عضو فعال این Crew است.");

        if (existing != null && !existing.IsActive)
        {
            existing.IsActive = true;
            existing.LeftDate = null;
            existing.JoinedDate = DateTime.Now;
            existing.RoleId = roleId;

            await _context.SaveChangesAsync();
            return;
        }

        await _context.CrewMembers.AddAsync(new CrewMember
        {
            CrewId = crewId,
            EmployeeId = employeeId,
            RoleId = roleId,
            JoinedDate = DateTime.Now,
            IsActive = true,
            CreatedDate = DateTime.Now
        });

        await _context.SaveChangesAsync();
    }

    public async Task RemoveMemberAsync(int crewMemberId)
    {
        var member = await _context.CrewMembers.FirstOrDefaultAsync(x => x.CrewMemberId == crewMemberId);
        if (member == null) throw new Exception("عضویت یافت نشد.");

        member.IsActive = false;
        member.LeftDate = DateTime.Now;

        await _context.SaveChangesAsync();
    }
}
