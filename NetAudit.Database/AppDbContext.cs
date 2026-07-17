using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using NetAudit.Core.Interfaces;

namespace NetAudit.Database;

/// <summary>SQLite database context for NetAudit</summary>
public class AppDbContext : DbContext
{
    private readonly string _connectionString;

    public AppDbContext(string dbPath = "")
    {
        _connectionString = string.IsNullOrEmpty(dbPath)
            ? GetDefaultPath()
            : $"Data Source={dbPath}";
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(_connectionString);
        optionsBuilder.EnableSensitiveDataLogging();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Add tables here as needed
        // modelBuilder.Entity<ScanLog>();
        // modelBuilder.Entity<Device>();
    }

    private static string GetDefaultPath()
    {
        string docPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string appPath = Path.Combine(docPath, "NetAudit", "data");
        Directory.CreateDirectory(appPath);
        return $"Data Source={Path.Combine(appPath, "netaudit.db")}";
    }
}
