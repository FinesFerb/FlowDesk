using FlowDesk.Tests.TestSupport;
using FlowDesk.Web.Application;
using FlowDesk.Web.Application.Contracts;
using FlowDesk.Web.Data;
using FlowDesk.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowDesk.Tests.UnitTests;

public class AdminServiceTests : ServiceTestBase
{
    [Fact]
    public async Task CreateCategory_NewCategory_Persists()
    {
        using var scope = Provider.CreateScope();
        var adminService = scope.ServiceProvider.GetRequiredService<IAdminService>();

        var created = await adminService.CreateCategoryAsync(new SaveCategoryDto
        {
            Name = "Логистика",
            Description = "Перемещение оборудования между офисами",
            DefaultSlaHours = 32
        });

        Assert.True(created.Id > 0);
        Assert.Equal("Логистика", created.Name);
        Assert.Equal(32, created.DefaultSlaHours);

        var all = await adminService.GetCategoriesAsync();
        Assert.Contains(all, x => x.Name == "Логистика" && x.DefaultSlaHours == 32);
    }

    [Fact]
    public async Task CreateCategory_DuplicateName_Throws()
    {
        using var scope = Provider.CreateScope();
        var adminService = scope.ServiceProvider.GetRequiredService<IAdminService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adminService.CreateCategoryAsync(new SaveCategoryDto { Name = "Ремонт техники", DefaultSlaHours = 10 }));

        Assert.Equal("Категория с таким названием уже существует.", exception.Message);
    }

    [Fact]
    public async Task CreateAssignmentRule_NonExecutorUser_Throws()
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var adminService = scope.ServiceProvider.GetRequiredService<IAdminService>();
        var category = await db.Categories.FirstAsync(x => x.Name == "IT-поддержка");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adminService.CreateAssignmentRuleAsync(new SaveAssignmentRuleDto
            {
                CategoryId = category.Id,
                ExecutorId = Users["client"].Id,
                IsActive = true,
                SortOrder = 1
            }));

        Assert.Equal("Пользователь не является исполнителем.", exception.Message);
    }

    [Fact]
    public async Task CreateAssignmentRule_ValidExecutor_Persists()
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var adminService = scope.ServiceProvider.GetRequiredService<IAdminService>();
        var category = await db.Categories.FirstAsync(x => x.Name == "Ремонт техники");

        var created = await adminService.CreateAssignmentRuleAsync(new SaveAssignmentRuleDto
        {
            CategoryId = category.Id,
            ExecutorId = Users["executor2"].Id,
            IsActive = true,
            SortOrder = 2
        });

        Assert.True(created.Id > 0);
        Assert.Equal("Ремонт техники", created.CategoryName);
        Assert.Equal("Анна Инженерова", created.ExecutorName);
        Assert.True(created.IsActive);

        var rules = await adminService.GetAssignmentRulesAsync();
        Assert.Contains(rules, x => x.Id == created.Id && x.ExecutorId == Users["executor2"].Id);
    }
}
