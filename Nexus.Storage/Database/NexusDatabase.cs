using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Nexus.Storage.Database;

public class NexusDatabase : IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<NexusDatabase>? _logger;
    private bool _initialized;

    public NexusDatabase(string? customDbPath = null, ILogger<NexusDatabase>? logger = null)
    {
        _logger = logger;
        string dbPath;
        if (!string.IsNullOrEmpty(customDbPath))
        {
            dbPath = customDbPath;
        }
        else
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var nexusDir = Path.Combine(appData, "NEXUS");
                Directory.CreateDirectory(nexusDir);
                dbPath = Path.Combine(nexusDir, "nexus.db");
            }
            catch
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var fallbackDir = Path.Combine(baseDir, "Data");
                Directory.CreateDirectory(fallbackDir);
                dbPath = Path.Combine(fallbackDir, "nexus.db");
            }
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Enable Write-Ahead Logging (WAL) for smooth concurrent read/write between App & Background Service
        using var walCmd = connection.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
        await walCmd.ExecuteNonQueryAsync();

        // Create Tables
        const string schema = @"
            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Profiles (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Mode INTEGER NOT NULL,
                JsonData TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AutomationRules (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                JsonData TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS DeviceConfigs (
                DeviceId TEXT PRIMARY KEY,
                Nickname TEXT,
                CustomPropertiesJson TEXT,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS TelemetryLog (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                CpuUsage REAL,
                CpuTemp REAL,
                GpuUsage REAL,
                GpuTemp REAL,
                RamUsage REAL
            );

            CREATE INDEX IF NOT EXISTS idx_telemetry_timestamp ON TelemetryLog(Timestamp);
        ";

        using var schemaCmd = connection.CreateCommand();
        schemaCmd.CommandText = schema;
        await schemaCmd.ExecuteNonQueryAsync();

        _initialized = true;
        _logger?.LogInformation("SQLite database initialized successfully.");
    }

    public SqliteConnection CreateConnection()
    {
        return new SqliteConnection(_connectionString);
    }

    public void Dispose()
    {
        // SqliteConnection handles connection pools automatically
    }
}
