
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
using XFLCSMS.Models.Settings;
using XFLCSMS.Models.Notify;

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
        public DbSet<Product> Products => Set<Product>();
        public DbSet<SupportType> SupportTypes { get; set; }
        public DbSet<SupportCatagory> SupportCatagories { get; set; }
        public DbSet<SupportSubCatagory> SupportSubCatagories { get; set; }
        public DbSet<AffectedSection> AffectedSectionss { get; set; }

        public DbSet<IssueTable> Issues { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Attachment> Attachments { get; set; }
        public DbSet<Todo> Todos { get; set; }
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<AppSetting> AppSettings => Set<AppSetting>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
        public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
        public DbSet<XFLCSMS.Models.Desk.TicketMessage> TicketMessages => Set<XFLCSMS.Models.Desk.TicketMessage>();
        public DbSet<XFLCSMS.Models.Desk.CannedReply> CannedReplies => Set<XFLCSMS.Models.Desk.CannedReply>();
        public DbSet<XFLCSMS.Models.Desk.KbArticle> KbArticles => Set<XFLCSMS.Models.Desk.KbArticle>();
        public DbSet<XFLCSMS.Models.Desk.Tag> Tags => Set<XFLCSMS.Models.Desk.Tag>();
        public DbSet<XFLCSMS.Models.Desk.TicketTag> TicketTags => Set<XFLCSMS.Models.Desk.TicketTag>();
        public DbSet<XFLCSMS.Models.Desk.TicketLink> TicketLinks => Set<XFLCSMS.Models.Desk.TicketLink>();
        public DbSet<XFLCSMS.Models.Desk.TicketWatcher> TicketWatchers => Set<XFLCSMS.Models.Desk.TicketWatcher>();

        /// <summary>
        /// Runs right before changes are written. The signed-in area sets it to add the audit lines for master data
        /// (houses, branches, support lists), so those are recorded wherever such a row is created, changed or deleted.
        /// </summary>
        public Action? BeforeSaving { get; set; }

        /// <summary>
        /// Runs after changes were written successfully. The notification service uses it to show the toasts and wake
        /// the e-mail / SMS worker only for changes that really reached the database.
        /// </summary>
        public Action? AfterSaving { get; set; }

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
            var written = base.SaveChanges(acceptAllChangesOnSuccess);
            AfterSaving?.Invoke();
            return written;
        }

        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            RunBeforeSaving();
            var written = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            AfterSaving?.Invoke();
            return written;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            // The connection string asks for several result sets at a time (MultipleActiveResultSets). With that
            // setting SQL Server cannot set save points inside a transaction, and EF Core logs a warning for every
            // save in one. The one place that uses a transaction (loading / removing demo data) rolls back as a
            // whole, so there is nothing to act on - and the warnings would fill the list on the system health page.
            optionsBuilder.ConfigureWarnings(warnings => warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.SqlServerEventId.SavepointsDisabledBecauseOfMARS));
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // The audit trail is read newest first, per item (history of a ticket) and per brokerage house.
            modelBuilder.Entity<AuditLog>().HasIndex(log => log.At);
            modelBuilder.Entity<AuditLog>().HasIndex(log => new { log.EntityType, log.EntityId });
            modelBuilder.Entity<AuditLog>().HasIndex(log => log.BrokerageId);

            // The bell reads the newest notifications of one user; the worker reads what is still to send.
            modelBuilder.Entity<Notification>().HasIndex(notice => new { notice.UserId, notice.Id });
            modelBuilder.Entity<NotificationDelivery>().HasIndex(delivery => new { delivery.Status, delivery.NextTryAt });

            // "Assigned to me" is looked up by the engineer's user id.
            modelBuilder.Entity<IssueTable>().HasIndex(issue => issue.AssignedToId);

            // Products of AIPG. A support list entry and a ticket may point at one product; a product that is still
            // pointed at cannot be deleted (the page says so, and the database would refuse as well).
            modelBuilder.Entity<Product>().HasIndex(product => product.Name).IsUnique();
            modelBuilder.Entity<SupportType>().HasOne(item => item.Product).WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<SupportCatagory>().HasOne(item => item.Product).WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<SupportSubCatagory>().HasOne(item => item.Product).WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<AffectedSection>().HasOne(item => item.Product).WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<IssueTable>().HasOne(issue => issue.Product).WithMany().HasForeignKey(issue => issue.ProductId).OnDelete(DeleteBehavior.Restrict);
            // The conversation of a ticket is read per ticket, oldest first; it goes when the ticket is deleted.
            modelBuilder.Entity<XFLCSMS.Models.Desk.TicketMessage>().HasIndex(message => new { message.IssueId, message.Id });
            modelBuilder.Entity<XFLCSMS.Models.Desk.TicketMessage>()
                .HasOne(message => message.Issue).WithMany().HasForeignKey(message => message.IssueId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Attachment>().HasIndex(file => file.MessageId);

            // The watcher of the service targets looks for open tickets whose target time comes up.
            modelBuilder.Entity<IssueTable>().HasIndex(issue => issue.ResolveDueAt);

            // Knowledge base. An article of a product that is deleted becomes an article for every product.
            modelBuilder.Entity<XFLCSMS.Models.Desk.KbArticle>().HasIndex(article => article.IsPublished);
            modelBuilder.Entity<XFLCSMS.Models.Desk.KbArticle>()
                .HasOne<Product>().WithMany().HasForeignKey(article => article.ProductId).OnDelete(DeleteBehavior.SetNull);

            // Tags, links and watchers go when their ticket is deleted. The "other" ticket of a link has no foreign
            // key (SQL Server allows one cascading path per table): CsmsController.DeleteTicket removes those links.
            modelBuilder.Entity<XFLCSMS.Models.Desk.Tag>().HasIndex(tag => tag.Name).IsUnique();
            modelBuilder.Entity<XFLCSMS.Models.Desk.TicketTag>().HasKey(row => new { row.IssueId, row.TagId });
            modelBuilder.Entity<XFLCSMS.Models.Desk.TicketTag>().HasOne(row => row.Issue).WithMany().HasForeignKey(row => row.IssueId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<XFLCSMS.Models.Desk.TicketTag>().HasOne(row => row.Tag).WithMany().HasForeignKey(row => row.TagId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<XFLCSMS.Models.Desk.TicketLink>().HasOne<IssueTable>().WithMany().HasForeignKey(link => link.IssueId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<XFLCSMS.Models.Desk.TicketLink>().HasIndex(link => link.OtherIssueId);
            modelBuilder.Entity<XFLCSMS.Models.Desk.TicketWatcher>().HasKey(row => new { row.IssueId, row.UserId });
            modelBuilder.Entity<XFLCSMS.Models.Desk.TicketWatcher>().HasOne<IssueTable>().WithMany().HasForeignKey(row => row.IssueId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<XFLCSMS.Models.Desk.TicketWatcher>().HasIndex(row => row.UserId);
        }


    }
}
