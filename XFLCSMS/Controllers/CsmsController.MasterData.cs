using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Admin;
using XFLCSMS.Models.Affected;
using XFLCSMS.Models.Branch;
using XFLCSMS.Models.Brocarage;
using XFLCSMS.Models.Common;
using XFLCSMS.Models.Issue;
using XFLCSMS.Models.Register;
using XFLCSMS.Models.Support;
using XFLCSMS.Models.Todos;
using XFLCSMS.Services;

namespace XFLCSMS.Controllers
{
    // Master data: brokerage houses, all branches, support types, categories, sub-categories, affected sections.
    // For every role that holds Permission.MasterData (by default the platform admin). The pages are in Views/Shared.
    public abstract partial class CsmsController
    {
        [RequirePermission(Permission.MasterData)]
        public IActionResult CreateBrocarage(int page, int rowperpage, string? searchString = null, string?
            sortField = null, bool sortAscending = true)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                
                return View();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBrocarage([Bind("BrokerageId,BrokerageHouseName,BrokerageHouseAcronym")] Brokerage brokerage)
        {
            try
            {
                brokerage.BrokerageId = 0; // identity column: the database assigns the id
                ValidateBrokerage(brokerage);
                if (!ModelState.IsValid)
                {
                    return View(brokerage);
                }

                await Db.AddAsync(brokerage);
                await Db.SaveChangesAsync();
                TempData["SuccessMessage"] = $"{brokerage.BrokerageHouseName} is created. Add at least one branch: its staff choose a branch when they register.";
                return RedirectToAction("BrocarageHouseList");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        // Ticket numbers are built from the acronym (ABC_0000001), so name and acronym must be unique.
        private void ValidateBrokerage(Brokerage brokerage)
        {
            brokerage.BrokerageHouseName = (brokerage.BrokerageHouseName ?? string.Empty).Trim();
            brokerage.BrokerageHouseAcronym = (brokerage.BrokerageHouseAcronym ?? string.Empty).Trim();

            if (Db.Brokerages.Any(b => b.BrokerageId != brokerage.BrokerageId && b.BrokerageHouseName == brokerage.BrokerageHouseName))
            {
                ModelState.AddModelError(nameof(Brokerage.BrokerageHouseName), "A brokerage house with this name already exists.");
            }

            if (Db.Brokerages.Any(b => b.BrokerageId != brokerage.BrokerageId && b.BrokerageHouseAcronym == brokerage.BrokerageHouseAcronym))
            {
                ModelState.AddModelError(nameof(Brokerage.BrokerageHouseAcronym), "This acronym is already used by another brokerage house.");
            }
        }

        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> BrocarageHouseList()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var Brocareges = await Db.Brokerages.Include(b => b.branches).ToListAsync();
                return View(Brocareges);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }

        }
        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> ViewBrocarageHouse(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var brocarage = await Db.Brokerages.FirstOrDefaultAsync(item => item.BrokerageId == id);

                if (brocarage == null)
                {
                    return NotFound();
                }

                return View(brocarage);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> EditBrocarage(int id)
        {
            try
            {

                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var brocarage = await Db.Brokerages.FirstOrDefaultAsync(item => item.BrokerageId == id);

                if (brocarage == null)
                {
                    return NotFound();
                }

                return View(brocarage);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }

        }
        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        public async Task<IActionResult> UpdateBrocarage(Brokerage brokeragesss)
        {
            try
            {
                var brocarage = await Db.Brokerages.FirstOrDefaultAsync(item => item.BrokerageId == brokeragesss.BrokerageId);
                if (brocarage == null)
                {
                    return NotFound();
                }

                ValidateBrokerage(brokeragesss);
                if (!ModelState.IsValid)
                {
                    return View("EditBrocarage", brokeragesss);
                }

                brocarage.BrokerageHouseName = brokeragesss.BrokerageHouseName;
                brocarage.BrokerageHouseAcronym = brokeragesss.BrokerageHouseAcronym;
                await Db.SaveChangesAsync();
                return RedirectToAction("BrocarageHouseList");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        [HttpDelete]
        public async Task<IActionResult> DeleteBrocarage(int id)
        {
            try
            {
                var house = await Db.Brokerages.FindAsync(id);
                if (house == null)
                {
                    return NotFound("The brokerage house was not found.");
                }

                // Deleting a house in use either fails on the foreign keys or silently wipes all of its tickets.
                if (await Db.Branchhs.AnyAsync(b => b.BrokerageId == id)
                    || await Db.Users.AnyAsync(u => u.BrokerageHouseName == id)
                    || await Db.Issues.AnyAsync(i => i.BrokerageId == id))
                {
                    return Conflict("This brokerage house still has branches, users or tickets, so it cannot be deleted.");
                }

                Db.Brokerages.Remove(house);
                await Db.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }


        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> BranchList()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var Branch = await Db.Branchhs.ToListAsync();
                List<BranchView> branches = new List<BranchView>();
                foreach (var branch in Branch)
                {
                    BranchView b = new BranchView();
                    b.BranchId = branch.BranchId;
                    b.BranchName = branch.BranchName;
                    b.BrokerageHouseName = HouseName(branch.BrokerageId);
                    branches.Add(b);
                }

                return View(branches);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        // brokerageId: the house is already chosen when the page is opened from the house list ("Add a branch").
        [RequirePermission(Permission.MasterData)]
        public IActionResult CreateBranch(int brokerageId = 0)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var Brocarage = Db.Brokerages.ToList();

                BranchView BB = new BranchView();
                BB.brocarage = Brocarage;
                BB.BrokerageId = Brocarage.Any(b => b.BrokerageId == brokerageId) ? brokerageId : 0;

                return View(BB);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }

        }

        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBranch(BranchView branchView)
        {
            try
            {
                if (!await Db.Brokerages.AnyAsync(b => b.BrokerageId == branchView.BrokerageId))
                {
                    ModelState.AddModelError(nameof(BranchView.BrokerageId), "Please select a brokerage house.");
                }

                if (!ModelState.IsValid)
                {
                    branchView.brocarage = await Db.Brokerages.ToListAsync(); // the drop-down needs its options again
                    return View(branchView);
                }

                Branchh branchh = new Branchh();
                branchh.BranchName = branchView.BranchName.Trim();
                branchh.BrokerageId = branchView.BrokerageId;
                await Db.Branchhs.AddAsync(branchh);
                await Db.SaveChangesAsync();
                return RedirectToAction("BranchList");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> ViewBranch(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var branch = await Db.Branchhs.FirstOrDefaultAsync(item => item.BranchId == id);

                if (branch == null)
                {
                    return NotFound();
                }

                BranchView b = new BranchView();

                b.BranchId = branch.BranchId;
                b.BranchName = branch.BranchName;
                b.BrokerageHouseName = HouseName(branch.BrokerageId);

                return View(b);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }


        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> EditBranch(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var branch = await Db.Branchhs.FirstOrDefaultAsync(item => item.BranchId == id);
                var Brocarage = await Db.Brokerages.ToListAsync();

                if (branch == null)
                {
                    return NotFound();
                }

                BranchView b = new BranchView();

                b.BranchId = branch.BranchId;
                b.BranchName = branch.BranchName;
                b.BrokerageHouseName = HouseName(branch.BrokerageId);
                b.brocarage = Brocarage;

                return View(b);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }


        }
        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        public async Task<IActionResult> UpdateBranch(BranchView branchView)
        {
            try
            {
                var branch = await Db.Branchhs.FirstOrDefaultAsync(item => item.BranchId == branchView.BranchId);
                if (branch == null)
                {
                    return NotFound();
                }

                if (!ModelState.IsValid)
                {
                    branchView.BrokerageHouseName = HouseName(branch.BrokerageId) ?? string.Empty;
                    return View("EditBranch", branchView);
                }

                branch.BranchName = branchView.BranchName.Trim();
                await Db.SaveChangesAsync();
                return RedirectToAction("BranchList");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        [HttpDelete]
        public async Task<IActionResult> DeleteBranch(int id)
        {
            try
            {
                var branch = await Db.Branchhs.FirstOrDefaultAsync(i => i.BranchId == id);
                if (branch == null)
                {
                    return NotFound("The branch was not found.");
                }

                if (await Db.Users.AnyAsync(u => u.Branch == id))
                {
                    return Conflict("Users are registered under this branch, so it cannot be deleted.");
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


        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> SupportTypeList()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var supportType = await Db.SupportTypes.ToListAsync();
                return View(supportType);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> ViewSupportType(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var brocarage = await Db.SupportTypes.FirstOrDefaultAsync(item => item.SupportTypeId == id);

                if (brocarage == null)
                {
                    return NotFound(); // Or handle the case where the user is not found
                }

                return View(brocarage);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        public IActionResult CreateSupportType()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                return View();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateSupportType([Bind("SupportTypeId,SType")] SupportType supportType)
        {
            try
            {
                supportType.SupportTypeId = 0; // identity column: the database assigns the id
                if (!ModelState.IsValid)
                {
                    return View(supportType);
                }

                await Db.AddAsync(supportType);
                await Db.SaveChangesAsync();
                return RedirectToAction("SupportTypeList");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }


        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> EditSupportType(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var brocarage = await Db.SupportTypes.FirstOrDefaultAsync(item => item.SupportTypeId == id);

                if (brocarage == null)
                {
                    return NotFound(); // Or handle the case where the user is not found
                }

                return View(brocarage);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }

        }
        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        public async Task<IActionResult> UpdateSupportType(SupportType brokeragesss)
        {
            try
            {
                var existing = await Db.SupportTypes.FirstOrDefaultAsync(item => item.SupportTypeId == brokeragesss.SupportTypeId);
                if (existing == null)
                {
                    return NotFound();
                }

                if (!ModelState.IsValid)
                {
                    return View("EditSupportType", brokeragesss);
                }

                existing.SType = brokeragesss.SType;
                await Db.SaveChangesAsync();
                return RedirectToAction("SupportTypeList");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        [HttpDelete]
        public async Task<IActionResult> DeleteSupportType(int id)
        {
            try
            {
                var item = await Db.SupportTypes.FindAsync(id);
                if (item == null)
                {
                    return NotFound("The support type was not found.");
                }

                // Tickets point at this row; deleting it would fail on the foreign key.
                if (await Db.Issues.AnyAsync(i => i.SupportTypeId == id))
                {
                    return Conflict("This support type is used by existing tickets, so it cannot be deleted.");
                }

                Db.SupportTypes.Remove(item);
                await Db.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }


        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> SupportCatagoryList()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var supportCatagory = await Db.SupportCatagories.ToListAsync();
                return View(supportCatagory);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> ViewSupportCatagory(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var supportCatagory = await Db.SupportCatagories.FirstOrDefaultAsync(item => item.SupportCatagoryId == id);

                if (supportCatagory == null)
                {
                    return NotFound(); // Or handle the case where the user is not found
                }

                return View(supportCatagory);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        public IActionResult CreateSupportCatagory()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                return View();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateSupportCatagory([Bind("SupportCatagoryId,SCatagory")] SupportCatagory supportCatagory)
        {
            try
            {
                supportCatagory.SupportCatagoryId = 0; // identity column: the database assigns the id
                if (!ModelState.IsValid)
                {
                    return View(supportCatagory);
                }

                await Db.AddAsync(supportCatagory);
                await Db.SaveChangesAsync();
                return RedirectToAction("SupportCatagoryList");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> EditSupportCatagory(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var supportCatagory = await Db.SupportCatagories.FirstOrDefaultAsync(item => item.SupportCatagoryId == id);

                if (supportCatagory == null)
                {
                    return NotFound(); // Or handle the case where the user is not found
                }

                return View(supportCatagory);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }

        }
        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        public async Task<IActionResult> UpdateSupportCatagory(SupportCatagory supportCatagory)
        {
            try
            {
                var existing = await Db.SupportCatagories.FirstOrDefaultAsync(item => item.SupportCatagoryId == supportCatagory.SupportCatagoryId);
                if (existing == null)
                {
                    return NotFound();
                }

                if (!ModelState.IsValid)
                {
                    return View("EditSupportCatagory", supportCatagory);
                }

                existing.SCatagory = supportCatagory.SCatagory;
                await Db.SaveChangesAsync();
                return RedirectToAction("SupportCatagoryList");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        [HttpDelete]
        public async Task<IActionResult> DeleteSupportCatagory(int id)
        {
            try
            {
                var item = await Db.SupportCatagories.FindAsync(id);
                if (item == null)
                {
                    return NotFound("The support category was not found.");
                }

                // Tickets point at this row; deleting it would fail on the foreign key.
                if (await Db.Issues.AnyAsync(i => i.SupportCatagoryId == id))
                {
                    return Conflict("This support category is used by existing tickets, so it cannot be deleted.");
                }

                Db.SupportCatagories.Remove(item);
                await Db.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> SupportSubCatagoryList()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var supportSubCatagory = await Db.SupportSubCatagories.ToListAsync();
                return View(supportSubCatagory);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> ViewSupportSubCatagory(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var supportSubCatagory = await Db.SupportSubCatagories.FirstOrDefaultAsync(item => item.SupportSubCatagoryId == id);

                if (supportSubCatagory == null)
                {
                    return NotFound(); // Or handle the case where the user is not found
                }

                return View(supportSubCatagory);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
        [RequirePermission(Permission.MasterData)]
        public IActionResult CreateSupportSubCatagory()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                return View();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateSupportSubCatagory([Bind("SupportSubCatagoryId,SubCatagory")] SupportSubCatagory supportSubCatagory)
        {
            try
            {
                supportSubCatagory.SupportSubCatagoryId = 0; // identity column: the database assigns the id
                if (!ModelState.IsValid)
                {
                    return View(supportSubCatagory);
                }

                await Db.AddAsync(supportSubCatagory);
                await Db.SaveChangesAsync();
                return RedirectToAction("SupportSubCatagoryList");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> EditSupportSubCatagory(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var supportSubCatagory = await Db.SupportSubCatagories.FirstOrDefaultAsync(item => item.SupportSubCatagoryId == id);

                if (supportSubCatagory == null)
                {
                    return NotFound(); // Or handle the case where the user is not found
                }

                return View(supportSubCatagory);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }

        }
        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        public async Task<IActionResult> UpdateSupportSubCatagory(SupportSubCatagory supportSubCatagory)
        {
            try
            {
                var existing = await Db.SupportSubCatagories.FirstOrDefaultAsync(item => item.SupportSubCatagoryId == supportSubCatagory.SupportSubCatagoryId);
                if (existing == null)
                {
                    return NotFound();
                }

                if (!ModelState.IsValid)
                {
                    return View("EditSupportSubCatagory", supportSubCatagory);
                }

                existing.SubCatagory = supportSubCatagory.SubCatagory;
                await Db.SaveChangesAsync();
                return RedirectToAction("SupportSubCatagoryList");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        [HttpDelete]
        public async Task<IActionResult> DeleteSupportSubCatagory(int id)
        {
            try
            {
                var item = await Db.SupportSubCatagories.FindAsync(id);
                if (item == null)
                {
                    return NotFound("The support sub-category was not found.");
                }

                // Tickets point at this row; deleting it would fail on the foreign key.
                if (await Db.Issues.AnyAsync(i => i.SupportSubCatagoryId == id))
                {
                    return Conflict("This support sub-category is used by existing tickets, so it cannot be deleted.");
                }

                Db.SupportSubCatagories.Remove(item);
                await Db.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }



        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> AffectedSectionList()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var affectedSectios = await Db.AffectedSectionss.ToListAsync();
                return View(affectedSectios);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> ViewAffectedSection(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var affectedsection = await Db.AffectedSectionss.FirstOrDefaultAsync(item => item.AffectedSectionId == id);

                if (affectedsection == null)
                {
                    return NotFound(); // Or handle the case where the user is not found
                }

                return View(affectedsection);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
        [RequirePermission(Permission.MasterData)]
        public IActionResult CreateAffectedSection()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                return View();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAffectedSection([Bind("AffectedSectionId,ASection")] AffectedSection affectedSection)
        {
            try
            {
                affectedSection.AffectedSectionId = 0; // identity column: the database assigns the id
                if (!ModelState.IsValid)
                {
                    return View(affectedSection);
                }

                await Db.AddAsync(affectedSection);
                await Db.SaveChangesAsync();
                return RedirectToAction("AffectedSectionList");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> EditAffectedSection(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var affectedSection = await Db.AffectedSectionss.FirstOrDefaultAsync(item => item.AffectedSectionId == id);

                if (affectedSection == null)
                {
                    return NotFound(); // Or handle the case where the user is not found
                }

                return View(affectedSection);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }

        }
        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        public async Task<IActionResult> UpdateAffectedSection(AffectedSection affectedSection)
        {
            try
            {
                var existing = await Db.AffectedSectionss.FirstOrDefaultAsync(item => item.AffectedSectionId == affectedSection.AffectedSectionId);
                if (existing == null)
                {
                    return NotFound();
                }

                if (!ModelState.IsValid)
                {
                    return View("EditAffectedSection", affectedSection);
                }

                existing.ASection = affectedSection.ASection;
                await Db.SaveChangesAsync();
                return RedirectToAction("AffectedSectionList");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        [HttpDelete]
        public async Task<IActionResult> DeleteAffectedSection(int id)
        {
            try
            {
                var item = await Db.AffectedSectionss.FindAsync(id);
                if (item == null)
                {
                    return NotFound("The affected section was not found.");
                }

                // Tickets point at this row; deleting it would fail on the foreign key.
                if (await Db.Issues.AnyAsync(i => i.AffectedSectionId == id))
                {
                    return Conflict("This affected section is used by existing tickets, so it cannot be deleted.");
                }

                Db.AffectedSectionss.Remove(item);
                await Db.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>Name of a brokerage house, or null when it does not exist.</summary>
        protected string? HouseName(int? id)
        {
            return id == null ? null : Db.Brokerages.Where(b => b.BrokerageId == id).Select(b => b.BrokerageHouseName).FirstOrDefault();
        }

        /// <summary>Full name of a user, or null when the account does not exist.</summary>
        protected string? PersonName(int? id)
        {
            return id == null ? null : Db.Users.Where(u => u.Id == id).Select(u => u.FullName).FirstOrDefault();
        }
    }
}
