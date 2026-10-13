using Microsoft.EntityFrameworkCore;
using TaskTrack.Repo.Models;
using TaskTrack.Repo.Repositories;
using TaskTrack.Service.Dtos;
using TaskTrack.Service.Interfaces;

namespace TaskTrack.Service.Services;

public class TaskService(TaskRepository repo, ProjectRepository projects) : ITaskService
{
    public async Task<IReadOnlyList<TaskResponse>> GetAllAsync(CancellationToken ct) => (await repo.Query().OrderBy(x => x.DueDate).ToListAsync(ct)).Select(Map).ToList();
    public async Task<TaskResponse> GetByIdAsync(int id, CancellationToken ct) => Map(await repo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Task not found."));
    public async Task<IReadOnlyList<TaskResponse>> GetByProjectAsync(int projectId, CancellationToken ct) => (await repo.GetByProjectAsync(projectId, ct)).Select(Map).ToList();

    public async Task<IReadOnlyList<TaskResponse>> SearchAsync(string? title, short? status, short? priority, int? projectId, int? tagId, CancellationToken ct)
    {
        var query = repo.Query();
        if (!string.IsNullOrWhiteSpace(title)) query = query.Where(x => EF.Functions.ILike(x.Title, $"%{title.Trim()}%"));
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (priority.HasValue) query = query.Where(x => x.Priority == priority.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        if (tagId.HasValue) query = query.Where(x => x.TaskTags.Any(link => link.TagId == tagId.Value));
        return (await query.OrderBy(x => x.DueDate).ToListAsync(ct)).Select(Map).ToList();
    }

    public async Task<TaskResponse> CreateAsync(TaskRequest request, int accountId, CancellationToken ct)
    {
        if (request.Title.Trim().Length == 0) throw new InvalidOperationException("Title is required.");
        if (!await projects.ExistsAsync(request.ProjectId, ct)) throw new InvalidOperationException("Project does not exist.");
        var tagIds = await ValidateTagIdsAsync(request.TagIds, ct);
        var item = new TaskItem { Title = request.Title.Trim(), Description = request.Description?.Trim(), Status = request.Status, Priority = request.Priority, DueDate = request.DueDate, ProjectId = request.ProjectId, CreatedById = accountId, IsActive = true, CreatedDate = DateTime.UtcNow };
        repo.ReplaceTags(item, tagIds);
        await repo.AddAsync(item, ct); await repo.SaveAsync(ct); return await GetByIdAsync(item.TaskId, ct);
    }

    public async Task<TaskResponse> UpdateAsync(int id, TaskRequest request, int accountId, CancellationToken ct)
    {
        var item = await repo.FindTrackedAsync(id, ct) ?? throw new KeyNotFoundException("Task not found.");
        if (request.Title.Trim().Length == 0) throw new InvalidOperationException("Title is required.");
        if (!await projects.ExistsAsync(request.ProjectId, ct)) throw new InvalidOperationException("Project does not exist.");
        var tagIds = await ValidateTagIdsAsync(request.TagIds, ct);
        item.Title = request.Title.Trim(); item.Description = request.Description?.Trim(); item.Status = request.Status; item.Priority = request.Priority; item.DueDate = request.DueDate; item.ProjectId = request.ProjectId; item.ModifiedDate = DateTime.UtcNow; item.UpdatedById = accountId;
        repo.ReplaceTags(item, tagIds);
        await repo.SaveAsync(ct); return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(int id, int accountId, CancellationToken ct)
    {
        var item = await repo.FindTrackedAsync(id, ct) ?? throw new KeyNotFoundException("Task not found.");
        item.IsActive = false; item.ModifiedDate = DateTime.UtcNow; item.UpdatedById = accountId; await repo.SaveAsync(ct);
    }

    private async Task<List<int>> ValidateTagIdsAsync(IEnumerable<int> requestedIds, CancellationToken ct)
    {
        var ids = requestedIds.Distinct().ToList();
        if (ids.Count == 0) return ids;
        var tags = await repo.GetTagsAsync(ids, ct);
        if (tags.Count != ids.Count) throw new InvalidOperationException("One or more tags do not exist.");
        return ids;
    }

    private static TaskResponse Map(TaskItem x) => new(x.TaskId, x.Title, x.Description, x.Status, x.Priority, x.DueDate, x.ProjectId, x.Project?.ProjectName ?? string.Empty, x.Project?.Department?.DepartmentName ?? string.Empty, x.IsActive, x.CreatedDate, x.ModifiedDate, x.TaskTags.Select(link => new TagResponse(link.TagId, link.Tag?.TagName ?? string.Empty, link.Tag?.Color)).ToList(), x.CreatedById, x.UpdatedById);
}
