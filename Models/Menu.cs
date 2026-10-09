namespace HRManagement.Models;

public partial class Menu
{
    public int MenuId { get; set; }

    public int? ParentId { get; set; }

    public string Code { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Route { get; set; }

    public string? IconName { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; }

    /// <summary>Shown in the SuperAdmin's sidebar (the SuperAdmin has no company roles, so it is not in RoleMenus).</summary>
    public bool ShowForSuperAdmin { get; set; }

    public virtual Menu? Parent { get; set; }

    public virtual ICollection<Menu> Children { get; set; } = new List<Menu>();

    public virtual ICollection<RoleMenu> RoleMenus { get; set; } = new List<RoleMenu>();
}
