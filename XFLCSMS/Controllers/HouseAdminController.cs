using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Admin;
using XFLCSMS.Models.Branch;
using XFLCSMS.Services;

namespace XFLCSMS.Controllers
{
    /// <summary>
    /// The administrator of one brokerage house. Every ticket page is the one of MakerController - the role decides
    /// that it shows the tickets of the whole house instead of the own ones (CsmsController.VisibleIssues).
    /// Accounts of the house and its audit trail are the shared pages of CsmsController (Users, Audit).
    /// This class adds the page for the house itself: its branches. (The actions are called ...HouseBranch: the
    /// names AddBranch / DeleteBranch belong to the master data pages, which work on the branches of every house.)
    /// </summary>
    public class HouseAdminController : MakerController
    {
        protected override Role MyRole => Role.HouseAdmin;

        public HouseAdminController(DataContext context, IWebHostEnvironment webHostEnvironment, TicketService tickets)
            : base(context, webHostEnvironment, tickets)
        {
        }

        private int MyHouse => CurrentUser!.BrokerageHouseName;

        /// <summary>The own brokerage house: its branches, and how many people and tickets each has.</summary>
        [HttpGet]
        [RequirePermission(Permission.BranchesHouse)]
        public async Task<IActionResult> Organization()
        {
            try
            {
                return View(await BuildOrganizationAsync());
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        private async Task<OrganizationView> BuildOrganizationAsync()
        {
            var house = await Db.Brokerages.FirstAsync(b => b.BrokerageId == MyHouse);
            var branches = await Db.Branchhs.Where(b => b.BrokerageId == MyHouse).OrderBy(b => b.BranchName).ToListAsync();
            var people = await ManagedUsers.Select(u => new { u.Branch, u.UStatus, u.VerifiedAt, u.Department }).ToListAsync();
            var tickets = await VisibleIssues.Select(i => i.IStatus).ToListAsync();

            return new OrganizationView
            {
                Name = house.BrokerageHouseName,
                Acronym = house.BrokerageHouseAcronym,
                Users = people.Count,
                Admins = people.Count(p => p.Department == Rbac.HouseAdminPosition),
                Waiting = people.Count(p => p.VerifiedAt == null && p.UStatus),
                OpenTickets = tickets.Count(status => status != TicketStatus.Closed),
                ClosedTickets = tickets.Count(status => status == TicketStatus.Closed),
                Branches = branches.Select(b => new OrganizationBranch { Id = b.BranchId, Name = b.BranchName, Users = people.Count(p => p.Branch == b.BranchId) }).ToList()
            };
        }

        private static readonly Regex BranchNameRule = new(@"^[a-z A-Z.]+$");

        private async Task<string?> CheckBranchNameAsync(string name, int exceptId)
        {
            if (name.Length == 0) { return "Enter the name of the branch."; }
            if (name.Length > 100) { return "The name is too long."; }
            if (!BranchNameRule.IsMatch(name)) { return "A branch name has letters, spaces and dots only."; }
            if (await Db.Branchhs.AnyAsync(b => b.BrokerageId == MyHouse && b.BranchId != exceptId && b.BranchName == name)) { return "Your house already has a branch with this name."; }
            return null;
        }

        [HttpPost]
        [RequirePermission(Permission.BranchesHouse)]
        public async Task<IActionResult> AddHouseBranch(string? branchName)
        {
            try
            {
                var name = (branchName ?? string.Empty).Trim();
                var problem = await CheckBranchNameAsync(name, 0);
                if (problem != null)
                {
                    TempData["ErrorMessage"] = problem;
                    return RedirectToAction("Organization");
                }

                Db.Branchhs.Add(new Branchh { BranchName = name, BrokerageId = MyHouse }); // always the own house
                await Db.SaveChangesAsync();
                TempData["SuccessMessage"] = "Branch " + name + " was added. People of your house can choose it when they register.";
                return RedirectToAction("Organization");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpPost]
        [RequirePermission(Permission.BranchesHouse)]
        public async Task<IActionResult> RenameHouseBranch(int id, string? branchName)
        {
            try
            {
                var branch = await Db.Branchhs.FirstOrDefaultAsync(b => b.BranchId == id && b.BrokerageId == MyHouse);
                if (branch == null)
                {
                    return NotFound(); // also a branch of another house
                }

                var name = (branchName ?? string.Empty).Trim();
                var problem = await CheckBranchNameAsync(name, id);
                if (problem != null)
                {
                    TempData["ErrorMessage"] = problem;
                    return RedirectToAction("Organization");
                }

                if (branch.BranchName != name)
                {
                    branch.BranchName = name;
                    await Db.SaveChangesAsync();
                    TempData["SuccessMessage"] = "The branch is called " + name + " now.";
                }

                return RedirectToAction("Organization");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpDelete]
        [RequirePermission(Permission.BranchesHouse)]
        public async Task<IActionResult> DeleteHouseBranch(int id)
        {
            try
            {
                var branch = await Db.Branchhs.FirstOrDefaultAsync(b => b.BranchId == id && b.BrokerageId == MyHouse);
                if (branch == null)
                {
                    return NotFound("The branch was not found.");
                }

                if (await Db.Users.AnyAsync(u => u.Branch == id))
                {
                    return Conflict("People are registered under this branch, so it cannot be deleted. Move them to another branch first (Users > Edit).");
                }

                Db.Branchhs.Remove(branch);
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
