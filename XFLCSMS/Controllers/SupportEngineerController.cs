using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using System.Security.Cryptography;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Admin;
using XFLCSMS.Models.Common;
using XFLCSMS.Models.Issue;
using XFLCSMS.Models.Register;
using XFLCSMS.Models.Todos;
using XFLCSMS.Services;

namespace XFLCSMS.Controllers
{
    public class SupportEngineerController : CsmsController
    {
        private readonly DataContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        protected override Role MyRole => Role.SupportEngineer;

        public SupportEngineerController(DataContext context, IWebHostEnvironment webHostEnvironment, TicketService tickets)
            : base(context, tickets)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
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

        /// <summary>The main ticket list of this role: every ticket it may see (CsmsController.VisibleIssues).</summary>
        public Task<IActionResult> AllTicketList(int page, int rowperpage, string? searchString = null, string? sortField = null, bool sortAscending = true, string? status = null)
        {
            return TicketListPage(tickets => tickets, page, rowperpage, searchString, sortField, sortAscending, status);
        }

        /// <summary>The open tickets assigned to the signed-in engineer.</summary>
        public Task<IActionResult> AssignedTicketList(int page, int rowperpage, string? searchString = null, string? sortField = null, bool sortAscending = true, string? status = null)
        {
            return TicketListPage(tickets => tickets.Where(TicketService.AssignedTo(CurrentUser!)).Where(TicketService.NotClosed), page, rowperpage, searchString, sortField, sortAscending, status);
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
                    Brokerages = _context.Brokerages.ToList(),
                    Branchhs = _context.Branchhs.ToList(),
                    LoginInfo = issueLoginInfo

                };
                FillTicketChoices(viewModel); // products and the support lists (CsmsController.Products.cs)
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
                return String.Empty;
            }
            return user.SType;
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
                    ReportName = "Support Engineer"


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
                if (item.BrokerageId == id)
                {
                    return item.BrokerageHouseName;
                }
            }
            return null;
        }
        private string SupportCatagoryName(int? id)
        {
            var user = _context.SupportCatagories.Where(i => i.SupportCatagoryId == id).FirstOrDefault();
            if (user == null)
            {
                return String.Empty;
            }

            return user.SCatagory;
        }

        private string SupportSubCatagoryName(int? id)
        {
            var user = _context.SupportSubCatagories.Where(i => i.SupportSubCatagoryId == id).FirstOrDefault();
            if (user == null)
            {
                return String.Empty;
            }
            return user.SubCatagory;
        }

        private string AffectedSectionName(int? id)
        {
            var user = _context.AffectedSectionss.Where(i => i.AffectedSectionId == id).FirstOrDefault();
            if (user == null)
            {
                return String.Empty;
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
            return String.Empty;

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
            return String.Empty;

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
