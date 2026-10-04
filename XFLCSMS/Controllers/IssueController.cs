using MessagePack;
using Microsoft.AspNetCore.Mvc;
using XFLCSMS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Newtonsoft.Json;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using XFLCSMS.Models.Brocarage;
using XFLCSMS.Models.Issue;
using XFLCSMS.Models.Register;


namespace XFLCSMS.Controllers
{
    [SessionAuthorize(SessionAuthorizeAttribute.Admin)]
    public class IssueController : Controller
    {
        private readonly DataContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IConfiguration _configuration;
        

        public IssueController(DataContext context, IWebHostEnvironment webHostEnvironment, IConfiguration configuration )
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _configuration = configuration;
        }

        Dictionary<string, int> nameCount = new Dictionary<string, int>();

        public int EditIssueId = 0; 
        public async Task<IActionResult> IssueRaiseFrom()
        {

            // Retrieve the JSON string from the session and deserialize it
            var jsonStringFromSession =  HttpContext.Session.GetString("MyObjectData");
            User LogSesson =  JsonConvert.DeserializeObject<User>(jsonStringFromSession);

            var brocarage = await _context.Brokerages.ToListAsync();
            string[] acronymsArray = new string[brocarage.Count];
            //Dictionary<string, int> nameCount = new Dictionary<string, int>();
            int i = 0;
            foreach(var item in brocarage)
            {
                string acronyme = item.BrokerageHouseAcronym;
                acronymsArray[i] = acronyme;
                i++;

            }

            var issueLoginInfo = new IssueLoginInfo
            {
                UserId = LogSesson.Id,
                BrocarageHouseName =  CreateHouseName(LogSesson.BrokerageHouseName),
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IssueRaiseFrom(IssueViewModel issueViewModel, List<IFormFile> files)
        {
            var issue = new IssueTable
            {
                TDate = issueViewModel.issueFrom.dateTime,
                TNumber = issueViewModel.issueFrom.TicketId,
               
                Priority = issueViewModel.issueFrom.Priority,
                Details = issueViewModel.issueFrom.IssueDetails.ToString(),
                Comments = issueViewModel.issueFrom.Commands.ToString(),
                UserId = issueViewModel.issueFrom.UserId,
                SupportCatagoryId = issueViewModel.issueFrom.SupportCatagoryId,
                SupportTypeId = issueViewModel.issueFrom.SupportTypeId,
                SupportSubCatagoryId = issueViewModel.issueFrom.SupportSubCatagoryID,
                AffectedSectionId = issueViewModel.issueFrom.AffectedSectionId,
                ITitle = issueViewModel.issueFrom.ITitle,
                //AffectedSectionId= null,
                //SupportCatagoryId= null,
                //SupportSubCatagoryId=null,
                //SupportTypeId = null,

                BrokerageId = CreateBrocarageID(issueViewModel.issueFrom.BrocarageHouseName)
            };

            _context.Issues.Add(issue);
            _context.SaveChanges();

            var uploadFolder = Path.Combine(_webHostEnvironment.WebRootPath, "Uplods");
            Directory.CreateDirectory(uploadFolder); // Create the directory once outside the loop

            foreach (var file in files)
            {
                var fileName = file.FileName; // Ensure unique file names
                var filePath = Path.Combine(uploadFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var attachment = new Attachment
                {
                    FileName = file.FileName,
                    AttachmentLoc = filePath,
                    IssueId = issue.IssueId
                };

                _context.Attachments.Add(attachment);
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("MackerTicketList");
        }

        public async Task<IActionResult> MackerTicketList()
        {
            var jsonStringFromSession = HttpContext.Session.GetString("MyObjectData");
            User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);

           
                    var TicketList = _context.Issues
                            .Where(item => item.UserId == LogSesson.Id)
                            .OrderByDescending(item => item.IssueId)
                            .ToList();
                    

                return View(TicketList);
          
        }

        public async Task<IActionResult> TicketView(int? id)
        {
            var issueWithAttachments = _context.Issues
                .Include(i => i.attachment)  // Include attachments in the query
                .FirstOrDefault(i => i.IssueId == id);

            if (issueWithAttachments == null)
            {
                return NotFound();
            }

            var makerView = new MakerView
            {
                IssueId = issueWithAttachments.IssueId,
                CreatedOn =issueWithAttachments.TDate,
                CreatedBy = UserName(issueWithAttachments.UserId),
                AssgnOn = issueWithAttachments.AssignOn,
                AssgnBy = issueWithAttachments.AssignBy,
                SupportType = SupportTypeName(issueWithAttachments.SupportTypeId),
                SupportCatagory = SupportCatagoryName(issueWithAttachments.SupportCatagoryId),
                SupportSubCatagory = SupportSubCatagoryName(issueWithAttachments.SupportSubCatagoryId),
                AffectedSection = AffectedSectionName(issueWithAttachments.AffectedSectionId),
                TicketDetails = issueWithAttachments.Details,
                Command = issueWithAttachments.Comments,
                TicketStatus = issueWithAttachments.IStatus,
                Priority = issueWithAttachments.Priority,
                Attachments = issueWithAttachments.attachment,
                ApproveOn = issueWithAttachments.ApproveOn,
                ApproveBy = issueWithAttachments.ApproveBy,


            };

            return View(makerView);
        }


        public async Task<IActionResult> DownloadAttachment(int? att)
        {
            if (att == null)
            {
                return BadRequest("Attachment ID is missing in the query parameters.");
            }

            var attachment = await _context.Attachments.FirstOrDefaultAsync(i => i.AttachmentId == att);

            if (attachment == null)
            {
                return NotFound("Attachment not found.");
            }

            var filePath = attachment.AttachmentLoc;
            var fileName = attachment.FileName;

            if (System.IO.File.Exists(filePath))
            {
                return PhysicalFile(filePath, "application/pdf", fileName);
            }
            else
            {
                return NotFound("File not found."); // or handle as appropriate
            }
        }


        public async Task<IActionResult> EditTicket(int id)
            {
            
            var issueWithAttachments = _context.Issues
                .Include(i => i.attachment)  // Include attachments in the query
                .FirstOrDefault(i => i.IssueId == id);
            var SupportEng = _context.Users.Where(i => i.Designation == "Support Engineer").ToList();

            if (issueWithAttachments == null)
            {
                return NotFound();
            }
            EditIssueId = issueWithAttachments.IssueId;

            var EditView = new MakerView
            {
                IssueId = issueWithAttachments.IssueId,
                CreatedOn = issueWithAttachments.TDate,
                CreatedBy = UserName(issueWithAttachments.UserId),
                AssgnOn = issueWithAttachments.AssignOn,
                AssgnBy = issueWithAttachments.AssignBy,
                SupportType = SupportTypeName(issueWithAttachments.SupportTypeId),
                SupportCatagory = SupportCatagoryName(issueWithAttachments.SupportCatagoryId),
                SupportSubCatagory = SupportSubCatagoryName(issueWithAttachments.SupportSubCatagoryId),
                AffectedSection = AffectedSectionName(issueWithAttachments.AffectedSectionId),
                TicketDetails = issueWithAttachments.Details,
                Command = issueWithAttachments.Comments,
                TicketStatus = issueWithAttachments.IStatus,
                Attachments = issueWithAttachments.attachment,
                IssueTitle = issueWithAttachments.ITitle,
                Priority = issueWithAttachments.Priority,
                SupportEngineers = SupportEng,
                ApproveOn = issueWithAttachments.ApproveOn,
                ApproveBy = issueWithAttachments.ApproveBy, 

            };
            return View(EditView);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTicketttt(MakerView makerView, List<IFormFile> files)
        {
            var jsonStringFromSession = HttpContext.Session.GetString("MyObjectData");
            User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
            var issue = await _context.Issues.FirstOrDefaultAsync(a=>a.IssueId == makerView.IssueId);
            if(issue !=null)
            {
                issue.ITitle = makerView.IssueTitle;
                issue.Details = makerView.TicketDetails;
                issue.Comments = makerView.Command;
                issue.AssignOn = makerView.AssgnOn;
                issue.AssignBy = makerView.AssgnBy;
                if(makerView.AssgnBy == null)
                {
                    issue.AssignOn = null;
                    issue.AssignBy = null;
                }
                if(issue.AssignBy != null && LogSesson.UType ==true)
                {
                    issue.ApproveOn=DateTime.Now;
                    issue.ApproveBy = LogSesson.FullName;
                }
                else
                {
                    issue.ApproveOn = null;
                    issue.ApproveBy = null;
                }
                issue.IStatus = makerView.IStatus;
                if(makerView.IStatus == "Close")
                {
                    issue.ClosedOn = DateTime.Now;
                    issue.ClosedBy = LogSesson.FullName;
                }
                else
                {
                    issue.ClosedOn = null;
                    issue.ClosedBy = null;
                }

                _context.Update(issue);
                await _context.SaveChangesAsync();


                if(files != null)
                {
                    var uploadFolder = Path.Combine(_webHostEnvironment.WebRootPath, "Uplods");
                    Directory.CreateDirectory(uploadFolder); // Create the directory once outside the loop

                    foreach (var file in files)
                    {
                        var fileName = file.FileName; // Ensure unique file names
                        var filePath = Path.Combine(uploadFolder, fileName);

                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await file.CopyToAsync(stream);
                        }

                        var attachment = new Attachment
                        {
                            FileName = file.FileName,
                            AttachmentLoc = filePath,
                            IssueId = makerView.IssueId
                        };

                        _context.Attachments.Add(attachment);
                    }

                    await _context.SaveChangesAsync();
                }


                return RedirectToAction("AdminView","Admin");
            }

         
            return NotFound("Edit is not done");
        }

        [HttpPost]
        public IActionResult DeleteTicket(int id)
        {
            // Perform the deletion logic here (remove the issue from the database)
            var issueToDelete = _context.Issues.Find(id);
            if (issueToDelete != null)
            {
                _context.Issues.Remove(issueToDelete);
                _context.SaveChanges();
                return Ok(); // Return a success status
            }
            return NotFound(); // Return a not found status if the issue doesn't exist
        }


        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task DeleteConfirmed(int id)
        {
            if (_context.Attachments == null)
            {
                // Log or handle the issue, as there is no return to the client
                return;
            }

            var path = await _context.Attachments.FindAsync(id);
            if (path != null)
            {
                _context.Attachments.Remove(path);
            }

            await _context.SaveChangesAsync();
            // No return statement here
        }
        [HttpPost]
        public IActionResult DeleteAttachment(int attachmentId)
        {
            var attachment = _context.Attachments.Find(attachmentId);

            if (attachment == null)
            {
                return NotFound();
            }

            _context.Attachments.Remove(attachment);
            _context.SaveChanges();

            return Ok();
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
            foreach(var item in Brocaragessss)
            {
                if(item.BrokerageId == id)
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
                    int max = Int32.MinValue;
                    //Dictionary<string, int> dictionary = new Dictionary<string, int>();
                    foreach (var item2 in testData)
                    {
                        string name = item2.TNumber.Split('_')[0];
                        int value = Int32.Parse(item2.TNumber.Split('_')[1]);
                        
                        if(name == item.BrokerageHouseAcronym)
                        {
                            if(value > max)
                                max = value;
                        }

                    }

                    max = max + 1;
                    string ticketID = $"{item.BrokerageHouseAcronym}+ '_' + {max}";
                    return ticketID;
                }

            }


            return null;
        }

        private int CreateBrocarageID(string brocarageName)
        {
            var Brocaragessss = _context.Brokerages.ToList();
            foreach(var item in Brocaragessss)
            {
                if(item.BrokerageHouseName == brocarageName)
                {
                    return item.BrokerageId;
                }

            }
            return 0;

        }

        private string UserName(int id)
        {
            var user = _context.Users.Where(i=>i.Id == id).FirstOrDefault();
            return user.FullName;
        }
        private string SupportTypeName(int? id)
        {
            var user = _context.SupportTypes.Where(i => i.SupportTypeId == id).FirstOrDefault();
            if(user == null)
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
