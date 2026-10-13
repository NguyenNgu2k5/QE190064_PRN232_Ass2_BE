namespace TaskTrack.Service.Dtos;

public record DepartmentResponse(int DepartmentId, string DepartmentName, string DepartmentDescription, bool IsActive, IReadOnlyList<ProjectSummary> Projects);
public record ProjectSummary(int ProjectId, string ProjectName, short Status, DateOnly StartDate, DateOnly? EndDate);
public record ProjectResponse(int ProjectId, string ProjectName, string? Description, DateOnly StartDate, DateOnly? EndDate, short Status, int DepartmentId, string DepartmentName, bool IsActive, DateTime CreatedDate, IReadOnlyList<TaskSummary> Tasks);
public record TaskSummary(int TaskId, string Title, short Status, short Priority, DateOnly? DueDate);
public record TaskResponse(int TaskId, string Title, string? Description, short Status, short Priority, DateOnly? DueDate, int ProjectId, string ProjectName, string DepartmentName, bool IsActive, DateTime CreatedDate, DateTime? ModifiedDate, IReadOnlyList<TagResponse> Tags, int? CreatedById, int? UpdatedById);
public record TagResponse(int TagId, string TagName, string? Color);
