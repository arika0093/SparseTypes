using SparseTypes;

[SparseModel]
public partial class UserPreferences
{
    public string? DisplayName { get; set; }

    public string? Theme { get; set; }

    public int RefreshInterval { get; set; }

    public NotificationPreferences? Notifications { get; set; }
}

public partial class NotificationPreferences
{
    public bool EmailEnabled { get; set; }
}
