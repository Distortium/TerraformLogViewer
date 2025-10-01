using System.Text.Json.Serialization;

public class PasswordResetCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; }
    public string Code { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; } = false;

    [JsonIgnore]
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    [JsonIgnore]
    public bool IsValid => !IsUsed && !IsExpired;
}