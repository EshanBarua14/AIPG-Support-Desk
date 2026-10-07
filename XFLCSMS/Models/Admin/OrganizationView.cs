namespace XFLCSMS.Models.Admin
{
    /// <summary>The "Organization" page of a house admin: the own brokerage house and its branches.</summary>
    public class OrganizationView
    {
        public string Name { get; set; } = string.Empty;
        public string Acronym { get; set; } = string.Empty;
        public int Users { get; set; }
        public int Admins { get; set; }
        /// <summary>Registered accounts that wait for activation.</summary>
        public int Waiting { get; set; }
        public int OpenTickets { get; set; }
        public int ClosedTickets { get; set; }
        public List<OrganizationBranch> Branches { get; set; } = new();
    }

    public class OrganizationBranch
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Users { get; set; }
    }
}
