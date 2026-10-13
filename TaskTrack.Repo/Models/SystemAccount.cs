namespace TaskTrack.Repo.Models;

public class SystemAccount
{
    public int AccountId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public short Role { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public int TokenVersion { get; set; }
    public string? RefreshTokenHash { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }
    public Guid? RefreshTokenFamilyId { get; set; }
    public ICollection<TaskItem> CreatedTasks { get; set; } = new List<TaskItem>();
}
