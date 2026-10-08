using GanttDiagram.Application.Abstractions;
using GanttDiagram.Application.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GanttDiagram.Application.Services;

public class UserService(IAppDbContext db)
{
    public async Task<IReadOnlyList<UserDto>> ListAsync(Guid? connectionId, string? search, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking();
        if (connectionId is not null) query = query.Where(u => u.ConnectionId == connectionId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim().ToLower();
            query = query.Where(u => u.DisplayName.ToLower().Contains(text) || u.Name.ToLower().Contains(text));
        }
        return (await query.OrderBy(u => u.DisplayName).Take(500).ToListAsync(ct)).Select(u => u.ToDto()).ToList();
    }

    public async Task<UserDto> GetAsync(Guid id, CancellationToken ct) =>
        (await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw NotFoundException.For("Пользователь", id)).ToDto();
}
