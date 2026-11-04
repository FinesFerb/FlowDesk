using FlowDesk.Web.Domain;
using FlowDesk.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FlowDesk.Web.Data;

public class FlowDeskDbContext(DbContextOptions<FlowDeskDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<RequestStatus> RequestStatuses => Set<RequestStatus>();
    public DbSet<ServiceRequest> Requests => Set<ServiceRequest>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();
    public DbSet<AssignmentRule> AssignmentRules => Set<AssignmentRule>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Category>(entity =>
        {
            entity.ToTable("categories");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasIndex(x => x.Name).IsUnique();
        });

        builder.Entity<RequestStatus>(entity =>
        {
            entity.ToTable("request_statuses");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(80).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
        });

        builder.Entity<ServiceRequest>(entity =>
        {
            entity.ToTable("requests");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.Priority)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            entity.HasOne(x => x.Category)
                .WithMany(x => x.Requests)
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Status)
                .WithMany(x => x.Requests)
                .HasForeignKey(x => x.StatusId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Client)
                .WithMany()
                .HasForeignKey(x => x.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Executor)
                .WithMany()
                .HasForeignKey(x => x.ExecutorId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Dispatcher)
                .WithMany()
                .HasForeignKey(x => x.DispatcherId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.SlaPolicy)
                .WithMany(x => x.Requests)
                .HasForeignKey(x => x.SlaPolicyId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(x => x.StatusId);
            entity.HasIndex(x => x.Priority);
            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => x.ExecutorId);
            entity.HasIndex(x => x.ClientId);
        });

        builder.Entity<Comment>(entity =>
        {
            entity.ToTable("comments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Text).HasMaxLength(4000).IsRequired();
            entity.HasOne(x => x.Request)
                .WithMany(x => x.Comments)
                .HasForeignKey(x => x.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Author)
                .WithMany()
                .HasForeignKey(x => x.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.RequestId, x.CreatedAt });
        });

        builder.Entity<Attachment>(entity =>
        {
            entity.ToTable("attachments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.StoredName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
            entity.HasOne(x => x.Request)
                .WithMany(x => x.Attachments)
                .HasForeignKey(x => x.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.UploadedBy)
                .WithMany()
                .HasForeignKey(x => x.UploadedById)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.RequestId);
        });

        builder.Entity<SlaPolicy>(entity =>
        {
            entity.ToTable("sla_policies");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Priority)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.HasOne(x => x.Category)
                .WithMany(x => x.SlaPolicies)
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.CategoryId, x.Priority }).IsUnique();
        });

        builder.Entity<AssignmentRule>(entity =>
        {
            entity.ToTable("assignment_rules");
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Category)
                .WithMany(x => x.AssignmentRules)
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Executor)
                .WithMany()
                .HasForeignKey(x => x.ExecutorId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.CategoryId, x.SortOrder });
        });

        builder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(1000).IsRequired();
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Request)
                .WithMany(x => x.Notifications)
                .HasForeignKey(x => x.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.UserId, x.IsRead });
        });

        builder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).HasMaxLength(120).IsRequired();
            entity.Property(x => x.OldValue).HasMaxLength(2000);
            entity.Property(x => x.NewValue).HasMaxLength(2000);
            entity.HasOne(x => x.Request)
                .WithMany(x => x.AuditLogs)
                .HasForeignKey(x => x.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.RequestId, x.CreatedAt });
        });
    }
}
