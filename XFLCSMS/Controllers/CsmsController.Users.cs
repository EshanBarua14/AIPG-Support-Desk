using Microsoft.AspNetCore.Mvc;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Admin;
using XFLCSMS.Models.Branch;
using XFLCSMS.Models.Register;
using XFLCSMS.Services;

namespace XFLCSMS.Controllers
{
    // Accounts. Two roles manage them, with the same pages and a different reach:
    //   UsersAll   (platform admin)  every account, every role, every brokerage house
    //                                (another role that is given UsersAll: everything except platform admin accounts)
    //   UsersHouse (house admin)     the house users and house admins of the own brokerage house
    // Every action below starts from ManagedUsers, so an account outside the reach does not exist for it.
    public abstract partial class CsmsController
    {
        private const Permission A = Permission.UsersAll;
        private const Permission H = Permission.UsersHouse;

        /// <summary>The accounts this role administers.</summary>
        protected IQueryable<User> ManagedUsers
        {
            get
            {
                if (Can(Permission.UsersAll))
                {
                    // Only a platform admin administers platform admins: a role that was given "manage all accounts"
                    // cannot make itself (or anybody) administrator, or lock the administrators out.
                    return MyRole == Role.PlatformAdmin ? Db.Users : Db.Users.Where(u => !u.UCatagory);
                }

                if (Can(Permission.UsersHouse))
                {
                    // XFL staff are never part of a brokerage house's reach, even when their account names that house.
                    var myHouse = CurrentUser?.BrokerageHouseName ?? 0;
                    return Db.Users.Where(u => u.BrokerageHouseName == myHouse && !u.UType && !u.UCatagory);
                }

                return Db.Users.Where(u => false);
            }
        }

        /// <summary>The roles this role may give to an account.</summary>
        protected Role[] AssignableRoles
        {
            get
            {
                if (Can(Permission.UsersAll))
                {
                    return MyRole == Role.PlatformAdmin ? Rbac.RolesByReach : Rbac.RolesByReach.Where(role => role != Role.PlatformAdmin).ToArray();
                }
                if (Can(Permission.UsersHouse)) { return new[] { Role.HouseUser, Role.HouseAdmin }; }
                return Array.Empty<Role>();
            }
        }

        /// <summary>The house a house admin is bound to; null for the platform admin (any house).</summary>
        private int? FixedHouse => Can(Permission.UsersAll) ? null : CurrentUser?.BrokerageHouseName;

