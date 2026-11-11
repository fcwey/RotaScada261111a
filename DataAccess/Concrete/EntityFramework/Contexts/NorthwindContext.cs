using Core.Entities.Concrete;
using Entities.Concrete;
using Microsoft.EntityFrameworkCore;
using System;
using System.Text.Json;

namespace DataAccess.Concrete.EntityFramework.Contexts
{
    public class NorthwindContext : DbContext
    {
        // Uygulama açılışında appsettings içindeki ConnectionStrings:Default değerinden doldurulur.
        public static string? ConnectionString { get; set; }

        // Hedef MySQL sürümü (8.0 ve üstü, 8.4 LTS dahil)
        private static readonly MySqlServerVersion MySqlVersion = new MySqlServerVersion(new Version(8, 0, 0));

        // Veritabanı bağlantısını MySQL için yapılandırır.
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (string.IsNullOrWhiteSpace(ConnectionString))
                throw new InvalidOperationException("Veritabanı bağlantısı tanımlı değil. appsettings içinde ConnectionStrings:Default değerini girin.");

            optionsBuilder.UseMySql(ConnectionString, MySqlVersion);
            base.OnConfiguring(optionsBuilder);
        }


        // SCADA Tabloları
        public DbSet<Alarm> Alarms { get; set; }
        public DbSet<Recipe> Recipes { get; set; }
        public DbSet<RecipeStep> RecipeSteps { get; set; } //  (20 Adımlı Reçete Detayları)
        public DbSet<Batch> Batches { get; set; }
        public DbSet<ProcessDataLog> ProcessDataLogs { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }

        // Kimlik Tabloları
        public DbSet<User> Users { get; set; }
        public DbSet<OperationClaim> OperationClaims { get; set; }
        public DbSet<UserOperationClaim> UserOperationClaims { get; set; }

        // Tablo ilişkilerini, indeksleri ve kısıtları tanımlar.
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 1. ProcessDataLog Index Yapılandırması (Historian Hızlı Trend Sorguları İçin)
            modelBuilder.Entity<ProcessDataLog>()
                .HasIndex(p => p.Timestamp)
                .HasDatabaseName("IX_ProcessDataLog_Timestamp");

            modelBuilder.Entity<ProcessDataLog>()
                .HasIndex(p => p.BatchId)
                .HasDatabaseName("IX_ProcessDataLog_BatchId");

            // 2. Reçete ve Adım (1 - N) İlişki Yapılandırması
            modelBuilder.Entity<RecipeStep>()
                .HasOne(s => s.Recipe)
                .WithMany(r => r.Steps)
                .HasForeignKey(s => s.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);

            // 3. Reçete İsim Uzunluk Kısıtı
            modelBuilder.Entity<Recipe>()
                .Property(r => r.RecipeName)
                .HasMaxLength(30)
                .IsRequired();

            // 4. Şarj durumu: indekslenebilmesi için uzunluk sınırı verilir (RUNNING, COMPLETED, ABORTED)
            modelBuilder.Entity<Batch>()
                .Property(b => b.Status)
                .HasMaxLength(20)
                .IsRequired();

            modelBuilder.Entity<Batch>()
                .HasIndex(b => b.Status)
                .HasDatabaseName("IX_Batch_Status");
        }

        // Kaydetmeden önce değişiklikleri toplar, kaydettikten sonra audit log yazar.
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var auditEntries = OnBeforeSaveChanges();
            var result = await base.SaveChangesAsync(cancellationToken);
            await OnAfterSaveChangesAsync(auditEntries);
            return result;
        }

        // Değişen kayıtların eski/yeni değerlerini audit kaydı için hazırlar.
        private List<AuditEntry> OnBeforeSaveChanges()
        {
            ChangeTracker.DetectChanges();
            var auditEntries = new List<AuditEntry>();

            foreach (var entry in ChangeTracker.Entries())
            {
                // AuditLog tablosunun kendisi, historian ölçüm kayıtları ve değişmeyen veriler loglanmaz.
                if (entry.Entity is AuditLog || entry.Entity is ProcessDataLog ||
                    entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
                    continue;

                var auditEntry = new AuditEntry
                {
                    TableName = entry.Entity.GetType().Name,
                    ActionType = entry.State.ToString()
                };

                foreach (var property in entry.Properties)
                {
                    if (property.IsTemporary) continue;

                    string propertyName = property.Metadata.Name;
                    if (property.Metadata.IsPrimaryKey())
                    {
                        auditEntry.KeyValues[propertyName] = property.CurrentValue;
                        continue;
                    }

                    switch (entry.State)
                    {
                        case EntityState.Added:
                            auditEntry.NewValues[propertyName] = property.CurrentValue;
                            break;

                        case EntityState.Deleted:
                            auditEntry.OldValues[propertyName] = property.OriginalValue;
                            break;

                        case EntityState.Modified:
                            if (property.IsModified)
                            {
                                auditEntry.OldValues[propertyName] = property.OriginalValue;
                                auditEntry.NewValues[propertyName] = property.CurrentValue;
                            }
                            break;
                    }
                }
                auditEntries.Add(auditEntry);
            }
            return auditEntries;
        }

        // Hazırlanan audit kayıtlarını AuditLogs tablosuna yazar.
        private async Task OnAfterSaveChangesAsync(List<AuditEntry> auditEntries)
        {
            if (auditEntries == null || auditEntries.Count == 0) return;

            foreach (var auditEntry in auditEntries)
            {
                AuditLogs.Add(new AuditLog
                {
                    ActionType = auditEntry.ActionType,
                    ModuleName = auditEntry.TableName,
                    Description = $"{auditEntry.TableName} tablosunda {auditEntry.ActionType} işlemi yapıldı.",
                    OldValues = auditEntry.OldValues.Count == 0 ? null : JsonSerializer.Serialize(auditEntry.OldValues),
                    NewValues = auditEntry.NewValues.Count == 0 ? null : JsonSerializer.Serialize(auditEntry.NewValues),
                    Timestamp = DateTime.Now,
                    IsSuccess = true
                });
            }
            await base.SaveChangesAsync();
        }
    }

    // Audit işlemi için yardımcı veri taşıyıcı sınıf
    internal class AuditEntry
    {
        public string TableName { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public Dictionary<string, object?> KeyValues { get; } = new();
        public Dictionary<string, object?> OldValues { get; } = new();
        public Dictionary<string, object?> NewValues { get; } = new();
    }
}