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
    // (The products and what the support lists share: CsmsController.Products.cs.)
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


        // ---- support types ---------------------------------------------------------------------------

        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> SupportTypeList()
        {
            try
            {
                return View(await Db.SupportTypes.Include(item => item.Product).OrderBy(item => item.SupportTypeId).ToListAsync());
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
                var item = await Db.SupportTypes.Include(row => row.Product).FirstOrDefaultAsync(row => row.SupportTypeId == id);
                if (item == null)
                {
                    return NotFound();
                }

                return View(item);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <param name="productId">The product the new entry is for (the link on the page of a product passes it).</param>
        /// <param name="back">"product": return to the page of the product afterwards instead of the list.</param>
        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> CreateSupportType(int? productId = null, string? back = null)
        {
            try
            {
                await LoadProductChoicesAsync(back);
                return View(new SupportType { ProductId = await ExistingProductAsync(productId) });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateSupportType([Bind("SupportTypeId,SType,ProductId")] SupportType supportType, string? back = null)
        {
            try
            {
                supportType.SupportTypeId = 0; // identity column: the database assigns the id
                supportType.SType = CleanName(supportType.SType);
                Revalidate(supportType, "SType"); // the rules are checked on the tidy name
                await CheckProductAsync(supportType.ProductId);
                if (ModelState.IsValid && await Db.SupportTypes.AnyAsync(row => row.SType == supportType.SType && row.ProductId == supportType.ProductId))
                {
                    ModelState.AddModelError(nameof(SupportType.SType), TakenMessage("support type", supportType.ProductId));
                }

                if (!ModelState.IsValid)
                {
                    await LoadProductChoicesAsync(back);
                    return View(supportType);
                }

                await Db.AddAsync(supportType);
                await Db.SaveChangesAsync();
                return BackToSupportList("SupportTypeList", supportType.ProductId, back);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> EditSupportType(int id, string? back = null)
        {
            try
            {
                var item = await Db.SupportTypes.FirstOrDefaultAsync(row => row.SupportTypeId == id);
                if (item == null)
                {
                    return NotFound();
                }

                await LoadProductChoicesAsync(back);
                return View(item);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        public async Task<IActionResult> UpdateSupportType([Bind("SupportTypeId,SType,ProductId")] SupportType supportType, string? back = null)
        {
            try
            {
                var existing = await Db.SupportTypes.FirstOrDefaultAsync(row => row.SupportTypeId == supportType.SupportTypeId);
                if (existing == null)
                {
                    return NotFound();
                }

                supportType.SType = CleanName(supportType.SType);
                Revalidate(supportType, "SType"); // the rules are checked on the tidy name
                await CheckProductAsync(supportType.ProductId);
                // tickets of one product must not end up with an entry of another: an entry in use can be opened to all
                // products, but it moves to a product only when every ticket that uses it is of that product
                if (supportType.ProductId != null && supportType.ProductId != existing.ProductId
                    && await Db.Issues.AnyAsync(issue => issue.SupportTypeId == existing.SupportTypeId && (issue.ProductId == null || issue.ProductId != supportType.ProductId)))
                {
                    ModelState.AddModelError("ProductId", "Tickets of other products use this entry, so it cannot move to this product. It can be offered for all products.");
                }

                if (ModelState.IsValid && await Db.SupportTypes.AnyAsync(row => row.SType == supportType.SType && row.ProductId == supportType.ProductId && row.SupportTypeId != existing.SupportTypeId))
                {
                    ModelState.AddModelError(nameof(SupportType.SType), TakenMessage("support type", supportType.ProductId));
                }

                if (!ModelState.IsValid)
                {
                    await LoadProductChoicesAsync(back);
                    return View("EditSupportType", supportType);
                }

                existing.SType = supportType.SType;
                existing.ProductId = supportType.ProductId;
                await Db.SaveChangesAsync();
                return BackToSupportList("SupportTypeList", existing.ProductId, back);
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

        // ---- support categories ---------------------------------------------------------------------------

        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> SupportCatagoryList()
        {
            try
            {
                return View(await Db.SupportCatagories.Include(item => item.Product).OrderBy(item => item.SupportCatagoryId).ToListAsync());
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
                var item = await Db.SupportCatagories.Include(row => row.Product).FirstOrDefaultAsync(row => row.SupportCatagoryId == id);
                if (item == null)
                {
                    return NotFound();
                }

                return View(item);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <param name="productId">The product the new entry is for (the link on the page of a product passes it).</param>
        /// <param name="back">"product": return to the page of the product afterwards instead of the list.</param>
        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> CreateSupportCatagory(int? productId = null, string? back = null)
        {
            try
            {
                await LoadProductChoicesAsync(back);
                return View(new SupportCatagory { ProductId = await ExistingProductAsync(productId) });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateSupportCatagory([Bind("SupportCatagoryId,SCatagory,ProductId")] SupportCatagory supportCatagory, string? back = null)
        {
            try
            {
                supportCatagory.SupportCatagoryId = 0; // identity column: the database assigns the id
                supportCatagory.SCatagory = CleanName(supportCatagory.SCatagory);
                Revalidate(supportCatagory, "SCatagory"); // the rules are checked on the tidy name
                await CheckProductAsync(supportCatagory.ProductId);
                if (ModelState.IsValid && await Db.SupportCatagories.AnyAsync(row => row.SCatagory == supportCatagory.SCatagory && row.ProductId == supportCatagory.ProductId))
                {
                    ModelState.AddModelError(nameof(SupportCatagory.SCatagory), TakenMessage("support category", supportCatagory.ProductId));
                }

                if (!ModelState.IsValid)
                {
                    await LoadProductChoicesAsync(back);
                    return View(supportCatagory);
                }

                await Db.AddAsync(supportCatagory);
                await Db.SaveChangesAsync();
                return BackToSupportList("SupportCatagoryList", supportCatagory.ProductId, back);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> EditSupportCatagory(int id, string? back = null)
        {
            try
            {
                var item = await Db.SupportCatagories.FirstOrDefaultAsync(row => row.SupportCatagoryId == id);
                if (item == null)
                {
                    return NotFound();
                }

                await LoadProductChoicesAsync(back);
                return View(item);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        public async Task<IActionResult> UpdateSupportCatagory([Bind("SupportCatagoryId,SCatagory,ProductId")] SupportCatagory supportCatagory, string? back = null)
        {
            try
            {
                var existing = await Db.SupportCatagories.FirstOrDefaultAsync(row => row.SupportCatagoryId == supportCatagory.SupportCatagoryId);
                if (existing == null)
                {
                    return NotFound();
                }

                supportCatagory.SCatagory = CleanName(supportCatagory.SCatagory);
                Revalidate(supportCatagory, "SCatagory"); // the rules are checked on the tidy name
                await CheckProductAsync(supportCatagory.ProductId);
                // tickets of one product must not end up with an entry of another: an entry in use can be opened to all
                // products, but it moves to a product only when every ticket that uses it is of that product
                if (supportCatagory.ProductId != null && supportCatagory.ProductId != existing.ProductId
                    && await Db.Issues.AnyAsync(issue => issue.SupportCatagoryId == existing.SupportCatagoryId && (issue.ProductId == null || issue.ProductId != supportCatagory.ProductId)))
                {
                    ModelState.AddModelError("ProductId", "Tickets of other products use this entry, so it cannot move to this product. It can be offered for all products.");
                }

                if (ModelState.IsValid && await Db.SupportCatagories.AnyAsync(row => row.SCatagory == supportCatagory.SCatagory && row.ProductId == supportCatagory.ProductId && row.SupportCatagoryId != existing.SupportCatagoryId))
                {
                    ModelState.AddModelError(nameof(SupportCatagory.SCatagory), TakenMessage("support category", supportCatagory.ProductId));
                }

                if (!ModelState.IsValid)
                {
                    await LoadProductChoicesAsync(back);
                    return View("EditSupportCatagory", supportCatagory);
                }

                existing.SCatagory = supportCatagory.SCatagory;
                existing.ProductId = supportCatagory.ProductId;
                await Db.SaveChangesAsync();
                return BackToSupportList("SupportCatagoryList", existing.ProductId, back);
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

        // ---- support sub-categories ---------------------------------------------------------------------------

        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> SupportSubCatagoryList()
        {
            try
            {
                return View(await Db.SupportSubCatagories.Include(item => item.Product).OrderBy(item => item.SupportSubCatagoryId).ToListAsync());
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
                var item = await Db.SupportSubCatagories.Include(row => row.Product).FirstOrDefaultAsync(row => row.SupportSubCatagoryId == id);
                if (item == null)
                {
                    return NotFound();
                }

                return View(item);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <param name="productId">The product the new entry is for (the link on the page of a product passes it).</param>
        /// <param name="back">"product": return to the page of the product afterwards instead of the list.</param>
        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> CreateSupportSubCatagory(int? productId = null, string? back = null)
        {
            try
            {
                await LoadProductChoicesAsync(back);
                return View(new SupportSubCatagory { ProductId = await ExistingProductAsync(productId) });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateSupportSubCatagory([Bind("SupportSubCatagoryId,SubCatagory,ProductId")] SupportSubCatagory supportSubCatagory, string? back = null)
        {
            try
            {
                supportSubCatagory.SupportSubCatagoryId = 0; // identity column: the database assigns the id
                supportSubCatagory.SubCatagory = CleanName(supportSubCatagory.SubCatagory);
                Revalidate(supportSubCatagory, "SubCatagory"); // the rules are checked on the tidy name
                await CheckProductAsync(supportSubCatagory.ProductId);
                if (ModelState.IsValid && await Db.SupportSubCatagories.AnyAsync(row => row.SubCatagory == supportSubCatagory.SubCatagory && row.ProductId == supportSubCatagory.ProductId))
                {
                    ModelState.AddModelError(nameof(SupportSubCatagory.SubCatagory), TakenMessage("support sub-category", supportSubCatagory.ProductId));
                }

                if (!ModelState.IsValid)
                {
                    await LoadProductChoicesAsync(back);
                    return View(supportSubCatagory);
                }

                await Db.AddAsync(supportSubCatagory);
                await Db.SaveChangesAsync();
                return BackToSupportList("SupportSubCatagoryList", supportSubCatagory.ProductId, back);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> EditSupportSubCatagory(int id, string? back = null)
        {
            try
            {
                var item = await Db.SupportSubCatagories.FirstOrDefaultAsync(row => row.SupportSubCatagoryId == id);
                if (item == null)
                {
                    return NotFound();
                }

                await LoadProductChoicesAsync(back);
                return View(item);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        public async Task<IActionResult> UpdateSupportSubCatagory([Bind("SupportSubCatagoryId,SubCatagory,ProductId")] SupportSubCatagory supportSubCatagory, string? back = null)
        {
            try
            {
                var existing = await Db.SupportSubCatagories.FirstOrDefaultAsync(row => row.SupportSubCatagoryId == supportSubCatagory.SupportSubCatagoryId);
                if (existing == null)
                {
                    return NotFound();
                }

                supportSubCatagory.SubCatagory = CleanName(supportSubCatagory.SubCatagory);
                Revalidate(supportSubCatagory, "SubCatagory"); // the rules are checked on the tidy name
                await CheckProductAsync(supportSubCatagory.ProductId);
                // tickets of one product must not end up with an entry of another: an entry in use can be opened to all
                // products, but it moves to a product only when every ticket that uses it is of that product
                if (supportSubCatagory.ProductId != null && supportSubCatagory.ProductId != existing.ProductId
                    && await Db.Issues.AnyAsync(issue => issue.SupportSubCatagoryId == existing.SupportSubCatagoryId && (issue.ProductId == null || issue.ProductId != supportSubCatagory.ProductId)))
                {
                    ModelState.AddModelError("ProductId", "Tickets of other products use this entry, so it cannot move to this product. It can be offered for all products.");
                }

                if (ModelState.IsValid && await Db.SupportSubCatagories.AnyAsync(row => row.SubCatagory == supportSubCatagory.SubCatagory && row.ProductId == supportSubCatagory.ProductId && row.SupportSubCatagoryId != existing.SupportSubCatagoryId))
                {
                    ModelState.AddModelError(nameof(SupportSubCatagory.SubCatagory), TakenMessage("support sub-category", supportSubCatagory.ProductId));
                }

                if (!ModelState.IsValid)
                {
                    await LoadProductChoicesAsync(back);
                    return View("EditSupportSubCatagory", supportSubCatagory);
                }

                existing.SubCatagory = supportSubCatagory.SubCatagory;
                existing.ProductId = supportSubCatagory.ProductId;
                await Db.SaveChangesAsync();
                return BackToSupportList("SupportSubCatagoryList", existing.ProductId, back);
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

        // ---- affected sections ---------------------------------------------------------------------------

        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> AffectedSectionList()
        {
            try
            {
                return View(await Db.AffectedSectionss.Include(item => item.Product).OrderBy(item => item.AffectedSectionId).ToListAsync());
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
                var item = await Db.AffectedSectionss.Include(row => row.Product).FirstOrDefaultAsync(row => row.AffectedSectionId == id);
                if (item == null)
                {
                    return NotFound();
                }

                return View(item);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <param name="productId">The product the new entry is for (the link on the page of a product passes it).</param>
        /// <param name="back">"product": return to the page of the product afterwards instead of the list.</param>
        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> CreateAffectedSection(int? productId = null, string? back = null)
        {
            try
            {
                await LoadProductChoicesAsync(back);
                return View(new AffectedSection { ProductId = await ExistingProductAsync(productId) });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAffectedSection([Bind("AffectedSectionId,ASection,ProductId")] AffectedSection affectedSection, string? back = null)
        {
            try
            {
                affectedSection.AffectedSectionId = 0; // identity column: the database assigns the id
                affectedSection.ASection = CleanName(affectedSection.ASection);
                Revalidate(affectedSection, "ASection"); // the rules are checked on the tidy name
                await CheckProductAsync(affectedSection.ProductId);
                if (ModelState.IsValid && await Db.AffectedSectionss.AnyAsync(row => row.ASection == affectedSection.ASection && row.ProductId == affectedSection.ProductId))
                {
                    ModelState.AddModelError(nameof(AffectedSection.ASection), TakenMessage("affected section", affectedSection.ProductId));
                }

                if (!ModelState.IsValid)
                {
                    await LoadProductChoicesAsync(back);
                    return View(affectedSection);
                }

                await Db.AddAsync(affectedSection);
                await Db.SaveChangesAsync();
                return BackToSupportList("AffectedSectionList", affectedSection.ProductId, back);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> EditAffectedSection(int id, string? back = null)
        {
            try
            {
                var item = await Db.AffectedSectionss.FirstOrDefaultAsync(row => row.AffectedSectionId == id);
                if (item == null)
                {
                    return NotFound();
                }

                await LoadProductChoicesAsync(back);
                return View(item);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        public async Task<IActionResult> UpdateAffectedSection([Bind("AffectedSectionId,ASection,ProductId")] AffectedSection affectedSection, string? back = null)
        {
            try
            {
                var existing = await Db.AffectedSectionss.FirstOrDefaultAsync(row => row.AffectedSectionId == affectedSection.AffectedSectionId);
                if (existing == null)
                {
                    return NotFound();
                }

                affectedSection.ASection = CleanName(affectedSection.ASection);
                Revalidate(affectedSection, "ASection"); // the rules are checked on the tidy name
                await CheckProductAsync(affectedSection.ProductId);
                // tickets of one product must not end up with an entry of another: an entry in use can be opened to all
                // products, but it moves to a product only when every ticket that uses it is of that product
                if (affectedSection.ProductId != null && affectedSection.ProductId != existing.ProductId
                    && await Db.Issues.AnyAsync(issue => issue.AffectedSectionId == existing.AffectedSectionId && (issue.ProductId == null || issue.ProductId != affectedSection.ProductId)))
                {
                    ModelState.AddModelError("ProductId", "Tickets of other products use this entry, so it cannot move to this product. It can be offered for all products.");
                }

                if (ModelState.IsValid && await Db.AffectedSectionss.AnyAsync(row => row.ASection == affectedSection.ASection && row.ProductId == affectedSection.ProductId && row.AffectedSectionId != existing.AffectedSectionId))
                {
                    ModelState.AddModelError(nameof(AffectedSection.ASection), TakenMessage("affected section", affectedSection.ProductId));
                }

                if (!ModelState.IsValid)
                {
                    await LoadProductChoicesAsync(back);
                    return View("EditAffectedSection", affectedSection);
                }

                existing.ASection = affectedSection.ASection;
                existing.ProductId = affectedSection.ProductId;
                await Db.SaveChangesAsync();
                return BackToSupportList("AffectedSectionList", existing.ProductId, back);
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
