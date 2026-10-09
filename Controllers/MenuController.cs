using System.Security.Claims;
using System.Text.RegularExpressions;
using HRManagement.Authorization;
using HRManagement.DTOs;
using HRManagement.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class MenuController : ControllerBase
    {
        private const int MaxDepth = 3;

        // SuperAdmin-only screens: never shown in, or assignable to, a company role menu
        private static readonly HashSet<string> PlatformMenuCodes = new(StringComparer.OrdinalIgnoreCase)
        {
            "platform.companies", "system.roles", "system.permissions", "system.menus"
        };
        // Always kept in the SuperAdmin's sidebar so it can never hide the screen that edits it
        private const string SuperAdminRequiredMenuCode = "system.menus";

        private static readonly Regex CodePattern = new("^[a-zA-Z0-9._-]+$", RegexOptions.Compiled);

        private readonly HrmsDbContext _context;

        public MenuController(HrmsDbContext context)
        {
            _context = context;
        }

        /// <summary>The current user's menu: active nodes mapped to one of their roles, whose ancestors are all visible too.</summary>
        [HttpGet("my")]
        public async Task<IActionResult> GetMyMenu()
        {
            var menus = await _context.Menus.AsNoTracking().Where(m => m.IsActive).ToListAsync();
            List<Menu> visible;

            // SuperAdmin has no company roles, so its menus are flagged on the menu itself
            if (User.IsInRole(PlatformRoles.SuperAdmin))
            {
                visible = menus.Where(m => m.ShowForSuperAdmin).ToList();
            }
            else
            {
                var roleNames = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

                var grantedMenuIds = await _context.RoleMenus
                    .Where(rm => roleNames.Contains(rm.Role.RoleName) && rm.Role.IsActive)
                    .Select(rm => rm.MenuId)
                    .Distinct()
                    .ToListAsync();

                var granted = grantedMenuIds.ToHashSet();
                visible = menus.Where(m => granted.Contains(m.MenuId) && !PlatformMenuCodes.Contains(m.Code)).ToList();
            }

            var byParent = visible.ToLookup(m => m.ParentId);

            List<MyMenuNodeDTO> Build(int? parentId) => byParent[parentId]
                .OrderBy(m => m.DisplayOrder).ThenBy(m => m.MenuId)
                .Select(m => new MyMenuNodeDTO
                {
                    MenuId = m.MenuId,
                    Code = m.Code,
                    Title = m.Title,
                    Route = m.Route,
                    IconName = m.IconName,
                    Children = Build(m.MenuId)
                })
                .ToList();

            // Starting from root: a child whose parent isn't visible never gets reached.
            return Ok(Build(null));
        }

        // The menu tree is shared by every company, so only the SuperAdmin may view or edit it.
        // Company admins only choose which menus their own roles see (roles/{roleId} below).
        [HttpGet]
        [Authorize(Roles = PlatformRoles.SuperAdmin)]
        public async Task<IActionResult> GetTree()
        {
            return Ok(await BuildAdminTreeAsync(null));
        }

        [HttpGet("{id:int}")]
        [Authorize(Roles = PlatformRoles.SuperAdmin)]
        public async Task<IActionResult> GetById(int id)
        {
            var menu = await _context.Menus.AsNoTracking().Include(m => m.RoleMenus).FirstOrDefaultAsync(m => m.MenuId == id);
            if (menu == null) return NotFound();
            return Ok(ToAdminNode(menu, menu.RoleMenus.Select(rm => rm.RoleId).ToList(), null));
        }

        [HttpPost]
        [Authorize(Roles = PlatformRoles.SuperAdmin)]
        public async Task<IActionResult> Create([FromBody] MenuWriteDTO dto)
        {
            var error = await ValidateAsync(dto, null);
            if (error != null) return BadRequest(new { message = error });

            var menu = new Menu();
            Apply(menu, dto);
            _context.Menus.Add(menu);
            await _context.SaveChangesAsync();

            if (dto.RoleIds != null)
                await ReplaceRolesForMenuAsync(menu.MenuId, dto.RoleIds);

            return Ok(new { menuId = menu.MenuId });
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = PlatformRoles.SuperAdmin)]
        public async Task<IActionResult> Update(int id, [FromBody] MenuWriteDTO dto)
        {
            var menu = await _context.Menus.FindAsync(id);
            if (menu == null) return NotFound();

            var error = await ValidateAsync(dto, id);
            if (error != null) return BadRequest(new { message = error });

            Apply(menu, dto);
            await _context.SaveChangesAsync();

            if (dto.RoleIds != null)
                await ReplaceRolesForMenuAsync(id, dto.RoleIds);

            return Ok(new { menuId = id });
        }

        /// <summary>Deletes the node together with all of its descendants.</summary>
        [HttpDelete("{id:int}")]
        [Authorize(Roles = PlatformRoles.SuperAdmin)]
        public async Task<IActionResult> Delete(int id)
        {
            var all = await _context.Menus.ToListAsync();
            if (all.All(m => m.MenuId != id)) return NotFound();

            var toDelete = new List<Menu>();
            void Collect(int menuId)
            {
                foreach (var child in all.Where(m => m.ParentId == menuId))
                    Collect(child.MenuId);
                toDelete.Add(all.First(m => m.MenuId == menuId));
            }
            Collect(id);

            // Children first, since the self-reference is ON DELETE NO ACTION.
            await using var tx = await _context.Database.BeginTransactionAsync();
            foreach (var menu in toDelete)
            {
                _context.Menus.Remove(menu);
                await _context.SaveChangesAsync();
            }
            await tx.CommitAsync();

            return Ok(new { deletedCount = toDelete.Count });
        }

        /// <summary>Full tree with IsGranted flagged for the given role.</summary>
        [HttpGet("roles/{roleId:int}")]
        [Authorize(Roles = PlatformRoles.SuperAdmin)]
        [SuperAdminCompanyScope]
        public async Task<IActionResult> GetMenusForRole(int roleId)
        {
            if (!await _context.Roles.AnyAsync(r => r.RoleId == roleId)) return NotFound();
            return Ok(await BuildAdminTreeAsync(roleId));
        }

        /// <summary>Replaces the full set of menu nodes a role can see.</summary>
        [HttpPut("roles/{roleId:int}")]
        [Authorize(Roles = PlatformRoles.SuperAdmin)]
        [SuperAdminCompanyScope]
        public async Task<IActionResult> UpdateMenusForRole(int roleId, [FromBody] UpdateRoleMenusDTO dto)
        {
            if (!await _context.Roles.AnyAsync(r => r.RoleId == roleId)) return NotFound();

            var validIds = (await _context.Menus
                .Where(m => dto.MenuIds.Contains(m.MenuId))
                .Select(m => new { m.MenuId, m.Code })
                .ToListAsync())
                .Where(m => !PlatformMenuCodes.Contains(m.Code))
                .Select(m => m.MenuId)
                .ToList();

            var existing = await _context.RoleMenus.Where(rm => rm.RoleId == roleId).ToListAsync();
            var existingIds = existing.Select(rm => rm.MenuId).ToHashSet();
            var wanted = validIds.ToHashSet();

            _context.RoleMenus.RemoveRange(existing.Where(rm => !wanted.Contains(rm.MenuId)));
            foreach (var menuId in wanted.Where(id => !existingIds.Contains(id)))
                _context.RoleMenus.Add(new RoleMenu { RoleId = roleId, MenuId = menuId });

            await _context.SaveChangesAsync();
            return Ok("Role menus updated");
        }

        /// <summary>Full tree with IsGranted flagged for the SuperAdmin's own sidebar.</summary>
        [HttpGet("superadmin")]
        [Authorize(Roles = PlatformRoles.SuperAdmin)]
        public async Task<IActionResult> GetSuperAdminMenus()
        {
            return Ok(await BuildAdminTreeAsync(null, grantedForSuperAdmin: true));
        }

        /// <summary>Replaces the set of menu nodes shown in the SuperAdmin's sidebar.</summary>
        [HttpPut("superadmin")]
        [Authorize(Roles = PlatformRoles.SuperAdmin)]
        public async Task<IActionResult> UpdateSuperAdminMenus([FromBody] UpdateRoleMenusDTO dto)
        {
            var menus = await _context.Menus.ToListAsync();
            var byId = menus.ToDictionary(m => m.MenuId);
            var wanted = dto.MenuIds.Where(byId.ContainsKey).ToHashSet();

            // Keep the menu screen and its ancestors, or the SuperAdmin could lock itself out of this page
            var required = menus.FirstOrDefault(m => m.Code == SuperAdminRequiredMenuCode);
            for (var cursor = required; cursor != null; cursor = cursor.ParentId is int p ? byId[p] : null)
                wanted.Add(cursor.MenuId);

            foreach (var menu in menus)
                menu.ShowForSuperAdmin = wanted.Contains(menu.MenuId);

            await _context.SaveChangesAsync();
            return Ok("SuperAdmin menus updated");
        }

        private async Task<List<MenuAdminNodeDTO>> BuildAdminTreeAsync(int? grantedForRoleId, bool grantedForSuperAdmin = false)
        {
            var menus = await _context.Menus.AsNoTracking().ToListAsync();
            // The per-role tree only offers menus a company role may actually get
            if (grantedForRoleId.HasValue)
                menus = menus.Where(m => !PlatformMenuCodes.Contains(m.Code)).ToList();
            var mappings = await _context.RoleMenus.AsNoTracking().ToListAsync();
            var rolesByMenu = mappings.ToLookup(rm => rm.MenuId, rm => rm.RoleId);
            var byParent = menus.ToLookup(m => m.ParentId);

            List<MenuAdminNodeDTO> Build(int? parentId) => byParent[parentId]
                .OrderBy(m => m.DisplayOrder).ThenBy(m => m.MenuId)
                .Select(m =>
                {
                    var roleIds = rolesByMenu[m.MenuId].ToList();
                    bool? isGranted = grantedForSuperAdmin ? m.ShowForSuperAdmin
                        : grantedForRoleId.HasValue ? roleIds.Contains(grantedForRoleId.Value)
                        : null;
                    var node = ToAdminNode(m, roleIds, isGranted);
                    node.Children = Build(m.MenuId);
                    return node;
                })
                .ToList();

            return Build(null);
        }

        private static MenuAdminNodeDTO ToAdminNode(Menu m, List<int> roleIds, bool? isGranted) => new()
        {
            MenuId = m.MenuId,
            ParentId = m.ParentId,
            Code = m.Code,
            Title = m.Title,
            Route = m.Route,
            IconName = m.IconName,
            DisplayOrder = m.DisplayOrder,
            IsActive = m.IsActive,
            RoleIds = roleIds,
            IsGranted = isGranted
        };

        private static void Apply(Menu menu, MenuWriteDTO dto)
        {
            menu.ParentId = dto.ParentId;
            menu.Code = dto.Code.Trim();
            menu.Title = dto.Title.Trim();
            menu.Route = string.IsNullOrWhiteSpace(dto.Route) ? null : dto.Route.Trim();
            menu.IconName = string.IsNullOrWhiteSpace(dto.IconName) ? null : dto.IconName.Trim();
            menu.DisplayOrder = dto.DisplayOrder;
            menu.IsActive = dto.IsActive;
        }

        private async Task ReplaceRolesForMenuAsync(int menuId, List<int> roleIds)
        {
            var validRoleIds = await _context.Roles.Where(r => roleIds.Contains(r.RoleId)).Select(r => r.RoleId).ToListAsync();
            var existing = await _context.RoleMenus.Where(rm => rm.MenuId == menuId).ToListAsync();
            _context.RoleMenus.RemoveRange(existing);
            foreach (var roleId in validRoleIds.Distinct())
                _context.RoleMenus.Add(new RoleMenu { RoleId = roleId, MenuId = menuId });
            await _context.SaveChangesAsync();
        }

        private async Task<string?> ValidateAsync(MenuWriteDTO dto, int? editingId)
        {
            if (string.IsNullOrWhiteSpace(dto.Title)) return "Tên hiển thị là bắt buộc.";
            if (string.IsNullOrWhiteSpace(dto.Code)) return "Mã hệ thống là bắt buộc.";
            if (!CodePattern.IsMatch(dto.Code.Trim())) return "Mã hệ thống chỉ gồm chữ, số và các ký tự . _ -";

            var code = dto.Code.Trim();
            if (await _context.Menus.AnyAsync(m => m.Code == code && m.MenuId != editingId))
                return "Mã hệ thống đã tồn tại.";

            var all = await _context.Menus.AsNoTracking().ToListAsync();
            var byId = all.ToDictionary(m => m.MenuId);

            if (dto.ParentId.HasValue)
            {
                if (!byId.ContainsKey(dto.ParentId.Value)) return "Mục cha không tồn tại.";

                if (editingId.HasValue)
                {
                    // Walking up from the new parent must never reach the node being edited.
                    int? cursor = dto.ParentId;
                    while (cursor.HasValue)
                    {
                        if (cursor == editingId) return "Không thể chọn chính mục này hoặc mục con của nó làm mục cha.";
                        cursor = byId[cursor.Value].ParentId;
                    }
                }
            }

            int parentDepth = 0;
            for (int? cursor = dto.ParentId; cursor.HasValue; cursor = byId[cursor.Value].ParentId)
                parentDepth++;

            int SubtreeHeight(int id) => 1 + all.Where(m => m.ParentId == id).Select(c => SubtreeHeight(c.MenuId)).DefaultIfEmpty(0).Max();
            var height = editingId.HasValue ? SubtreeHeight(editingId.Value) : 1;

            if (parentDepth + height > MaxDepth)
                return $"Menu chỉ được sâu tối đa {MaxDepth} cấp.";

            return null;
        }
    }
}
