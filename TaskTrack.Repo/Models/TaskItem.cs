namespace TaskTrack.Repo.Models;

public class TaskItem
{
    public int TaskId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public short Status { get; set; }
    public short Priority { get; set; }
    public DateOnly? DueDate { get; set; }
    public int ProjectId { get; set; }
    public int? CreatedById { get; set; }
    public SystemAccount? CreatedBy { get; set; }
    public int? UpdatedById { get; set; }
    public SystemAccount? UpdatedBy { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public Project Project { get; set; } = null!;
    public ICollection<TaskTag> TaskTags { get; set; } = new List<TaskTag>();
}
