using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using Org.BouncyCastle.Asn1.Ocsp;
using System.Security.Cryptography;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Admin;
using XFLCSMS.Models.Common;
using XFLCSMS.Models.Issue;
using XFLCSMS.Models.Register;
using XFLCSMS.Models.Todos;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;
using XFLCSMS.Services;

namespace XFLCSMS.Controllers
{
    public class MakerController : CsmsController
    {
        private readonly DataContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        protected override Role MyRole => Role.HouseUser;

        // Which tickets this area shows follows from the role (CsmsController.VisibleIssues): a house user sees the
        // tickets he raised himself. HouseAdminController reuses every page of this controller for a whole house.

        public MakerController(DataContext context, IWebHostEnvironment webHostEnvironment, TicketService tickets)
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
                return View(await BuildDashboardAsync(includeHouses: false));
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpGet]
        public async Task<IActionResult> MackerTicketList(int page, int rowperpage, string? searchString = null, string?
            sortField = null, bool sortAscending = true)
        {
            try
            {

                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                if (page <= 0) { page = 1; }

                ViewBag.CurrentSortField = sortField;
                ViewBag.CurrentSortAscending = sortAscending;

                var TicketList = VisibleIssues
                .OrderByDescending(item => item.IssueId)
                .ToList();
                SetRaisers(TicketList);

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

        [HttpGet]
        public async Task<IActionResult> Pagination(int page, int rowperpage, string? searchString = null, string? sortField = null, bool sortAscending = true)
        {
            try
            {

                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;

                if (page <= 0) { page = 1; }

                ViewBag.CurrentSortField = sortField;
                ViewBag.CurrentSortAscending = sortAscending;

                var TicketList = VisibleIssues
                .OrderByDescending(item => item.IssueId)
                .ToList();
                SetRaisers(TicketList);

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
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.TNumber).ToList() : TicketList.OrderByDescending(item => item.TNumber).ToList();
                        break;
                    case "Tickets":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.ITitle).ToList() : TicketList.OrderByDescending(item => item.ITitle).ToList();
                        break;
                    case "Approval Status":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.AssignBy).ToList() : TicketList.OrderByDescending(item => item.AssignBy).ToList();
                        break;
                    case "Priority":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.Priority).ToList() : TicketList.OrderByDescending(item => item.Priority).ToList();
                        break;
                    case "Status":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.IStatus).ToList() : TicketList.OrderByDescending(item => item.IStatus).ToList();
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

        public async Task<IActionResult> UnassignedTicketList(int page, int rowperpage, string? searchString = null, string? sortField = null, bool sortAscending = true)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                if (page <= 0) { page = 1; }

                ViewBag.CurrentSortField = sortField;
                ViewBag.CurrentSortAscending = sortAscending;
                var TicketList = VisibleIssues.OrderByDescending(i => i.IssueId).Where(i => i.AssignOn == null
                && i.AssignBy == null && i.IStatus != "Close").ToList();
                SetRaisers(TicketList);

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
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.TNumber).ToList() : TicketList.OrderByDescending(item => item.TNumber).ToList();
                        break;
                    case "Tickets":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.ITitle).ToList() : TicketList.OrderByDescending(item => item.ITitle).ToList();
                        break;
                    case "Approval Status":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.AssignBy).ToList() : TicketList.OrderByDescending(item => item.AssignBy).ToList();
                        break;
                    case "Priority":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.Priority).ToList() : TicketList.OrderByDescending(item => item.Priority).ToList();
                        break;
                    case "Status":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.IStatus).ToList() : TicketList.OrderByDescending(item => item.IStatus).ToList();
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

        public async Task<IActionResult> ClosedTicketList(int page, int rowperpage, string? searchString = null, string? sortField = null, bool sortAscending = true)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                if (page <= 0) { page = 1; }

                ViewBag.CurrentSortField = sortField;
                ViewBag.CurrentSortAscending = sortAscending;
                List<IssueTable> TicketList = await VisibleIssues.OrderByDescending(i => i.IssueId).Where(i => i.IStatus == "Close").ToListAsync();
                SetRaisers(TicketList);
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

        [RequirePermission(Permission.TicketCreate)]
        public async Task<IActionResult> IssueRaiseFrom()
        {
            try
            {


                // Retrieve the JSON string from the session and deserialize it
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);

                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var errorMessage = TempData["ErrorMessage"] as string;
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    // Do something with the error message, e.g., pass it to the view
                    ViewBag.ErrorMessage = errorMessage;
                }

                var brocarage = await _context.Brokerages.ToListAsync();
                string[] acronymsArray = new string[brocarage.Count];
                //Dictionary<string, int> nameCount = new Dictionary<string, int>();
                int i = 0;
                foreach (var item in brocarage)
                {
                    string acronyme = item.BrokerageHouseAcronym;
                    acronymsArray[i] = acronyme;
                    i++;

                }

                var issueLoginInfo = new IssueLoginInfo
                {
                    UserId = LogSesson.Id,
                    BrocarageHouseName = CreateHouseName(LogSesson.BrokerageHouseName),
                    BranchName = CreateBranchName(LogSesson.Branch),
                    TicketID = GenerateTicketId(LogSesson.BrokerageHouseName)
                };

                var viewModel = new IssueViewModel
                {
                    SupportTypes = _context.SupportTypes.ToList(),
                    SupportCatagories = _context.SupportCatagories.ToList(),
                    SupportSubCatagories = _context.SupportSubCatagories.ToList(),
                    AffectedSections = _context.AffectedSectionss.ToList(),
                    Brokerages = _context.Brokerages.ToList(),
                    Branchhs = _context.Branchhs.ToList(),
                    LoginInfo = issueLoginInfo

                };
                return View(viewModel);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }


        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permission.TicketCreate)]
        public async Task<IActionResult> IssueRaiseFrom(IssueViewModel issueViewModel, List<IFormFile> files)
        {
            try
            {
                // Owner, brokerage house, ticket number and date are decided on the server (see TicketService).
                var result = await Tickets.CreateAsync(CurrentUser!, issueViewModel?.issueFrom, files);
                if (result.Issue == null)
                {
                    TempData["ErrorMessage"] = result.Error;
                    return RedirectToAction("IssueRaiseFrom");
                }

                if (result.RejectedFiles.Count > 0)
                {
                    TempData["ErrorMessage"] = "Ticket " + result.Issue.TNumber + " was created, but these files were not attached (file type not allowed): "
                        + string.Join(", ", result.RejectedFiles);
                }
                else
                {
                    TempData["SuccessMessage"] = "Ticket " + result.Issue.TNumber + " was created.";
                }

                return RedirectToAction("TicketView", new { id = result.Issue.IssueId });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }


        public async Task<IActionResult> TicketView(int? id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
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
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;

                var issueWithAttachments = _context.Issues
                    .Include(i => i.attachment)  // Include attachments in the query
                    .FirstOrDefault(i => i.IssueId == id);

                if (issueWithAttachments == null || !CanAccessIssue(issueWithAttachments))
                {
                    return NotFound();
                }

                var refusal = RefuseEdit(issueWithAttachments);
                if (refusal != null)
                {
                    return refusal;
                }


                var EditView = ToMakerView(issueWithAttachments, includeEngineers: false);
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

                var refusal = RefuseEdit(issue);
                if (refusal != null)
                {
                    return refusal;
                }

                Tickets.ApplyOwnerEdit(issue, makerView, editor);
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

        [HttpGet]
        public async Task<IActionResult> Reports()
        {
            try
            {


                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;

                var reportView = new ReportView
                {
                    Issues = null,

                };


                return View(reportView);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }


        [HttpGet]
        public async Task<IActionResult> Search(ReportView reportView)
        {
            // the report page can be opened/searched with an empty filter
            reportView.MakerSearch ??= new MakerSearch();
            var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
            User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
            if (reportView.MakerSearch.FromDate >= DateTime.Now)
            {
                reportView.MakerSearch.FromDate = DateTime.Now;
            }
            if (reportView.MakerSearch.ToDate >= DateTime.Now)
            {
                reportView.MakerSearch.ToDate = DateTime.Now;
            }

            var searchResults = VisibleIssues
                .OrderByDescending(item => item.IssueId)
                .ToList();


            if (!String.IsNullOrEmpty(reportView.MakerSearch.Priority))
                searchResults = searchResults.Where(x => x.Priority.Contains(reportView.MakerSearch.Priority)).ToList();

            if (!String.IsNullOrEmpty(reportView.MakerSearch.AStatus))
                searchResults = searchResults.Where(x => x.IStatus != null && x.IStatus.Contains(reportView.MakerSearch.AStatus)).ToList();

            // both ends of the range are inclusive, and either end may be left empty
            if (reportView.MakerSearch.FromDate != null)
                searchResults = searchResults.Where(x => x.TDate.Date >= reportView.MakerSearch.FromDate.Value.Date).ToList();
            if (reportView.MakerSearch.ToDate != null)
                searchResults = searchResults.Where(x => x.TDate.Date <= reportView.MakerSearch.ToDate.Value.Date).ToList();

            string Brocaragename = GetBrocarageHouseName(LogSesson.BrokerageHouseName);
            string EmployeeNamee = LogSesson.FullName;



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
                ReportName = "Maker"


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
        public async Task<IActionResult> TodoReports()
        {
            try
            {


                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;

                var reportView = new TodoReportView
                {
                    Todos = null,
                    ListofEmployee = null,
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
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
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
                    ReportName = "Maker"


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
        public async Task<IActionResult> ViewTodo(int page, int rowperpage, string? searchString = null, string? sortField = null, bool sortAscending = true)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
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


        [HttpGet]
        public async Task<IActionResult> AllTodo(int page, int rowperpage, string? searchString = null, string? sortField = null, bool sortAscending = true)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
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
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
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


        [HttpPost]
        public async Task<IActionResult> AddTodo(TodoviewModel newTodo)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
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
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var todo = await _context.Todos.FindAsync(id);
                if (todo == null || todo.UserId != LogSesson.Id)
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
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);

                var existingTodo = await _context.Todos.FindAsync(model.Id);
                if (existingTodo == null || existingTodo.UserId != LogSesson.Id)
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









        private string GetBrocarageHouseName(int? id)
        {
            var Brocarage = _context.Brokerages.ToList();
            foreach (var item in Brocarage)
            {
                if (item.BrokerageId == id)
                {
                    return item.BrokerageHouseName;
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
        private string CreateBranchName(int id)
        {
            var Brocaragessss = _context.Branchhs.ToList();
            foreach (var item in Brocaragessss)
            {
                if (item.BranchId == id)
                {
                    string BranchName = item.BranchName;
                    return BranchName;
                }

            }
            return null;

        }

        private string CreateHouseName(int id)
        {
            var Brocaragessss = _context.Brokerages.ToList();
            foreach (var item in Brocaragessss)
            {
                if (item.BrokerageId == id)
                {
                    string HouseName = item.BrokerageHouseName;
                    return HouseName;
                }

            }
            return null;

        }

        private int CreateBrocarageID(string brocarageName)
        {
            var Brocaragessss = _context.Brokerages.ToList();
            foreach (var item in Brocaragessss)
            {
                if (item.BrokerageHouseName == brocarageName)
                {
                    return item.BrokerageId;
                }

            }
            return 0;

        }

        private string GenerateTicketId(int id)
        {
            // Shown on the form as a preview only; the real number is taken when the ticket is saved.
            return Tickets.NextTicketNumber(id) ?? string.Empty;
        }
    }
}