        [RequirePermission(A, H)]
        public async Task<IActionResult> UserList()
        {
            try
            {
                var users = await ManagedUsers.ToListAsync();
                ViewBag.Houses = await Db.Brokerages.ToDictionaryAsync(b => b.BrokerageId, b => b.BrokerageHouseName);
                ViewBag.Branches = await Db.Branchhs.ToDictionaryAsync(b => b.BranchId, b => b.BranchName);
                return View(users);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(A, H)]
        public async Task<IActionResult> UserView(int id)
        {
            try
            {
                var user = await ManagedUsers.FirstOrDefaultAsync(item => item.Id == id);
                if (user == null)
                {
                    return NotFound();
                }

                return View(new UserView
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    Department = user.Department ?? string.Empty,
                    Email = user.Email,
                    PhonNumber = user.PhonNumber,
                    Designation = user.Designation ?? string.Empty,
                    BrokerageHouseName = await Db.Brokerages.Where(b => b.BrokerageId == user.BrokerageHouseName).Select(b => b.BrokerageHouseName).FirstOrDefaultAsync() ?? string.Empty,
                    Branch = await Db.Branchhs.Where(b => b.BranchId == user.Branch).Select(b => b.BranchName).FirstOrDefaultAsync() ?? string.Empty,
                    EmployeeId = user.EmployeeId,
                    UserName = user.UserName,
                    UCatagory = user.UCatagory,
                    UType = user.UType,
                    UStatus = user.UStatus,
                    VerifiedAt = user.VerifiedAt,
                    Role = Rbac.RoleOf(user),
                    IsSelf = user.Id == CurrentUser!.Id,
                    TicketCount = await Db.Issues.CountAsync(i => i.UserId == user.Id)
                });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        // ---- create ----------------------------------------------------------------------------

        [RequirePermission(A, H)]
        public async Task<IActionResult> CreateUser()
        {
            try
            {
                return View(await FillChoicesAsync(new NewUser { Role = Role.HouseUser, BrokerageId = FixedHouse }));
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpPost]
        [RequirePermission(A, H)]
        public async Task<IActionResult> CreateUser(NewUser form)
        {
            try
            {
                form.FullName = (form.FullName ?? string.Empty).Trim();
                form.Email = (form.Email ?? string.Empty).Trim();
                form.UserName = (form.UserName ?? string.Empty).Trim();
                form.EmployeeId = (form.EmployeeId ?? string.Empty).Trim();
                if (FixedHouse != null)
                {
                    // a house admin creates accounts for the own house only: whatever was posted does not count
                    form.BrokerageId = FixedHouse;
                    ModelState.Remove(nameof(NewUser.BrokerageId));
                }

                if (!AssignableRoles.Contains(form.Role))
                {
                    ModelState.AddModelError(nameof(NewUser.Role), "You cannot give this role.");
                }

                if (ModelState.IsValid)
                {
                    await CheckHouseAndBranchAsync(form.BrokerageId ?? 0, form.BranchId ?? 0, nameof(NewUser.BrokerageId), nameof(NewUser.BranchId));

                    // People sign in with user name OR email, so neither may collide with the other column.
                    if (await Db.Users.AnyAsync(u => u.Email == form.Email || u.UserName == form.Email))
                    {
                        ModelState.AddModelError(nameof(NewUser.Email), "This email is already registered.");
                    }

                    if (await Db.Users.AnyAsync(u => u.UserName == form.UserName || u.Email == form.UserName))
                    {
                        ModelState.AddModelError(nameof(NewUser.UserName), "This user name is already taken.");
                    }
                }

                if (!ModelState.IsValid)
                {
                    return View(await FillChoicesAsync(form));
                }

                var columns = Rbac.Columns(form.Role);
                PasswordHasher.Create(form.Password, out byte[] passwordHash, out byte[] passwordSalt);
                var user = new User
                {
                    FullName = form.FullName,
                    Email = form.Email,
                    PhonNumber = form.PhonNumber,
                    Designation = (form.Designation ?? string.Empty).Trim(),
                    BrokerageHouseName = form.BrokerageId!.Value,
                    BrokerageHouseAcronym = form.BrokerageId.Value,
                    Branch = form.BranchId!.Value,
                    EmployeeId = form.EmployeeId,
                    UserName = form.UserName,
                    PasswordHash = passwordHash,
                    PasswordSalt = passwordSalt,
                    VerifiedAt = DateTime.Now, // created by an administrator: nothing left to confirm
                    MustChangePassword = true, // the administrator knows this password: the owner chooses an own one at the first sign-in
                    Department = columns.Position,
                    UType = columns.IsXflStaff,
                    UCatagory = columns.IsAdmin,
                    UStatus = true,
                    Terms = true
                };

                await Db.Users.AddAsync(user);
                await Db.SaveChangesAsync(); // the account gets its id here

                Audit(AuditActions.UserCreate, "User", user.Id, user.FullName + " (" + user.UserName + ")",
                    "Created the account as " + Rbac.Label(form.Role) + ", " + user.Email, Rbac.HouseOf(user));
                HttpContext.RequestServices.GetRequiredService<XFLCSMS.Services.Notify.NotificationService>().AccountCreated(user, CurrentUser);
                await Db.SaveChangesAsync();

                TempData["SuccessMessage"] = $"{user.FullName} can sign in now as {Rbac.Label(form.Role)} with the user name {user.UserName} and the password you entered. Ask them to change it after the first sign-in.";
                return RedirectToAction("UserList");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        private async Task<NewUser> FillChoicesAsync(NewUser form)
        {
            var house = FixedHouse;
            form.Brokerages = await Db.Brokerages.Where(b => house == null || b.BrokerageId == house).OrderBy(b => b.BrokerageHouseName).ToListAsync();
            form.FixedHouseName = house == null ? null : form.Brokerages.Select(b => b.BrokerageHouseName).FirstOrDefault();
            form.Branches = form.BrokerageId > 0
                ? await Db.Branchhs.Where(b => b.BrokerageId == form.BrokerageId).OrderBy(b => b.BranchName).ToListAsync()
                : new List<Branchh>();
            if (form.BranchId != null && form.Branches.All(b => b.BranchId != form.BranchId))
            {
                form.BranchId = null; // a branch of another house is not kept as the choice
            }

            form.Roles = AssignableRoles.ToList();
            form.Password = string.Empty;
            form.ConfirmPassword = string.Empty;
            return form;
        }

        private async Task CheckHouseAndBranchAsync(int houseId, int branchId, string houseField, string branchField)
        {
            if (!await Db.Brokerages.AnyAsync(b => b.BrokerageId == houseId))
            {
                ModelState.AddModelError(houseField, "Choose the brokerage house.");
            }
            else if (!await Db.Branchhs.AnyAsync(b => b.BranchId == branchId && b.BrokerageId == houseId))
            {
                ModelState.AddModelError(branchField, "Choose a branch of this brokerage house.");
            }
        }

        // ---- edit ------------------------------------------------------------------------------

        [RequirePermission(A, H)]
        public async Task<IActionResult> EditUser(int id)
        {
            try
            {
                var user = await ManagedUsers.FirstOrDefaultAsync(item => item.Id == id);
                if (user == null)
                {
                    return NotFound();
                }

                return View(await FillEditAsync(new EditUserForm
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    Email = user.Email,
                    PhonNumber = user.PhonNumber,
                    Designation = user.Designation,
                    EmployeeId = user.EmployeeId,
                    BrokerageId = user.BrokerageHouseName,
                    BranchId = user.Branch,
                    Role = Rbac.RoleOf(user),
                    UStatus = user.UStatus
                }, user));
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        private async Task<EditUserForm> FillEditAsync(EditUserForm form, User user)
        {
            form.UserName = user.UserName;
            form.VerifiedAt = user.VerifiedAt;
            form.LockedUntil = user.LockedUntil;
            form.FailedAttempts = user.FailedAttempts;
            form.MustChangePassword = user.MustChangePassword;
            form.IsSelf = user.Id == CurrentUser!.Id;
            form.TicketCount = await Db.Issues.CountAsync(i => i.UserId == user.Id);
            // Tickets carry the house they were raised for, so an account that has raised tickets stays with its house.
            form.CanChangeHouse = FixedHouse == null && form.TicketCount == 0;
            if (!form.CanChangeHouse)
            {
                form.BrokerageId = user.BrokerageHouseName;
            }

            form.Brokerages = await Db.Brokerages.Where(b => form.CanChangeHouse || b.BrokerageId == user.BrokerageHouseName).OrderBy(b => b.BrokerageHouseName).ToListAsync();
            form.Branches = await Db.Branchhs.Where(b => b.BrokerageId == form.BrokerageId).OrderBy(b => b.BranchName).ToListAsync();
            // the current role stays selectable even when this administrator could not give it (it is then the only extra choice)
            form.Roles = AssignableRoles.Union(new[] { Rbac.RoleOf(user) }).OrderBy(role => Array.IndexOf(Rbac.RolesByReach, role)).ToList();
            return form;
        }

        [HttpPost]
        [RequirePermission(A, H)]
        public async Task<IActionResult> UpdateUser(EditUserForm form)
        {
            try
            {
                var me = CurrentUser!;
                var user = await ManagedUsers.FirstOrDefaultAsync(a => a.Id == form.Id);
                if (user == null)
                {
                    return NotFound();
                }

                var oldRole = Rbac.RoleOf(user);
                var oldHouse = Rbac.HouseOf(user);
                var tickets = await Db.Issues.CountAsync(i => i.UserId == user.Id);
                var houseId = FixedHouse == null && tickets == 0 ? form.BrokerageId ?? user.BrokerageHouseName : user.BrokerageHouseName;

                form.FullName = (form.FullName ?? string.Empty).Trim();
                form.Email = (form.Email ?? string.Empty).Trim();
                form.EmployeeId = (form.EmployeeId ?? string.Empty).Trim();

                // An administrator who takes his own rights away, or disables himself, may leave nobody who can undo it.
                if (user.Id == me.Id && (form.Role != oldRole || !form.UStatus))
                {
                    ModelState.AddModelError(string.Empty, "You cannot change your own role or disable your own account. Ask another administrator.");
                }

                if (form.Role != oldRole && !AssignableRoles.Contains(form.Role))
                {
                    ModelState.AddModelError(nameof(EditUserForm.Role), "You cannot give this role.");
                }

                if (ModelState.IsValid)
                {
                    await CheckHouseAndBranchAsync(houseId, form.BranchId ?? 0, nameof(EditUserForm.BrokerageId), nameof(EditUserForm.BranchId));

                    if (form.Email != user.Email && await Db.Users.AnyAsync(u => u.Id != user.Id && (u.Email == form.Email || u.UserName == form.Email)))
                    {
                        ModelState.AddModelError(nameof(EditUserForm.Email), "This email is already registered.");
                    }
                }

                if (!ModelState.IsValid)
                {
                    form.BrokerageId = houseId;
                    return View("EditUser", await FillEditAsync(form, user));
                }

                var changes = new List<string>();
                void Change(string what, string? before, string? after, Action apply)
                {
                    if ((before ?? string.Empty) == (after ?? string.Empty)) { return; }
                    changes.Add(what + " from “" + before + "” to “" + after + "”");
                    apply();
                }

                var oldName = user.FullName;
                Change("name", user.FullName, form.FullName, () => user.FullName = form.FullName);
                if (oldName != user.FullName)
                {
                    // tickets show the name of their engineer: keep it in step, and store the id where only the name was
                    var held = oldRole == Role.SupportEngineer
                        ? await Db.Issues.Where(i => i.AssignedToId == user.Id || (i.AssignedToId == null && i.AssignBy == oldName)).ToListAsync()
                        : await Db.Issues.Where(i => i.AssignedToId == user.Id).ToListAsync();
                    foreach (var ticket in held)
                    {
                        ticket.AssignedToId = user.Id;
                        ticket.AssignBy = user.FullName;
                    }
                }
                Change("email", user.Email, form.Email, () => user.Email = form.Email);
                Change("phone", user.PhonNumber, form.PhonNumber, () => user.PhonNumber = form.PhonNumber);
                Change("designation", user.Designation, (form.Designation ?? string.Empty).Trim(), () => user.Designation = (form.Designation ?? string.Empty).Trim());
                Change("employee ID", user.EmployeeId, form.EmployeeId, () => user.EmployeeId = form.EmployeeId);

                if (houseId != user.BrokerageHouseName)
                {
                    var names = await Db.Brokerages.Where(b => b.BrokerageId == houseId || b.BrokerageId == user.BrokerageHouseName).ToDictionaryAsync(b => b.BrokerageId, b => b.BrokerageHouseName);
                    changes.Add("brokerage house from “" + names.GetValueOrDefault(user.BrokerageHouseName) + "” to “" + names.GetValueOrDefault(houseId) + "”");
                    user.BrokerageHouseName = houseId;
                    user.BrokerageHouseAcronym = houseId;
                }

                var branchId = form.BranchId!.Value;
                if (branchId != user.Branch)
                {
                    var names = await Db.Branchhs.Where(b => b.BranchId == branchId || b.BranchId == user.Branch).ToDictionaryAsync(b => b.BranchId, b => b.BranchName);
                    changes.Add("branch from “" + names.GetValueOrDefault(user.Branch) + "” to “" + names.GetValueOrDefault(branchId) + "”");
                    user.Branch = branchId;
                }

                if (form.Role != oldRole)
                {
                    var columns = Rbac.Columns(form.Role);
                    user.UCatagory = columns.IsAdmin;
                    user.UType = columns.IsXflStaff;
                    user.Department = columns.Position;
                    changes.Add("role from " + Rbac.Label(oldRole) + " to " + Rbac.Label(form.Role));
                }

                if (form.UStatus != user.UStatus)
                {
                    user.UStatus = form.UStatus;
                    changes.Add(form.UStatus ? "account enabled" : "account disabled");
                }

                if (changes.Count > 0)
                {
                    // the line belongs to the house the account was in, so its house admin sees where the account went
                    Audit(AuditActions.UserUpdate, "User", user.Id, user.FullName + " (" + user.UserName + ")",
                        "Changed " + string.Join("; ", changes), oldHouse ?? Rbac.HouseOf(user));
                    await Db.SaveChangesAsync();
                    TempData["SuccessMessage"] = user.FullName + " was saved." + (form.Role != oldRole || !form.UStatus ? " If they are signed in, they are signed out at their next click." : string.Empty);
                }

                return RedirectToAction("UserList");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        // ---- activate, password, delete -------------------------------------------------------

        /// <summary>Activate an account that registered but never entered the token from the e-mail.</summary>
        [HttpPost]
        [RequirePermission(A, H)]
        public async Task<IActionResult> ActivateUser(int id)
        {
            try
            {
                var user = await ManagedUsers.FirstOrDefaultAsync(u => u.Id == id);
                if (user == null)
                {
                    return NotFound();
                }

                if (user.VerifiedAt == null)
                {
                    user.VerifiedAt = DateTime.Now;
                    Audit(AuditActions.UserActivate, "User", user.Id, user.FullName + " (" + user.UserName + ")",
                        "Activated the account without the e-mail token", Rbac.HouseOf(user));
                    HttpContext.RequestServices.GetRequiredService<XFLCSMS.Services.Notify.NotificationService>().AccountActivated(user, CurrentUser);
                    await Db.SaveChangesAsync();
                    TempData["SuccessMessage"] = user.UStatus
                        ? $"{user.FullName} is activated and can sign in now."
                        : $"{user.FullName} is activated, but the account is disabled. Set it to Active under Edit.";
                }

                return RedirectToAction("UserList");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>Give a user a new password, for when "Forgot your password?" does not reach them.</summary>
        [HttpPost]
        [RequirePermission(A, H)]
        public async Task<IActionResult> SetUserPassword(SetPassword form)
        {
            try
            {
                var user = await ManagedUsers.FirstOrDefaultAsync(u => u.Id == form.Id);
                if (user == null)
                {
                    return NotFound();
                }

                if (user.Id == CurrentUser!.Id)
                {
                    TempData["ErrorMessage"] = "For your own account use Change password in the menu at the top right.";
                    return RedirectToAction("EditUser", new { id = user.Id });
                }

                if (!ModelState.IsValid)
                {
                    TempData["ErrorMessage"] = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault()
                        ?? "The password was not accepted.";
                    return RedirectToAction("EditUser", new { id = user.Id });
                }

                PasswordHasher.Create(form.NewPassword, out byte[] passwordHash, out byte[] passwordSalt);
                user.PasswordHash = passwordHash;
                user.PasswordSalt = passwordSalt;
                user.PasswordResetToken = null;
                user.ResetTokenExpires = null;
                // the administrator knows this password, so the owner has to replace it at the next sign-in;
                // a lock from wrong attempts ends with the new password
                user.MustChangePassword = true;
                user.FailedAttempts = 0;
                user.LockedUntil = null;
                Audit(AuditActions.UserPasswordSet, "User", user.Id, user.FullName + " (" + user.UserName + ")", "Set a new password for the account", Rbac.HouseOf(user));
                HttpContext.RequestServices.GetRequiredService<XFLCSMS.Services.Notify.NotificationService>().AccountPasswordSet(user, CurrentUser);
                await Db.SaveChangesAsync();

                TempData["SuccessMessage"] = $"New password saved for {user.FullName}. They have to choose their own password when they sign in with it.";
                return RedirectToAction("EditUser", new { id = user.Id });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>End the lock an account got from too many wrong passwords, without waiting for it to run out.</summary>
        [HttpPost]
        [RequirePermission(A, H)]
        public async Task<IActionResult> UnlockUser(int id)
        {
            try
            {
                var user = await ManagedUsers.FirstOrDefaultAsync(u => u.Id == id);
                if (user == null)
                {
                    return NotFound();
                }

                if (user.LockedUntil != null || user.FailedAttempts > 0)
                {
                    user.LockedUntil = null;
                    user.FailedAttempts = 0;
                    Audit(AuditActions.UserUnlock, "User", user.Id, user.FullName + " (" + user.UserName + ")", "Ended the lock after wrong sign-in attempts", Rbac.HouseOf(user));
                    await Db.SaveChangesAsync();
                }

                TempData["SuccessMessage"] = $"{user.FullName} can sign in again.";
                return RedirectToAction("EditUser", new { id = user.Id });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpDelete]
        [RequirePermission(A, H)]
        public async Task<IActionResult> DeleteUser(int id)
        {
            try
            {
                if (id == CurrentUser!.Id)
                {
                    return Conflict("You cannot delete the account you are signed in with.");
                }

                var user = await ManagedUsers.FirstOrDefaultAsync(u => u.Id == id);
                if (user == null)
                {
                    return NotFound("The user was not found.");
                }

                // Deleting the user would cascade and delete every ticket he raised.
                if (await Db.Issues.AnyAsync(i => i.UserId == id))
                {
                    return Conflict("This user has raised tickets, so the account cannot be deleted. Disable the user instead (Edit > Account).");
                }

                Audit(AuditActions.UserDelete, "User", user.Id, user.FullName + " (" + user.UserName + ")",
                    "Deleted the account (" + Rbac.Label(Rbac.RoleOf(user)) + ", " + user.Email + ")", Rbac.HouseOf(user));
                Db.Users.Remove(user);
                await Db.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
    }
}
