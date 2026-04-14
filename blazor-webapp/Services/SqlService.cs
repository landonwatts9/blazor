using Microsoft.Data.SqlClient;
using System.Data;

namespace SamReporting.Services;

public class SqlService
{
    private readonly string _connectionString;

    public SqlService(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("SamReporting")
            ?? throw new InvalidOperationException("Missing connection string 'SamReporting'");
    }

    public async Task<List<Dictionary<string, object?>>> QueryAsync(
        string sql, params (string name, object? value)[] parameters)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(reader.FieldCount);
            for (int i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            results.Add(row);
        }
        return results;
    }

    public async Task<List<T>> QueryAsync<T>(
        string sql, Func<IDataRecord, T> map, params (string name, object? value)[] parameters)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<T>();
        while (await reader.ReadAsync())
            results.Add(map(reader));
        return results;
    }
}
