
using Microsoft.EntityFrameworkCore;
using XFLCSMS.Models.Affected;
using XFLCSMS.Models.Audit;
using XFLCSMS.Models.Branch;
using XFLCSMS.Models.Brocarage;
using XFLCSMS.Models.Issue;
using XFLCSMS.Models.Register;
using XFLCSMS.Models.Support;
using XFLCSMS.Models.Login;
using XFLCSMS.Models.DataTable;
using XFLCSMS.Models.Todos;

namespace XFLCSMS.Data
{
    public class DataContext:DbContext
    {
        public DataContext(DbContextOptions<DataContext> options) : base(options)
        {

        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Branchh> Branchhs {  get; set; }
        public DbSet<Brokerage> Brokerages { get; set; }
        public DbSet<SupportType> SupportTypes { get; set; }
        public DbSet<SupportCatagory> SupportCatagories { get; set; }
        public DbSet<SupportSubCatagory> SupportSubCatagories { get; set; }
        public DbSet<AffectedSection> AffectedSectionss { get; set; }

        public DbSet<IssueTable> Issues { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Attachment> Attachments { get; set; }
        public DbSet<Todo> Todos { get; set; }
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        /// <summary>
        /// Runs right before changes are written. The signed-in area sets it to add the audit lines for master data
        /// (houses, branches, support lists), so those are recorded wherever such a row is created, changed or deleted.
        /// </summary>
        public Action? BeforeSaving { get; set; }

        private bool _beforeSavingRuns;

        private void RunBeforeSaving()
        {
            if (BeforeSaving == null || _beforeSavingRuns)
            {
                return;
            }

            _beforeSavingRuns = true;
            try
            {
                BeforeSaving();
            }
            finally
            {
                _beforeSavingRuns = false;
            }
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            RunBeforeSaving();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            RunBeforeSaving();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // The audit trail is read newest first, per item (history of a ticket) and per brokerage house.
            modelBuilder.Entity<AuditLog>().HasIndex(log => log.At);
            modelBuilder.Entity<AuditLog>().HasIndex(log => new { log.EntityType, log.EntityId });
            modelBuilder.Entity<AuditLog>().HasIndex(log => log.BrokerageId);

            // "Assigned to me" is looked up by the engineer's user id.
            modelBuilder.Entity<IssueTable>().HasIndex(issue => issue.AssignedToId);
        }


    }
}
