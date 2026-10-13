using TaskTrack.Service.Dtos;

namespace TaskTrack.Service.Interfaces;

public interface IDepartmentService
{
    Task<IReadOnlyList<DepartmentResponse>> GetAllAsync(CancellationToken ct);
    Task<DepartmentResponse> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<DepartmentResponse>> SearchAsync(string name, CancellationToken ct);
    Task<DepartmentResponse> CreateAsync(DepartmentRequest request, CancellationToken ct);
    Task<DepartmentResponse> UpdateAsync(int id, DepartmentRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}

public interface IProjectService
{
    Task<IReadOnlyList<ProjectResponse>> GetAllAsync(CancellationToken ct);
    Task<ProjectResponse> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<ProjectResponse>> GetByDepartmentAsync(int departmentId, CancellationToken ct);
    Task<IReadOnlyList<ProjectResponse>> SearchAsync(string? name, short? status, int? departmentId, CancellationToken ct);
    Task<ProjectResponse> CreateAsync(ProjectRequest request, CancellationToken ct);
    Task<ProjectResponse> UpdateAsync(int id, ProjectRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}

public interface ITaskService
{
    Task<IReadOnlyList<TaskResponse>> GetAllAsync(CancellationToken ct);
    Task<TaskResponse> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<TaskResponse>> GetByProjectAsync(int projectId, CancellationToken ct);
    Task<IReadOnlyList<TaskResponse>> SearchAsync(string? title, short? status, short? priority, int? projectId, int? tagId, CancellationToken ct);
    Task<TaskResponse> CreateAsync(TaskRequest request, int accountId, CancellationToken ct);
    Task<TaskResponse> UpdateAsync(int id, TaskRequest request, int accountId, CancellationToken ct);
    Task DeleteAsync(int id, int accountId, CancellationToken ct);
}

public interface ITagService
{
    Task<IReadOnlyList<TagResponse>> GetAllAsync(CancellationToken ct);
    Task<TagResponse> CreateAsync(TagRequest request, CancellationToken ct);
    Task<TagResponse> UpdateAsync(int id, TagRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
