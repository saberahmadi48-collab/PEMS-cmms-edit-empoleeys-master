using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class LetterUserSettingsService : ILetterUserSettingsService
{
    private readonly PEMSDbContext _ctx;

    public LetterUserSettingsService(PEMSDbContext ctx) => _ctx = ctx;

    public async Task<LetterUserSettingsRules.Settings> GetAsync(int userId)
    {
        var json = await _ctx.LetterUserSettings.AsNoTracking().Where(s => s.ApplicationUserId == userId).Select(s => s.SettingsJson).FirstOrDefaultAsync();
        return LetterUserSettingsRules.Parse(json);
    }

    public async Task SaveAsync(int userId, LetterUserSettingsRules.Settings settings)
    {
        var json = LetterUserSettingsRules.Serialize(settings);
        var row = await _ctx.LetterUserSettings.FirstOrDefaultAsync(s => s.ApplicationUserId == userId);
        if (row == null) _ctx.LetterUserSettings.Add(new LetterUserSetting { ApplicationUserId = userId, SettingsJson = json, UpdatedDate = DateTime.Now });
        else { row.SettingsJson = json; row.UpdatedDate = DateTime.Now; }
        await _ctx.SaveChangesAsync();
    }
}
