using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using XFLCSMS.Infrastructure;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using XFLCSMS.Models;
using XFLCSMS.Models.Brocarage;
using XFLCSMS.Models.Issue;
using XFLCSMS.Models.Register;

namespace XFLCSMS.Controllers
{
    // Old scaffolding pages (brokerage CRUD, file upload). They were open to anyone who knew the URL.
    [SessionAuthorize(SessionAuthorizeAttribute.Admin)]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly DataContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        

        public HomeController(ILogger<HomeController> logger, DataContext context, IWebHostEnvironment webHostEnvironment)
        {
            _logger = logger;
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }


        public IActionResult Index()
        {

            var Brocaragee = _context.Brokerages.ToList();
            return View(Brocaragee);
        }

        public IActionResult FileUpload()
        {

            return View();
        }
        [HttpPost]
        public async Task<IActionResult> FileUpload( IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                ViewBag.Messege = "Please choose a file first.";
                return View();
            }

            string UploadFolder = Path.Combine(_webHostEnvironment.WebRootPath, "Uplods");
            if(!Directory.Exists(UploadFolder))
            {
                Directory.CreateDirectory(UploadFolder);
            }
            string fileName = Path.GetFileName(file.FileName);
            string fileSavePath = Path.Combine(UploadFolder, fileName);

            using (FileStream stream = new FileStream(fileSavePath, FileMode.Create))
            {
               await file.CopyToAsync(stream);
            }

            ViewBag.Messege = fileName + " uploaded successfully";
            return View();
              
        }

        Dictionary<string, int> nameCount = new Dictionary<string, int>();
        public async Task<IActionResult> IssueRaiseFrom()
        {

            // Retrieve the JSON string from the session and deserialize it
           var jsonStringFromSession = HttpContext.Session.GetString("MyObjectData");
           User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);

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

        public IActionResult Create()
        {

            return View();
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("BrokerageId,BrokerageHouseName,BrokerageHouseAcronym")] Brokerage brokerage)
        {
           
                await _context.AddAsync(brokerage);
                await _context.SaveChangesAsync();
                return RedirectToAction("BrocarageHouseList","Admin"); // Redirect to the action that displays a list of brokerages     
        }
        public IActionResult Brocarage()
        {
            return View();
        }
        [HttpPost]
        public IActionResult Brocarage(Brokerage brokerage)
        {
            if(_context.Brokerages.Any(u=> u.BrokerageHouseName == brokerage.BrokerageHouseName)) 
            {
                RedirectToAction("Brocarage");
            }

            return View();
        }

        public async Task<IActionResult> Details(int? id)
        {
            if(id == null || _context.Brokerages==null)
            {
                return NotFound();
            }

            var stdData = await _context.Brokerages.FirstOrDefaultAsync(u=> u.BrokerageId==id);

            if(stdData==null)
            {
                return NotFound();
            }
            return View(stdData);
        }

        // GET: Home/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || _context.Brokerages == null)
            {
                return NotFound();
            }

            var student = await _context.Brokerages.FindAsync(id);
            if (student == null)
            {
                return NotFound();
            }
            return View(student);

        }

        //POST: Home/Edit/5
         //To protect from overposting attacks, enable the specific properties you want to bind to.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("BrokerageId,BrokerageHouseName,BrokerageHouseAcronym")] Brokerage brokerage)
        {
            if (id != brokerage.BrokerageId)
            {
                return NotFound();
            }

            
                try
                {
                    _context.Update(brokerage);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!BrokerageExists(brokerage.BrokerageId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            
            //return View(brokerage);
        }

        // GET: Home/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null || _context.Brokerages == null)
            {
                return NotFound();
            }

            var brocarage = await _context.Brokerages
                .FirstOrDefaultAsync(m => m.BrokerageId == id);
            if (brocarage == null)
            {
                return NotFound();
            }

            return View(brocarage);
        }

        // POST: Home/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (_context.Brokerages == null)
            {
                return Problem("Entity set 'MyDatabaseContext.Brocarage'  is null.");
            }
            var brocarage = await _context.Brokerages.FindAsync(id);
            if (brocarage != null)
            {
                _context.Brokerages.Remove(brocarage);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }



        [AllowAnonymous]
        public IActionResult Privacy()
        {
            return View();
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        private bool BrokerageExists(int id)
        {
            return (_context.Brokerages?.Any(e => e.BrokerageId == id)).GetValueOrDefault();
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

        private string GenerateTicketId(int id)
        {
            var Brocaragessss = _context.Brokerages.ToList();
            foreach (var item in Brocaragessss)
            {

                if (item.BrokerageId == id)
                {
                    var testData = _context.Issues.ToList();
                    int max = 0000000;
                    //Dictionary<string, int> dictionary = new Dictionary<string, int>();
                    foreach (var item2 in testData)
                    {
                        string name = item2.TNumber.Split('_')[0];
                        int value = Int32.Parse(item2.TNumber.Split('_')[1]);

                        if (name == item.BrokerageHouseAcronym)
                        {
                            if (value > max)
                                max = value;
                        }

                    }

                    max = max + 1;
                    string formattedNumber = max.ToString("D7");
                    string ticketID = item.BrokerageHouseAcronym + '_' + formattedNumber;
                    return ticketID;
                }

            }


            return null;
        }



    }
        
}