using System.Text.Json;
using Microsoft.Data.Sqlite;
using Nexus.Abstractions.Automation;
using Nexus.Abstractions.Profiles;
using Nexus.Storage.Database;

namespace Nexus.Storage.Repositories;

public class StorageRepositories
{
    private readonly NexusDatabase _database;

    public StorageRepositories(NexusDatabase database)
    {
        _database = database;
    }

    // Settings
    public async Task SetSettingAsync(string key, string value)
    {
        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Settings (Key, Value, UpdatedAt)
            VALUES (@key, @val, @time)
            ON CONFLICT(Key) DO UPDATE SET Value = @val, UpdatedAt = @time;
        ";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@val", value);
        cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetSettingAsync(string key)
    {
        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Settings WHERE Key = @key;";
        cmd.Parameters.AddWithValue("@key", key);

        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString();
    }

    // Profiles
    public async Task SaveProfileAsync(ProfileData profile)
    {
        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        var json = JsonSerializer.Serialize(profile);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Profiles (Id, Name, Mode, JsonData, IsActive, UpdatedAt)
            VALUES (@id, @name, @mode, @json, @active, @time)
            ON CONFLICT(Id) DO UPDATE SET Name = @name, Mode = @mode, JsonData = @json, IsActive = @active, UpdatedAt = @time;
        ";
        cmd.Parameters.AddWithValue("@id", profile.Id);
        cmd.Parameters.AddWithValue("@name", profile.Name);
        cmd.Parameters.AddWithValue("@mode", (int)profile.Mode);
        cmd.Parameters.AddWithValue("@json", json);
        cmd.Parameters.AddWithValue("@active", profile.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ProfileData>> LoadProfilesAsync()
    {
        var list = new List<ProfileData>();
        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT JsonData FROM Profiles ORDER BY Name ASC;";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var json = reader.GetString(0);
            var p = JsonSerializer.Deserialize<ProfileData>(json);
            if (p != null) list.Add(p);
        }

        return list;
    }

    // Telemetry History
    public async Task LogTelemetryAsync(DateTime timestamp, double? cpuUsage, double? cpuTemp, double? gpuUsage, double? gpuTemp, double? ramUsage)
    {
        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO TelemetryLog (Timestamp, CpuUsage, CpuTemp, GpuUsage, GpuTemp, RamUsage)
            VALUES (@time, @cpuU, @cpuT, @gpuU, @gpuT, @ram);
        ";
        cmd.Parameters.AddWithValue("@time", timestamp.ToString("o"));
        cmd.Parameters.AddWithValue("@cpuU", (object?)cpuUsage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cpuT", (object?)cpuTemp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@gpuU", (object?)gpuUsage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@gpuT", (object?)gpuTemp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ram", (object?)ramUsage ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }
}
