using FlowDesk.Web.Application.Contracts;
using FlowDesk.Web.Data;
using FlowDesk.Web.Domain;
using FlowDesk.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlowDesk.Web.Application.Services;

public class AdminService(
    FlowDeskDbContext db,
    UserManager<ApplicationUser> userManager) : IAdminService
{
    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken ct = default)
    {
        return await db.Categories
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new CategoryDto
            {
                Id = x.Id,
                Name = x.Name,
                Description = x.Description,
                DefaultSlaHours = x.DefaultSlaHours
            })
            .ToListAsync(ct);
    }

    public async Task<CategoryDto> CreateCategoryAsync(SaveCategoryDto dto, CancellationToken ct = default)
    {
        if (await db.Categories.AnyAsync(x => x.Name == dto.Name, ct))
        {
            throw new InvalidOperationException("Категория с таким названием уже существует.");
        }

        var category = new Category
        {
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            DefaultSlaHours = dto.DefaultSlaHours
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return new CategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            DefaultSlaHours = category.DefaultSlaHours
        };
    }

    public async Task UpdateCategoryAsync(int id, SaveCategoryDto dto, CancellationToken ct = default)
    {
        var category = await db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("Категория не найдена.");
        if (await db.Categories.AnyAsync(x => x.Id != id && x.Name == dto.Name, ct))
        {
            throw new InvalidOperationException("Категория с таким названием уже существует.");
        }

        category.Name = dto.Name.Trim();
        category.Description = dto.Description?.Trim();
        category.DefaultSlaHours = dto.DefaultSlaHours;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SlaPolicyDto>> GetSlaPoliciesAsync(CancellationToken ct = default)
    {
        return await db.SlaPolicies
            .AsNoTracking()
            .Include(x => x.Category)
            .OrderBy(x => x.Category.Name)
            .ThenBy(x => x.Priority)
            .Select(x => new SlaPolicyDto
            {
                Id = x.Id,
                CategoryId = x.CategoryId,
                CategoryName = x.Category.Name,
                Priority = x.Priority,
                Hours = x.Hours
            })
            .ToListAsync(ct);
    }

    public async Task<SlaPolicyDto> CreateSlaPolicyAsync(SaveSlaPolicyDto dto, CancellationToken ct = default)
    {
        if (await db.SlaPolicies.AnyAsync(x => x.CategoryId == dto.CategoryId && x.Priority == dto.Priority, ct))
        {
            throw new InvalidOperationException("Для этой категории и приоритета SLA уже задан.");
        }
        if (!await db.Categories.AnyAsync(x => x.Id == dto.CategoryId, ct))
        {
            throw new InvalidOperationException("Категория не найдена.");
        }

        var policy = new SlaPolicy
        {
            CategoryId = dto.CategoryId,
            Priority = dto.Priority,
            Hours = dto.Hours
        };
        db.SlaPolicies.Add(policy);
        await db.SaveChangesAsync(ct);
        return await db.SlaPolicies
            .AsNoTracking()
            .Include(x => x.Category)
            .Where(x => x.Id == policy.Id)
            .Select(x => new SlaPolicyDto
            {
                Id = x.Id,
                CategoryId = x.CategoryId,
                CategoryName = x.Category.Name,
                Priority = x.Priority,
                Hours = x.Hours
            })
            .FirstAsync(ct);
    }

    public async Task UpdateSlaPolicyAsync(int id, SaveSlaPolicyDto dto, CancellationToken ct = default)
    {
        var policy = await db.SlaPolicies.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("SLA-политика не найдена.");
        if (await db.SlaPolicies.AnyAsync(x => x.Id != id && x.CategoryId == dto.CategoryId && x.Priority == dto.Priority, ct))
        {
            throw new InvalidOperationException("Для этой категории и приоритета SLA уже задан.");
        }

        policy.CategoryId = dto.CategoryId;
        policy.Priority = dto.Priority;
        policy.Hours = dto.Hours;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteSlaPolicyAsync(int id, CancellationToken ct = default)
    {
        var policy = await db.SlaPolicies.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("SLA-политика не найдена.");
        db.SlaPolicies.Remove(policy);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AssignmentRuleDto>> GetAssignmentRulesAsync(CancellationToken ct = default)
    {
        return await db.AssignmentRules
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.Executor)
            .OrderBy(x => x.Category.Name)
            .ThenBy(x => x.SortOrder)
            .Select(x => new AssignmentRuleDto
            {
                Id = x.Id,
                CategoryId = x.CategoryId,
                CategoryName = x.Category.Name,
                ExecutorId = x.ExecutorId,
                ExecutorName = x.Executor.FullName,
                IsActive = x.IsActive,
                SortOrder = x.SortOrder
            })
            .ToListAsync(ct);
    }

    public async Task<AssignmentRuleDto> CreateAssignmentRuleAsync(SaveAssignmentRuleDto dto, CancellationToken ct = default)
    {
        var executor = await db.Users.FirstOrDefaultAsync(x => x.Id == dto.ExecutorId, ct)
            ?? throw new InvalidOperationException("Исполнитель не найден.");
        if (!await userManager.IsInRoleAsync(executor, RoleNames.Executor))
        {
            throw new InvalidOperationException("Пользователь не является исполнителем.");
        }

        var rule = new AssignmentRule
        {
            CategoryId = dto.CategoryId,
            ExecutorId = dto.ExecutorId,
            IsActive = dto.IsActive,
            SortOrder = dto.SortOrder
        };
        db.AssignmentRules.Add(rule);
        await db.SaveChangesAsync(ct);

        var category = await db.Categories.AsNoTracking().FirstAsync(x => x.Id == rule.CategoryId, ct);
        return new AssignmentRuleDto
        {
            Id = rule.Id,
            CategoryId = rule.CategoryId,
            CategoryName = category.Name,
            ExecutorId = rule.ExecutorId,
            ExecutorName = executor.FullName,
            IsActive = rule.IsActive,
            SortOrder = rule.SortOrder
        };
    }

    public async Task DeleteAssignmentRuleAsync(int id, CancellationToken ct = default)
    {
        var rule = await db.AssignmentRules.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("Правило не найдено.");
        db.AssignmentRules.Remove(rule);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken ct = default)
    {
        var users = await db.Users.AsNoTracking().OrderBy(x => x.FullName).ToListAsync(ct);
        var result = new List<UserDto>(users.Count);
        foreach (var user in users)
        {
            result.Add(new UserDto
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                FullName = user.FullName,
                IsActive = user.IsActive,
                Roles = (await userManager.GetRolesAsync(user)).OrderBy(x => x).ToList()
            });
        }
        return result;
    }

    public async Task SetUserActiveAsync(Guid userId, bool isActive, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw new InvalidOperationException("Пользователь не найден.");
        user.IsActive = isActive;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
        }
    }
}
