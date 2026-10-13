using Microsoft.EntityFrameworkCore;
using TaskTrack.Repo.Models;

namespace TaskTrack.Repo;

public class TaskManagementDbContext(DbContextOptions<TaskManagementDbContext> options) : DbContext(options)
{
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TaskTag> TaskTags => Set<TaskTag>();
    public DbSet<SystemAccount> Accounts => Set<SystemAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Department>(entity =>
        {
            entity.ToTable("Department");
            entity.HasKey(x => x.DepartmentId).HasName("Department_pkey");
            entity.Property(x => x.DepartmentId).HasColumnName("DepartmentID");
            entity.Property(x => x.DepartmentName).HasColumnName("DepartmentName").HasMaxLength(100).IsRequired();
            entity.Property(x => x.DepartmentDescription).HasColumnName("DepartmentDescription").HasMaxLength(300).IsRequired();
            entity.Property(x => x.IsActive).HasColumnName("IsActive").HasDefaultValue(true);
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("Project");
            entity.HasKey(x => x.ProjectId).HasName("Project_pkey");
            entity.Property(x => x.ProjectId).HasColumnName("ProjectID");
            entity.Property(x => x.ProjectName).HasColumnName("ProjectName").HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasColumnName("Description");
            entity.Property(x => x.StartDate).HasColumnName("StartDate");
            entity.Property(x => x.EndDate).HasColumnName("EndDate");
            entity.Property(x => x.Status).HasColumnName("Status").HasDefaultValue((short)0);
            entity.Property(x => x.DepartmentId).HasColumnName("DepartmentID");
            entity.Property(x => x.IsActive).HasColumnName("IsActive").HasDefaultValue(true);
            entity.Property(x => x.CreatedDate).HasColumnName("CreatedDate").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(x => x.Department).WithMany(x => x.Projects).HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Project_Department");
        });

        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.ToTable("Task");
            entity.HasKey(x => x.TaskId).HasName("Task_pkey");
            entity.Property(x => x.TaskId).HasColumnName("TaskID");
            entity.Property(x => x.Title).HasColumnName("Title").HasMaxLength(300).IsRequired();
            entity.Property(x => x.Description).HasColumnName("Description");
            entity.Property(x => x.Status).HasColumnName("Status").HasDefaultValue((short)0);
            entity.Property(x => x.Priority).HasColumnName("Priority").HasDefaultValue((short)1).HasSentinel((short)-1);
            entity.Property(x => x.DueDate).HasColumnName("DueDate");
            entity.Property(x => x.ProjectId).HasColumnName("ProjectID");
            entity.Property(x => x.CreatedById).HasColumnName("CreatedByID");
            entity.HasOne(x => x.CreatedBy).WithMany(x => x.CreatedTasks).HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Task_CreatedBy");
            entity.Property(x => x.IsActive).HasColumnName("IsActive").HasDefaultValue(true);
            entity.Property(x => x.CreatedDate).HasColumnName("CreatedDate").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(x => x.ModifiedDate).HasColumnName("ModifiedDate");
            entity.HasOne(x => x.Project).WithMany(x => x.Tasks).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Task_Project");
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.ToTable("Tag");
            entity.HasKey(x => x.TagId).HasName("Tag_pkey");
            entity.Property(x => x.TagId).HasColumnName("TagID");
            entity.Property(x => x.TagName).HasColumnName("TagName").HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.TagName).IsUnique();
            entity.Property(x => x.Color).HasColumnName("Color").HasMaxLength(7);
        });

        modelBuilder.Entity<SystemAccount>(entity =>
        {
            entity.ToTable("SystemAccount", table => table.HasCheckConstraint("CK_SystemAccount_Role", "\"Role\" IN (0, 1)"));
            entity.HasKey(x => x.AccountId);
            entity.Property(x => x.AccountId).HasColumnName("AccountID");
            entity.Property(x => x.FullName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
            entity.Property(x => x.PasswordHash).IsRequired();
            entity.Property(x => x.Role).HasDefaultValue((short)0);
            entity.Property(x => x.CreatedDate).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<TaskTag>(entity =>
        {
            entity.ToTable("TaskTag");
            entity.HasKey(x => new { x.TaskId, x.TagId }).HasName("PK_TaskTag");
            entity.Property(x => x.TaskId).HasColumnName("TaskID");
            entity.Property(x => x.TagId).HasColumnName("TagID");
            entity.HasOne(x => x.Task).WithMany(x => x.TaskTags).HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_TaskTag_Task");
            entity.HasOne(x => x.Tag).WithMany(x => x.TaskTags).HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_TaskTag_Tag");
        });
    }
}
