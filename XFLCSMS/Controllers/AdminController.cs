using MailKit.Search;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using System.Security.Cryptography;
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
    public class AdminController : CsmsController
    {
        private readonly DataContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        protected override string SessionKey => "AdminData";

        public AdminController(DataContext context, IWebHostEnvironment webHostEnvironment, TicketService tickets)
            : base(context, tickets)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }
        public IActionResult Index()
        {
            return RedirectToAction("Dashbord");
        }

        public async Task<IActionResult> Dashbord()
        {
            try
            {
                return View(await BuildDashboardAsync(includeHouses: true));
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        public IActionResult AdminView(int page, int rowperpage, string? searchString = null, string?
            sortField = null, bool sortAscending = true)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                if (page <= 0) { page = 1; }

                ViewBag.CurrentSortField = sortField;
                ViewBag.CurrentSortAscending = sortAscending;
                List<IssueTable> TicketList = _context.Issues.OrderByDescending(i => i.IssueId).ToList();

                if (!string.IsNullOrEmpty(searchString))
                {


                    TicketList = TicketList.Where(e =>
                                            e.TNumber.ToLower().Contains(searchString.ToLower()) ||
                                            e.ITitle?.ToLower().Contains(searchString.ToLower()) == true ||
                                            e.Priority.ToLower().Contains(searchString.ToLower()) || // Convert Priority to string for searching
                                            e.IStatus?.ToLower().Contains(searchString.ToLower()) == true ||
                                            e.AssignOn?.ToString().ToLower().Contains(searchString.ToLower()) == true ||
                                            e.TDate.ToString().ToLower().Contains(searchString.ToLower()) ||
                                            e.AssignBy?.ToLower().Contains(searchString.ToLower()) == true).ToList();

                }

                switch (sortField)
                {
                    case "TNumber":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.TNumber).ToList() :
                            TicketList.OrderByDescending(item => item.TNumber).ToList();
                        break;
                    case "Tickets":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.ITitle).ToList() :
                            TicketList.OrderByDescending(item => item.ITitle).ToList();
                        break;
                    case "Approval Status":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.AssignBy).ToList() :
                            TicketList.OrderByDescending(item => item.AssignBy).ToList();
                        break;
                    case "Priority":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.Priority).ToList() :
                            TicketList.OrderByDescending(item => item.Priority).ToList();
                        break;
                    case "Status":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.IStatus).ToList() :
                            TicketList.OrderByDescending(item => item.IStatus).ToList();
                        break;

                    // Add cases for other fields as needed
                    default:
                        // Default sorting if no valid sort field provided
                        TicketList = TicketList.OrderByDescending(item => item.IssueId).ToList();
                        break;
                }

                int tot_records = TicketList.Count;
                int pagesize = rowperpage > 0 ? rowperpage : 10;
                int number_of_button = 4;


                Pager P = new Pager(tot_records, page, pagesize, number_of_button, searchString);
                ViewBag.pager = P;
                int skip_records = (Math.Max(P.CurrentPage, 1) - 1) * pagesize;
                int take_records = pagesize;
                List<IssueTable> IssueList = TicketList.Skip(skip_records).Take(take_records).ToList();
                return View(IssueList);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        public async Task<IActionResult> TicketView(int? id)
        {
            var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
            User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
            ViewBag.Profile = LogSesson;
            try
            {
                var issueWithAttachments = _context.Issues
                    .Include(i => i.attachment)  // Include attachments in the query
                    .FirstOrDefault(i => i.IssueId == id);

                if (issueWithAttachments == null || !CanAccessIssue(issueWithAttachments))
                {
                    return NotFound();
                }

                var makerView = ToMakerView(issueWithAttachments, includeEngineers: false);

                return View(makerView);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        public async Task<IActionResult> EditTicket(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var issueWithAttachments = _context.Issues
                    .Include(i => i.attachment)  // Include attachments in the query
                    .FirstOrDefault(i => i.IssueId == id);

                if (issueWithAttachments == null || !CanAccessIssue(issueWithAttachments))
                {
                    return NotFound();
                }


                var EditView = ToMakerView(issueWithAttachments, includeEngineers: true);
                return View(EditView);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTicketttt(MakerView makerView, List<IFormFile> files)
        {
            try
            {
                var editor = CurrentUser!;
                var issue = await _context.Issues.FirstOrDefaultAsync(a => a.IssueId == makerView.IssueId);
                if (issue == null || !CanAccessIssue(issue))
                {
                    return NotFound("The ticket was not found.");
                }

                Tickets.ApplyStaffEdit(issue, makerView, editor, canApprove: editor.UType);
                await _context.SaveChangesAsync();

                var rejected = await Tickets.SaveAttachmentsAsync(issue.IssueId, files);
                if (rejected.Count > 0)
                {
                    TempData["ErrorMessage"] = "The ticket was saved, but these files were not attached (file type not allowed): "
                        + string.Join(", ", rejected);
                }
                else
                {
                    TempData["SuccessMessage"] = "Ticket " + issue.TNumber + " was saved.";
                }

                return RedirectToAction("TicketView", new { id = issue.IssueId });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
        public async Task<IActionResult> ClosedTicketList(int page, int rowperpage, string? searchString = null, string?
            sortField = null, bool sortAscending = true)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                if (page <= 0) { page = 1; }

                ViewBag.CurrentSortField = sortField;
                ViewBag.CurrentSortAscending = sortAscending;
                List<IssueTable> TicketList = _context.Issues.OrderByDescending(i => i.IssueId).Where(i => i.IStatus == "Close").ToList();

                if (!string.IsNullOrEmpty(searchString))
                {


                    TicketList = TicketList.Where(e =>
                                            e.TNumber.ToLower().Contains(searchString.ToLower()) ||
                                            e.ITitle?.ToLower().Contains(searchString.ToLower()) == true ||
                                            e.Priority.ToLower().Contains(searchString.ToLower()) || // Convert Priority to string for searching
                                            e.IStatus?.ToLower().Contains(searchString.ToLower()) == true ||
                                            e.AssignOn?.ToString().ToLower().Contains(searchString.ToLower()) == true ||
                                            e.TDate.ToString().ToLower().Contains(searchString.ToLower()) ||
                                            e.AssignBy?.ToLower().Contains(searchString.ToLower()) == true).ToList();

                }

                switch (sortField)
                {
                    case "TNumber":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.TNumber).ToList() :
                            TicketList.OrderByDescending(item => item.TNumber).ToList();
                        break;
                    case "Tickets":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.ITitle).ToList() :
                            TicketList.OrderByDescending(item => item.ITitle).ToList();
                        break;
                    case "Approval Status":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.AssignBy).ToList() :
                            TicketList.OrderByDescending(item => item.AssignBy).ToList();
                        break;
                    case "Priority":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.Priority).ToList() :
                            TicketList.OrderByDescending(item => item.Priority).ToList();
                        break;
                    case "Status":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.IStatus).ToList() :
                            TicketList.OrderByDescending(item => item.IStatus).ToList();
                        break;

                    // Add cases for other fields as needed
                    default:
                        // Default sorting if no valid sort field provided
                        TicketList = TicketList.OrderByDescending(item => item.IssueId).ToList();
                        break;
                }

                int tot_records = TicketList.Count;
                int pagesize = rowperpage > 0 ? rowperpage : 10;
                int number_of_button = 4;


                Pager P = new Pager(tot_records, page, pagesize, number_of_button, searchString);
                ViewBag.pager = P;
                int skip_records = (Math.Max(P.CurrentPage, 1) - 1) * pagesize;
                int take_records = pagesize;
                List<IssueTable> IssueList = TicketList.Skip(skip_records).Take(take_records).ToList();
                return View(IssueList);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }

        }

        public async Task<IActionResult> UnassignedTicketList(int page, int rowperpage, string? searchString = null, string?
            sortField = null, bool sortAscending = true)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                if (page <= 0) { page = 1; }

                ViewBag.CurrentSortField = sortField;
                ViewBag.CurrentSortAscending = sortAscending;
                List<IssueTable> TicketList = _context.Issues.OrderByDescending(i => i.IssueId).Where(i => i.AssignOn == null && i.AssignBy == null && i.IStatus != "Close").ToList();
                if (!string.IsNullOrEmpty(searchString))
                {


                    TicketList = TicketList.Where(e =>
                                            e.TNumber.ToLower().Contains(searchString.ToLower()) ||
                                            e.ITitle?.ToLower().Contains(searchString.ToLower()) == true ||
                                            e.Priority.ToLower().Contains(searchString.ToLower()) || // Convert Priority to string for searching
                                            e.IStatus?.ToLower().Contains(searchString.ToLower()) == true ||
                                            e.AssignOn?.ToString().ToLower().Contains(searchString.ToLower()) == true ||
                                            e.TDate.ToString().ToLower().Contains(searchString.ToLower()) ||
                                            e.AssignBy?.ToLower().Contains(searchString.ToLower()) == true).ToList();

                }

                switch (sortField)
                {
                    case "TNumber":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.TNumber).ToList() :
                            TicketList.OrderByDescending(item => item.TNumber).ToList();
                        break;
                    case "Tickets":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.ITitle).ToList() :
                            TicketList.OrderByDescending(item => item.ITitle).ToList();
                        break;
                    case "Approval Status":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.AssignBy).ToList() :
                            TicketList.OrderByDescending(item => item.AssignBy).ToList();
                        break;
                    case "Priority":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.Priority).ToList() :
                            TicketList.OrderByDescending(item => item.Priority).ToList();
                        break;
                    case "Status":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.IStatus).ToList() :
                            TicketList.OrderByDescending(item => item.IStatus).ToList();
                        break;

                    // Add cases for other fields as needed
                    default:
                        // Default sorting if no valid sort field provided
                        TicketList = TicketList.OrderByDescending(item => item.IssueId).ToList();
                        break;
                }

                int tot_records = TicketList.Count;
                int pagesize = rowperpage > 0 ? rowperpage : 10;
                int number_of_button = 4;


                Pager P = new Pager(tot_records, page, pagesize, number_of_button, searchString);
                ViewBag.pager = P;
                int skip_records = (Math.Max(P.CurrentPage, 1) - 1) * pagesize;
                int take_records = pagesize;
                List<IssueTable> IssueList = TicketList.Skip(skip_records).Take(take_records).ToList();
                return View(IssueList);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }

        }

        public IActionResult CreateBrocarage(int page, int rowperpage, string? searchString = null, string?
            sortField = null, bool sortAscending = true)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                
                return View();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

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

                await _context.AddAsync(brokerage);
                await _context.SaveChangesAsync();
                return RedirectToAction("BrocarageHouseList", "Admin");
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

            if (_context.Brokerages.Any(b => b.BrokerageId != brokerage.BrokerageId && b.BrokerageHouseName == brokerage.BrokerageHouseName))
            {
                ModelState.AddModelError(nameof(Brokerage.BrokerageHouseName), "A brokerage house with this name already exists.");
            }

            if (_context.Brokerages.Any(b => b.BrokerageId != brokerage.BrokerageId && b.BrokerageHouseAcronym == brokerage.BrokerageHouseAcronym))
            {
                ModelState.AddModelError(nameof(Brokerage.BrokerageHouseAcronym), "This acronym is already used by another brokerage house.");
            }
        }


        [HttpDelete]
        public async Task<IActionResult> DeleteTicket(int id)
        {
            try
            {
                var issue = await _context.Issues.Include(i => i.attachment).FirstOrDefaultAsync(i => i.IssueId == id);
                if (issue == null)
                {
                    return NotFound("The ticket was not found.");
                }

                // remove the uploaded files too, not only the database rows
                foreach (var attachment in issue.attachment?.ToList() ?? new List<Attachment>())
                {
                    await Tickets.DeleteAttachmentAsync(attachment);
                }

                _context.Issues.Remove(issue);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        // The user list's Delete button used to call DeleteTicket with the USER id and deleted an unrelated ticket.
        [HttpDelete]
        public async Task<IActionResult> DeleteUser(int id)
        {
            try
            {
                if (id == CurrentUser!.Id)
                {
                    return Conflict("You cannot delete the account you are signed in with.");
                }

                var user = await _context.Users.FindAsync(id);
                if (user == null)
                {
                    return NotFound("The user was not found.");
                }

                // Deleting the user would cascade and delete every ticket he raised.
                if (await _context.Issues.AnyAsync(i => i.UserId == id))
                {
                    return Conflict("This user has raised tickets, so the account cannot be deleted. Disable the user instead (Edit > User Status).");
                }

                _context.Users.Remove(user);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        public async Task<IActionResult> UserList()
        {
            try
            {

                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var users = _context.Users.ToList();
                return View(users);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }


        public async Task<IActionResult> UserView(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var user = await _context.Users.FirstOrDefaultAsync(item => item.Id == id);

                if (user == null)
                {
                    return NotFound(); // Or handle the case where the user is not found
                }

                var userView = new UserView
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    Department = user.Department ?? string.Empty,
                    Email = user.Email,
                    PhonNumber = user.PhonNumber,
                    Designation = user.Designation,
                    BrokerageHouseName = GetBrocarageHouseName(user.BrokerageHouseName),
                    Branch = GetBranchName(user.Branch),
                    EmployeeId = user.EmployeeId,
                    UserName = user.UserName,
                    UCatagory = user.UCatagory,
                    UType = user.UType,
                    UStatus = user.UStatus
                };


                return View(userView);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        public async Task<IActionResult> EditUser(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;

                var user = await _context.Users.FirstOrDefaultAsync(item => item.Id == id);

                if (user == null)
                {
                    return NotFound(); // Or handle the case where the user is not found
                }

                var userView = new UserView
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    Department = user.Department ?? string.Empty,
                    Email = user.Email,
                    PhonNumber = user.PhonNumber,
                    Designation = user.Designation,
                    BrokerageHouseName = GetBrocarageHouseName(user.BrokerageHouseName),
                    Branch = GetBranchName(user.Branch),
                    EmployeeId = user.EmployeeId,
                    UserName = user.UserName,
                    UCatagory = user.UCatagory,
                    UType = user.UType,
                    UStatus = user.UStatus
                };
                return View(userView);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }


            
        }

        [HttpPost]
        public async Task<IActionResult> UpdateUser( UserView userView)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                var user = await _context.Users.FirstOrDefaultAsync(a => a.Id == userView.Id);

                if (user != null)
                {
                    user.UType = userView.UType;
                    user.UStatus = userView.UStatus;
                    user.UCatagory = userView.UCatagory;
                    user.Department = userView.Department;
                    _context.Update(user);
                    await _context.SaveChangesAsync();
                    return RedirectToAction("UserList");
                }
                return RedirectToAction("UserList");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        public async Task<IActionResult> BrocarageHouseList()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var Brocareges = await _context.Brokerages.ToListAsync();
                return View(Brocareges);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }

        }
        public async Task<IActionResult> ViewBrocarageHouse(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var brocarage = await _context.Brokerages.FirstOrDefaultAsync(item => item.BrokerageId == id);

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

        public async Task<IActionResult> EditBrocarage(int id)
        {
            try
            {

                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var brocarage = await _context.Brokerages.FirstOrDefaultAsync(item => item.BrokerageId == id);

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
        [HttpPost]
        public async Task<IActionResult> UpdateBrocarage(Brokerage brokeragesss)
        {
            try
            {
                var brocarage = await _context.Brokerages.FirstOrDefaultAsync(item => item.BrokerageId == brokeragesss.BrokerageId);
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
                await _context.SaveChangesAsync();
                return RedirectToAction("BrocarageHouseList", "Admin");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteBrocarage(int id)
        {
            try
            {
                var house = await _context.Brokerages.FindAsync(id);
                if (house == null)
                {
                    return NotFound("The brokerage house was not found.");
                }

                // Deleting a house in use either fails on the foreign keys or silently wipes all of its tickets.
                if (await _context.Branchhs.AnyAsync(b => b.BrokerageId == id)
                    || await _context.Users.AnyAsync(u => u.BrokerageHouseName == id)
                    || await _context.Issues.AnyAsync(i => i.BrokerageId == id))
                {
                    return Conflict("This brokerage house still has branches, users or tickets, so it cannot be deleted.");
                }

                _context.Brokerages.Remove(house);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }


        public async Task<IActionResult> BranchList()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var Branch = await _context.Branchhs.ToListAsync();
                List<BranchView> branches = new List<BranchView>();
                foreach (var branch in Branch)
                {
                    BranchView b = new BranchView();
                    b.BranchId = branch.BranchId;
                    b.BranchName = branch.BranchName;
                    b.BrokerageHouseName = GetBrocarageHouseName(branch.BrokerageId);
                    branches.Add(b);
                }

                return View(branches);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        public IActionResult CreateBranch()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var Brocarage = _context.Brokerages.ToList();

                BranchView BB = new BranchView();
                BB.brocarage = Brocarage;

                return View(BB);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }

        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBranch(BranchView branchView)
        {
            try
            {
                if (!await _context.Brokerages.AnyAsync(b => b.BrokerageId == branchView.BrokerageId))
                {
                    ModelState.AddModelError(nameof(BranchView.BrokerageId), "Please select a brokerage house.");
                }

                if (!ModelState.IsValid)
                {
                    branchView.brocarage = await _context.Brokerages.ToListAsync(); // the drop-down needs its options again
                    return View(branchView);
                }

                Branchh branchh = new Branchh();
                branchh.BranchName = branchView.BranchName.Trim();
                branchh.BrokerageId = branchView.BrokerageId;
                await _context.Branchhs.AddAsync(branchh);
                await _context.SaveChangesAsync();
                return RedirectToAction("BranchList", "Admin");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        public async Task<IActionResult> ViewBranch(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var branch = await _context.Branchhs.FirstOrDefaultAsync(item => item.BranchId == id);

                if (branch == null)
                {
                    return NotFound();
                }

                BranchView b = new BranchView();

                b.BranchId = branch.BranchId;
                b.BranchName = branch.BranchName;
                b.BrokerageHouseName = GetBrocarageHouseName(branch.BrokerageId);

                return View(b);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }


        public async Task<IActionResult> EditBranch(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var branch = await _context.Branchhs.FirstOrDefaultAsync(item => item.BranchId == id);
                var Brocarage = await _context.Brokerages.ToListAsync();

                if (branch == null)
                {
                    return NotFound();
                }

                BranchView b = new BranchView();

                b.BranchId = branch.BranchId;
                b.BranchName = branch.BranchName;
                b.BrokerageHouseName = GetBrocarageHouseName(branch.BrokerageId);
                b.brocarage = Brocarage;

                return View(b);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }


        }
        [HttpPost]
        public async Task<IActionResult> UpdateBranch(BranchView branchView)
        {
            try
            {
                var branch = await _context.Branchhs.FirstOrDefaultAsync(item => item.BranchId == branchView.BranchId);
                if (branch == null)
                {
                    return NotFound();
                }

                if (!ModelState.IsValid)
                {
                    branchView.BrokerageHouseName = GetBrocarageHouseName(branch.BrokerageId) ?? string.Empty;
                    return View("EditBranch", branchView);
                }

                branch.BranchName = branchView.BranchName.Trim();
                await _context.SaveChangesAsync();
                return RedirectToAction("BranchList", "Admin");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteBranch(int id)
        {
            try
            {
                var branch = await _context.Branchhs.FirstOrDefaultAsync(i => i.BranchId == id);
                if (branch == null)
                {
                    return NotFound("The branch was not found.");
                }

                if (await _context.Users.AnyAsync(u => u.Branch == id))
                {
                    return Conflict("Users are registered under this branch, so it cannot be deleted.");
                }

                _context.Branchhs.Remove(branch);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }


        public async Task<IActionResult> SupportTypeList()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var supportType = await _context.SupportTypes.ToListAsync();
                return View(supportType);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
        public async Task<IActionResult> ViewSupportType(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var brocarage = await _context.SupportTypes.FirstOrDefaultAsync(item => item.SupportTypeId == id);

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

        public IActionResult CreateSupportType()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                return View();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

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

                await _context.AddAsync(supportType);
                await _context.SaveChangesAsync();
                return RedirectToAction("SupportTypeList", "Admin");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }


        public async Task<IActionResult> EditSupportType(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var brocarage = await _context.SupportTypes.FirstOrDefaultAsync(item => item.SupportTypeId == id);

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
        [HttpPost]
        public async Task<IActionResult> UpdateSupportType(SupportType brokeragesss)
        {
            try
            {
                var existing = await _context.SupportTypes.FirstOrDefaultAsync(item => item.SupportTypeId == brokeragesss.SupportTypeId);
                if (existing == null)
                {
                    return NotFound();
                }

                if (!ModelState.IsValid)
                {
                    return View("EditSupportType", brokeragesss);
                }

                existing.SType = brokeragesss.SType;
                await _context.SaveChangesAsync();
                return RedirectToAction("SupportTypeList", "Admin");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteSupportType(int id)
        {
            try
            {
                var item = await _context.SupportTypes.FindAsync(id);
                if (item == null)
                {
                    return NotFound("The support type was not found.");
                }

                // Tickets point at this row; deleting it would fail on the foreign key.
                if (await _context.Issues.AnyAsync(i => i.SupportTypeId == id))
                {
                    return Conflict("This support type is used by existing tickets, so it cannot be deleted.");
                }

                _context.SupportTypes.Remove(item);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }


        public async Task<IActionResult> SupportCatagoryList()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var supportCatagory = await _context.SupportCatagories.ToListAsync();
                return View(supportCatagory);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
        public async Task<IActionResult> ViewSupportCatagory(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var supportCatagory = await _context.SupportCatagories.FirstOrDefaultAsync(item => item.SupportCatagoryId == id);

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

        public IActionResult CreateSupportCatagory()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                return View();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

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

                await _context.AddAsync(supportCatagory);
                await _context.SaveChangesAsync();
                return RedirectToAction("SupportCatagoryList", "Admin");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        public async Task<IActionResult> EditSupportCatagory(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var supportCatagory = await _context.SupportCatagories.FirstOrDefaultAsync(item => item.SupportCatagoryId == id);

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
        [HttpPost]
        public async Task<IActionResult> UpdateSupportCatagory(SupportCatagory supportCatagory)
        {
            try
            {
                var existing = await _context.SupportCatagories.FirstOrDefaultAsync(item => item.SupportCatagoryId == supportCatagory.SupportCatagoryId);
                if (existing == null)
                {
                    return NotFound();
                }

                if (!ModelState.IsValid)
                {
                    return View("EditSupportCatagory", supportCatagory);
                }

                existing.SCatagory = supportCatagory.SCatagory;
                await _context.SaveChangesAsync();
                return RedirectToAction("SupportCatagoryList", "Admin");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteSupportCatagory(int id)
        {
            try
            {
                var item = await _context.SupportCatagories.FindAsync(id);
                if (item == null)
                {
                    return NotFound("The support category was not found.");
                }

                // Tickets point at this row; deleting it would fail on the foreign key.
                if (await _context.Issues.AnyAsync(i => i.SupportCatagoryId == id))
                {
                    return Conflict("This support category is used by existing tickets, so it cannot be deleted.");
                }

                _context.SupportCatagories.Remove(item);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        public async Task<IActionResult> SupportSubCatagoryList()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var supportSubCatagory = await _context.SupportSubCatagories.ToListAsync();
                return View(supportSubCatagory);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
        public async Task<IActionResult> ViewSupportSubCatagory(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var supportSubCatagory = await _context.SupportSubCatagories.FirstOrDefaultAsync(item => item.SupportSubCatagoryId == id);

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
        public IActionResult CreateSupportSubCatagory()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                return View();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

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

                await _context.AddAsync(supportSubCatagory);
                await _context.SaveChangesAsync();
                return RedirectToAction("SupportSubCatagoryList", "Admin");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        public async Task<IActionResult> EditSupportSubCatagory(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var supportSubCatagory = await _context.SupportSubCatagories.FirstOrDefaultAsync(item => item.SupportSubCatagoryId == id);

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
        [HttpPost]
        public async Task<IActionResult> UpdateSupportSubCatagory(SupportSubCatagory supportSubCatagory)
        {
            try
            {
                var existing = await _context.SupportSubCatagories.FirstOrDefaultAsync(item => item.SupportSubCatagoryId == supportSubCatagory.SupportSubCatagoryId);
                if (existing == null)
                {
                    return NotFound();
                }

                if (!ModelState.IsValid)
                {
                    return View("EditSupportSubCatagory", supportSubCatagory);
                }

                existing.SubCatagory = supportSubCatagory.SubCatagory;
                await _context.SaveChangesAsync();
                return RedirectToAction("SupportSubCatagoryList", "Admin");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteSupportSubCatagory(int id)
        {
            try
            {
                var item = await _context.SupportSubCatagories.FindAsync(id);
                if (item == null)
                {
                    return NotFound("The support sub-category was not found.");
                }

                // Tickets point at this row; deleting it would fail on the foreign key.
                if (await _context.Issues.AnyAsync(i => i.SupportSubCatagoryId == id))
                {
                    return Conflict("This support sub-category is used by existing tickets, so it cannot be deleted.");
                }

                _context.SupportSubCatagories.Remove(item);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }



        public async Task<IActionResult> AffectedSectionList()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var affectedSectios = await _context.AffectedSectionss.ToListAsync();
                return View(affectedSectios);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
        public async Task<IActionResult> ViewAffectedSection(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var affectedsection = await _context.AffectedSectionss.FirstOrDefaultAsync(item => item.AffectedSectionId == id);

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
        public IActionResult CreateAffectedSection()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                return View();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

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

                await _context.AddAsync(affectedSection);
                await _context.SaveChangesAsync();
                return RedirectToAction("AffectedSectionList", "Admin");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        public async Task<IActionResult> EditAffectedSection(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var affectedSection = await _context.AffectedSectionss.FirstOrDefaultAsync(item => item.AffectedSectionId == id);

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
        [HttpPost]
        public async Task<IActionResult> UpdateAffectedSection(AffectedSection affectedSection)
        {
            try
            {
                var existing = await _context.AffectedSectionss.FirstOrDefaultAsync(item => item.AffectedSectionId == affectedSection.AffectedSectionId);
                if (existing == null)
                {
                    return NotFound();
                }

                if (!ModelState.IsValid)
                {
                    return View("EditAffectedSection", affectedSection);
                }

                existing.ASection = affectedSection.ASection;
                await _context.SaveChangesAsync();
                return RedirectToAction("AffectedSectionList", "Admin");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteAffectedSection(int id)
        {
            try
            {
                var item = await _context.AffectedSectionss.FindAsync(id);
                if (item == null)
                {
                    return NotFound("The affected section was not found.");
                }

                // Tickets point at this row; deleting it would fail on the foreign key.
                if (await _context.Issues.AnyAsync(i => i.AffectedSectionId == id))
                {
                    return Conflict("This affected section is used by existing tickets, so it cannot be deleted.");
                }

                _context.AffectedSectionss.Remove(item);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Reports()
        {
            var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
            User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
            ViewBag.Profile = LogSesson;

            var supportEngineer = _context.Users.Where(i => i.Department == "Support Engineer").Select(user=>user.FullName).ToList();
            var BrocarageHouse = _context.Brokerages.ToList();
     

            var reportview = new ReportView
            {
                EmployeeNames = supportEngineer,
                brocarages = BrocarageHouse,
                

            };

            return View(reportview);
        }


        [HttpGet]
        public async Task<IActionResult> Search(ReportView reportView)
        {
            // the report page can be opened/searched with an empty filter
            reportView.search ??= new XFLCSMS.Models.Admin.Search();


            if(reportView.search.ToDate>=DateTime.Now)
            {
                reportView.search.ToDate=DateTime.Now;
            }

            var searchResults = await _context.Issues.ToListAsync();

            if (reportView.search.BrokerageId.HasValue)
                searchResults = searchResults.Where(x => x.BrokerageId == reportView.search.BrokerageId).ToList();
            if (!String.IsNullOrEmpty(reportView.search.Priority))
                searchResults = searchResults.Where(x => x.Priority.Contains(reportView.search.Priority)).ToList();
            if (!String.IsNullOrEmpty(reportView.search.AStatus))
                searchResults = searchResults.Where(x => x.IStatus != null && x.IStatus.Contains(reportView.search.AStatus)).ToList();
            if (reportView.search.EmployeeName != null)
                searchResults = searchResults.Where(x => x.ClosedBy == reportView.search.EmployeeName).ToList();
            // both ends of the range are inclusive, and either end may be left empty
            if (reportView.search.FromDate != null)
                searchResults = searchResults.Where(x => x.TDate.Date >= reportView.search.FromDate.Value.Date).ToList();
            if (reportView.search.ToDate != null)
                searchResults = searchResults.Where(x => x.TDate.Date <= reportView.search.ToDate.Value.Date).ToList();

            string Brocaragename = (reportView.search.BrokerageId > 0) ? GetBrocarageHouseName(reportView.search.BrokerageId) : "All";
            string EmployeeNamee = (reportView.search.EmployeeName !=null) ? reportView.search.EmployeeName : "All";



            int TotalTickett = searchResults.Count();
            int TotalOpenTickett = searchResults.Where(x => x.IStatus == "Open").Count();
            int TotalCloseTickett = searchResults.Where(x => x.IStatus == "Close").Count();
            int TotalInquee = TotalTickett - TotalOpenTickett - TotalCloseTickett;


            HeaderInfo SectionInfo = new HeaderInfo
            {
                BrokerageHouseName = Brocaragename,
                EmployeeName = EmployeeNamee,
                TotalTicket = TotalTickett,
                TotalOpenTicket = TotalOpenTickett,
                TotalCloseTicket = TotalCloseTickett,
                TotalInque = TotalInquee,
                ReportName = "Admin"


            };

            // Populate the Issues property of the ReportView model with search results
            var reportViewWithSearchResults = new ReportView
            {
                Issues = searchResults,
                HeaderInfo = SectionInfo,

            };

            // Return the partial view with the populated reportView model
            return PartialView("_SearchResults", reportViewWithSearchResults);
        }


        [HttpGet]
        public async Task<IActionResult> ViewTodo(int page, int rowperpage, string? searchString = null, string? sortField = null, bool sortAscending = true)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                if (page <= 0) { page = 1; }

                ViewBag.CurrentSortField = sortField;
                ViewBag.CurrentSortAscending = sortAscending;
                // Fetch all Todo items from the database
                //var todos = await _context.Todos.Where(item => item.UserId == LogSesson.Id && item.Status == "In progress").ToListAsync();
                var todos = await _context.Todos
                            .Where(item => item.UserId == LogSesson.Id && item.Status == "In progress")
                            .OrderByDescending(item => item.Id)
                            .ToListAsync();
                //List<Todo> todoss = todos;
                if (!string.IsNullOrEmpty(searchString))
                {
                    todos = todos.Where(todo =>
                                            todo.Todoname.ToLower().Contains(searchString.ToLower()) ||
                                            todo.Status?.ToLower().Contains(searchString.ToLower()) == true ||
                                            todo.CreatedOn.ToString().ToLower().Contains(searchString.ToLower())
                                     ).ToList();
                }

                switch (sortField)
                {
                    case "Task":
                        todos = sortAscending ? todos.OrderBy(item => item.Todoname).ToList() :
                            todos.OrderByDescending(item => item.Todoname).ToList();
                        break;
                    case "Status":
                        todos = sortAscending ? todos.OrderBy(item => item.Status).ToList() :
                            todos.OrderByDescending(item => item.Status).ToList();
                        break;
                    case "CreatedOn":
                        todos = sortAscending ? todos.OrderBy(item => item.CreatedOn).ToList() :
                            todos.OrderByDescending(item => item.CreatedOn).ToList();
                        break;
                    default:
                        // Default sorting if no valid sort field provided (sort by Id by default)
                        todos = todos.OrderByDescending(item => item.Id).ToList();
                        break;
                }


                int tot_records = todos.Count;
                int pagesize = rowperpage > 0 ? rowperpage : 10;
                int number_of_button = 4;

                Pager P = new Pager(tot_records, page, pagesize, number_of_button, searchString);
                ViewBag.pager = P;
                int skip_records = (Math.Max(P.CurrentPage, 1) - 1) * pagesize;
                int take_records = pagesize;
                List<Todo> todoss = todos.Skip(skip_records).Take(take_records).ToList();



                TodoviewModel todoviewModel = new TodoviewModel()
                {
                    Todos = todoss,
                    Todo = null,

                };

                // Return the view with the list of Todo items
                return View(todoviewModel);

            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }

        }

        [HttpPost]
        public async Task<IActionResult> AddTodo(TodoviewModel newTodo)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                if (string.IsNullOrWhiteSpace(newTodo?.Todo?.Todoname))
                {
                    TempData["ErrorMessage"] = "Please enter a to-do before adding it.";
                    return RedirectToAction("ViewTodo");
                }

                Todo todo = new Todo()
                {
                    CreatedOn = DateTime.Now,
                    Todoname = newTodo.Todo.Todoname,
                    Status = "In progress",
                    UserId = LogSesson.Id,
                    BrokerageId = LogSesson.BrokerageHouseName,
                };

                await _context.Todos.AddAsync(todo);
                await _context.SaveChangesAsync();
                return RedirectToAction("ViewTodo");

            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }

        }

        [HttpGet]
        public async Task<IActionResult> GetTodo(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var todo = await _context.Todos.FindAsync(id);
                if (todo == null)
                {
                    return NotFound();
                }
                var statusOptions = new List<SelectListItem>
                {
                    new SelectListItem("In progress", "In progress"),
                    new SelectListItem("Done", "Done"),
                    new SelectListItem("Canceled", "Canceled")
                };

                ViewBag.StatusOptions = statusOptions;

                return View(todo);

            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }


        }

        [HttpPost]
        public async Task<IActionResult> Updatetodo(Todo model)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);

                var existingTodo = await _context.Todos.FindAsync(model.Id);
                if (existingTodo == null)
                {
                    return NotFound();
                }

                if (!string.IsNullOrWhiteSpace(model.Todoname))
                {
                    existingTodo.Todoname = model.Todoname;
                }

                if (!string.IsNullOrWhiteSpace(model.Status))
                {
                    existingTodo.Status = model.Status;
                }

                _context.Todos.Update(existingTodo);
                await _context.SaveChangesAsync();

                return RedirectToAction("ViewTodo");

            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }


        }

        [HttpGet]
        public async Task<IActionResult> AllTodo(int page, int rowperpage, string? searchString = null, string? sortField = null, bool sortAscending = true)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                if (page <= 0) { page = 1; }

                ViewBag.CurrentSortField = sortField;
                ViewBag.CurrentSortAscending = sortAscending;
                // Fetch all Todo items from the database
                var todos = await _context.Todos.Where(item => item.UserId == LogSesson.Id).ToListAsync();
                //List<Todo> todoss = todos;

                if (!string.IsNullOrEmpty(searchString))
                {
                    todos = todos.Where(todo =>
                                            todo.Todoname.ToLower().Contains(searchString.ToLower()) ||
                                            todo.Status?.ToLower().Contains(searchString.ToLower()) == true ||
                                            todo.CreatedOn.ToString().ToLower().Contains(searchString.ToLower())
                                     ).ToList();
                }

                switch (sortField)
                {
                    case "Task":
                        todos = sortAscending ? todos.OrderBy(item => item.Todoname).ToList() :
                            todos.OrderByDescending(item => item.Todoname).ToList();
                        break;
                    case "Status":
                        todos = sortAscending ? todos.OrderBy(item => item.Status).ToList() :
                            todos.OrderByDescending(item => item.Status).ToList();
                        break;
                    case "CreatedOn":
                        todos = sortAscending ? todos.OrderBy(item => item.CreatedOn).ToList() :
                            todos.OrderByDescending(item => item.CreatedOn).ToList();
                        break;
                    default:
                        // Default sorting if no valid sort field provided (sort by Id by default)
                        todos = todos.OrderByDescending(item => item.Id).ToList();
                        break;
                }


                int tot_records = todos.Count;
                int pagesize = rowperpage > 0 ? rowperpage : 10;
                int number_of_button = 4;

                Pager P = new Pager(tot_records, page, pagesize, number_of_button, searchString);
                ViewBag.pager = P;
                int skip_records = (Math.Max(P.CurrentPage, 1) - 1) * pagesize;
                int take_records = pagesize;
                List<Todo> todoss = todos.Skip(skip_records).Take(take_records).ToList();



                TodoviewModel todoviewModel = new TodoviewModel()
                {
                    Todos = todoss,
                    Todo = null,

                };

                // Return the view with the list of Todo items
                return View(todoviewModel);

            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }

        }

        [HttpGet]
        public async Task<IActionResult> CompletedTodo(int page, int rowperpage, string? searchString = null, string? sortField = null, bool sortAscending = true)
        {

            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                if (page <= 0) { page = 1; }

                ViewBag.CurrentSortField = sortField;
                ViewBag.CurrentSortAscending = sortAscending;
                // Fetch all Todo items from the database
                var todos = await _context.Todos.Where(item => item.UserId == LogSesson.Id && item.Status == "Done").ToListAsync();
                //List<Todo> todoss = todos;
                if (!string.IsNullOrEmpty(searchString))
                {
                    todos = todos.Where(todo =>
                                            todo.Todoname.ToLower().Contains(searchString.ToLower()) ||
                                            todo.Status?.ToLower().Contains(searchString.ToLower()) == true ||
                                            todo.CreatedOn.ToString().ToLower().Contains(searchString.ToLower())
                                     ).ToList();
                }

                switch (sortField)
                {
                    case "Task":
                        todos = sortAscending ? todos.OrderBy(item => item.Todoname).ToList() :
                            todos.OrderByDescending(item => item.Todoname).ToList();
                        break;
                    case "Status":
                        todos = sortAscending ? todos.OrderBy(item => item.Status).ToList() :
                            todos.OrderByDescending(item => item.Status).ToList();
                        break;
                    case "CreatedOn":
                        todos = sortAscending ? todos.OrderBy(item => item.CreatedOn).ToList() :
                            todos.OrderByDescending(item => item.CreatedOn).ToList();
                        break;
                    default:
                        // Default sorting if no valid sort field provided (sort by Id by default)
                        todos = todos.OrderByDescending(item => item.Id).ToList();
                        break;
                }


                int tot_records = todos.Count;
                int pagesize = rowperpage > 0 ? rowperpage : 10;
                int number_of_button = 4;

                Pager P = new Pager(tot_records, page, pagesize, number_of_button, searchString);
                ViewBag.pager = P;
                int skip_records = (Math.Max(P.CurrentPage, 1) - 1) * pagesize;
                int take_records = pagesize;
                List<Todo> todoss = todos.Skip(skip_records).Take(take_records).ToList();



                TodoviewModel todoviewModel = new TodoviewModel()
                {
                    Todos = todoss,
                    Todo = null,

                };

                // Return the view with the list of Todo items
                return View(todoviewModel);

            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }


        }


        [HttpGet]
        public async Task<IActionResult> TodoReports()
        {
            try
            {


                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;

                var supportEngineer = _context.Users.Where(i => i.Department == "Support Engineer").ToList();

                var reportView = new TodoReportView
                {
                    Todos = null,
                    ListofEmployee = supportEngineer,
                    search = null,
                    TodoHederInfo = null,
                    brokerages = null,

                };


                return View(reportView);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpGet]
        public async Task<IActionResult> TodoSearch(TodoReportView reportView)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                reportView.search ??= new XFLCSMS.Models.Todos.TodoSearch();
                if (reportView.search != null)
                {
                    // Check if FromDate is not null and is greater than or equal to the current date
                    if (reportView.search.FromDate.HasValue && reportView.search.FromDate >= DateTime.Now)
                    {
                        reportView.search.FromDate = DateTime.Now;
                    }

                    // Check if ToDate is not null and is greater than or equal to the current date
                    if (reportView.search.ToDate.HasValue && reportView.search.ToDate >= DateTime.Now)
                    {
                        reportView.search.ToDate = DateTime.Now;
                    }
                }

                var searchResults = _context.Todos
                    .Where(item => item.UserId == LogSesson.Id)
                    .OrderByDescending(item => item.Id)
                    .ToList();


                if (!String.IsNullOrEmpty(reportView.search.Status))
                    searchResults = searchResults.Where(x => x.Status != null && x.Status.Contains(reportView.search.Status)).ToList();

                // both ends of the range are inclusive, and either end may be left empty
                if (reportView.search.FromDate != null)
                    searchResults = searchResults.Where(x => x.CreatedOn.Date >= reportView.search.FromDate.Value.Date).ToList();
                if (reportView.search.ToDate != null)
                    searchResults = searchResults.Where(x => x.CreatedOn.Date <= reportView.search.ToDate.Value.Date).ToList();

                string Brocaragename = GetBrocarageHouseName(LogSesson.BrokerageHouseName);
                string EmployeeNamee = LogSesson.FullName;



                int TotalTodo = searchResults.Count();
                int TotalInprogressTodo = searchResults.Where(x => x.Status == "In progress").Count();
                int TotalCompletedTodoo = searchResults.Where(x => x.Status == "Done").Count();
                int TotalCancledTodo = TotalTodo - TotalInprogressTodo - TotalCompletedTodoo;


                TodoHederInfo SectionInfo = new TodoHederInfo
                {
                    BrokerageHouseName = Brocaragename,
                    EmployeeName = EmployeeNamee,
                    TotalTodo = TotalTodo,
                    TotalInprogress = TotalInprogressTodo,
                    TotalCanseled = TotalCancledTodo,
                    TotalCompleteTodo = TotalCompletedTodoo,
                    ReportName = "Admin"


                };

                // Populate the Issues property of the ReportView model with search results
                var reportViewWithSearchResults = new TodoReportView
                {
                    Todos = searchResults,
                    TodoHederInfo = SectionInfo,

                };

                // Return the partial view with the populated reportView model
                return PartialView("_todoSearchResult", reportViewWithSearchResults);

            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }


        }


        [HttpGet]
        public async Task<IActionResult> ViewAllTodo(int page, int rowperpage, string? searchString = null, string? sortField = null, bool sortAscending = true)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                if (page <= 0) { page = 1; }

                ViewBag.CurrentSortField = sortField;
                ViewBag.CurrentSortAscending = sortAscending;
                // Fetch all Todo items from the database
                //var todos = await _context.Todos.Where(item => item.UserId == LogSesson.Id && item.Status == "In progress").ToListAsync();
                // the team list shows who each to-do belongs to
                var todos = await _context.Todos
                            .Include(item => item.User)
                            .OrderByDescending(item => item.Id)
                            .ToListAsync();
                //List<Todo> todoss = todos;
                if (!string.IsNullOrEmpty(searchString))
                {
                    todos = todos.Where(todo =>
                                            todo.Todoname.ToLower().Contains(searchString.ToLower()) ||
                                            todo.Status?.ToLower().Contains(searchString.ToLower()) == true ||
                                            todo.CreatedOn.ToString().ToLower().Contains(searchString.ToLower())
                                     ).ToList();
                }

                switch (sortField)
                {
                    case "Task":
                        todos = sortAscending ? todos.OrderBy(item => item.Todoname).ToList() :
                            todos.OrderByDescending(item => item.Todoname).ToList();
                        break;
                    case "Status":
                        todos = sortAscending ? todos.OrderBy(item => item.Status).ToList() :
                            todos.OrderByDescending(item => item.Status).ToList();
                        break;
                    case "CreatedOn":
                        todos = sortAscending ? todos.OrderBy(item => item.CreatedOn).ToList() :
                            todos.OrderByDescending(item => item.CreatedOn).ToList();
                        break;
                    default:
                        // Default sorting if no valid sort field provided (sort by Id by default)
                        todos = todos.OrderByDescending(item => item.Id).ToList();
                        break;
                }


                int tot_records = todos.Count;
                int pagesize = rowperpage > 0 ? rowperpage : 10;
                int number_of_button = 4;

                Pager P = new Pager(tot_records, page, pagesize, number_of_button, searchString);
                ViewBag.pager = P;
                int skip_records = (Math.Max(P.CurrentPage, 1) - 1) * pagesize;
                int take_records = pagesize;
                List<Todo> todoss = todos.Skip(skip_records).Take(take_records).ToList();



                TodoviewModel todoviewModel = new TodoviewModel()
                {
                    Todos = todoss,
                    Todo = null,

                };

                // Return the view with the list of Todo items
                return View(todoviewModel);

            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }

        }


        [HttpGet]
        public async Task<IActionResult> AllTodoReports()
        {
            try
            {


                var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;

                var supportEngineer = _context.Users.ToList();
                var BrocarageHouse = _context.Brokerages.ToList();

                var reportView = new TodoReportView
                {
                    Todos = null,
                    ListofEmployee = supportEngineer,
                    search = null,
                    TodoHederInfo = null,
                    brokerages = BrocarageHouse,

                };


                return View(reportView);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }


        [HttpGet]
        public async Task<IActionResult> AllTodoSearch(TodoReportView reportView)
        {
            try { 
            var jsonStringFromSession = HttpContext.Session.GetString("AdminData");
            User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);


            reportView.search ??= new XFLCSMS.Models.Todos.TodoSearch();
            if (reportView.search != null)
            {
                // Check if FromDate is not null and is greater than or equal to the current date
                if (reportView.search.FromDate.HasValue && reportView.search.FromDate >= DateTime.Now)
                {
                    reportView.search.FromDate = DateTime.Now;
                }

                // Check if ToDate is not null and is greater than or equal to the current date
                if (reportView.search.ToDate.HasValue && reportView.search.ToDate >= DateTime.Now)
                {
                    reportView.search.ToDate = DateTime.Now;
                }
            }

            var searchResults = await _context.Todos.Include(item => item.User).ToListAsync();

            // both ends of the range are inclusive, and either end may be left empty
            if (reportView.search.FromDate != null)
                searchResults = searchResults.Where(x => x.CreatedOn.Date >= reportView.search.FromDate.Value.Date).ToList();
            if (reportView.search.ToDate != null)
                searchResults = searchResults.Where(x => x.CreatedOn.Date <= reportView.search.ToDate.Value.Date).ToList();

            if (!String.IsNullOrEmpty(reportView.search.Status))
                searchResults = searchResults.Where(x => x.Status != null && x.Status.Contains(reportView.search.Status)).ToList();

            if (reportView.search.BrocarageHouseName.HasValue)
                searchResults = searchResults.Where(x => x.BrokerageId == reportView.search.BrocarageHouseName).ToList();
            if (reportView.search.EmployeeName.HasValue)
                searchResults = searchResults.Where(x => x.UserId==reportView.search.EmployeeName).ToList();



            string Brocaragename = reportView.search.BrocarageHouseName !=null ?GetBrocarageHouseName(reportView.search.BrocarageHouseName) :"All";
            string EmployeeNamee = (reportView.search.EmployeeName != null) ? GetEmployeeName(reportView.search.EmployeeName) : "All";




            int TotalTodo = searchResults.Count();
            int TotalInprogressTodo = searchResults.Where(x => x.Status == "In progress").Count();
            int TotalCompletedTodoo = searchResults.Where(x => x.Status == "Done").Count();
            int TotalCancledTodo = TotalTodo - TotalInprogressTodo - TotalCompletedTodoo;


            TodoHederInfo SectionInfo = new TodoHederInfo
            {
                BrokerageHouseName = Brocaragename,
                EmployeeName = EmployeeNamee,
                TotalTodo = TotalTodo,
                TotalInprogress = TotalInprogressTodo,
                TotalCanseled = TotalCancledTodo,
                TotalCompleteTodo = TotalCompletedTodoo,
                ReportName = "Admin"


            };

            // Populate the Issues property of the ReportView model with search results
            var reportViewWithSearchResults = new TodoReportView
            {
                Todos = searchResults,
                TodoHederInfo = SectionInfo,

            };

            // Return the partial view with the populated reportView model
            return PartialView("_todoSearchResult", reportViewWithSearchResults);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }


















        private string GetBrocarageHouseName(int? id)
        {
            var Brocarage = _context.Brokerages.ToList();
            foreach (var item in Brocarage)
            {
                if(item.BrokerageId== id)
                {
                    return item.BrokerageHouseName;
                }
            }
            return null;
        }

        private string GetEmployeeName(int? id)
        {
            var user = _context.Users.ToList();
            foreach (var item in user)
            {
                if (item.Id == id)
                {
                    return item.FullName;
                }
            }
            return null;
        }

        private string GetBranchName(int id)
        {
            var Brocarage = _context.Branchhs.ToList();
            foreach (var item in Brocarage)
            {
                if (item.BranchId == id)
                {
                    return item.BranchName;
                }
            }
            return null;
        }


        private string UserName(int id)
        {
            var user = _context.Users.Where(i => i.Id == id).FirstOrDefault();
            return user?.FullName ?? string.Empty;
        }
        private string SupportTypeName(int? id)
        {
            var user = _context.SupportTypes.Where(i => i.SupportTypeId == id).FirstOrDefault();
            if (user == null)
            {
                return null;
            }
            return user.SType;
        }



        private string SupportCatagoryName(int? id)
        {
            var user = _context.SupportCatagories.Where(i => i.SupportCatagoryId == id).FirstOrDefault();
            if (user == null)
            {
                return null;
            }

            return user.SCatagory;
        }

        private string SupportSubCatagoryName(int? id)
        {
            var user = _context.SupportSubCatagories.Where(i => i.SupportSubCatagoryId == id).FirstOrDefault();
            if (user == null)
            {
                return null;
            }
            return user.SubCatagory;
        }

        private string AffectedSectionName(int? id)
        {
            var user = _context.AffectedSectionss.Where(i => i.AffectedSectionId == id).FirstOrDefault();
            if (user == null)
            {
                return null;
            }
            return user.ASection;
        }


    }
}
