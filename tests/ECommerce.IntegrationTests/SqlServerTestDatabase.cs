using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace ECommerce.IntegrationTests;

/// <summary>
/// Creates one throwaway SQL Server database for the integration-test run.
/// Testcontainers is preferred; when Docker is unavailable the suite uses a
/// real local SQL Server (or ECOMMERCE_TEST_SQL_CONNECTION). EF InMemory is
/// deliberately never used because it cannot validate migrations or SQL.
/// </summary>
public sealed class SqlServerTestDatabase : IAsyncDisposable
{
    private const string LocalFallbackConnection =
        "Server=.;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connection Timeout=10;MultipleActiveResultSets=true";

    private readonly string _masterConnectionString;
    private readonly IAsyncDisposable? _container;

    private SqlServerTestDatabase(
        string masterConnectionString,
        IAsyncDisposable? container,
        string databaseName,
        string connectionString,
        string provider)
    {
        _masterConnectionString = masterConnectionString;
        _container = container;
        DatabaseName = databaseName;
        ConnectionString = connectionString;
        Provider = provider;
    }

    public string DatabaseName { get; }

    public string ConnectionString { get; }

    public string Provider { get; }

    public static async Task<SqlServerTestDatabase> CreateAsync()
    {
        var (masterConnectionString, container, provider) = await ResolveServerAsync();

        var databaseName = $"ECommerceIntegration_{Guid.NewGuid():N}";
        SqlConnection.ClearAllPools();

        await using (var connection = new SqlConnection(masterConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"IF DB_ID(N'{databaseName}') IS NULL CREATE DATABASE [{databaseName}]";
            await command.ExecuteNonQueryAsync();
        }

        var connectionStringBuilder = new SqlConnectionStringBuilder(masterConnectionString)
        {
            InitialCatalog = databaseName
        };

        return new SqlServerTestDatabase(
            masterConnectionString,
            container,
            databaseName,
            connectionStringBuilder.ConnectionString,
            provider);
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();

        try
        {
            await using var connection = new SqlConnection(_masterConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"IF DB_ID(N'{DatabaseName}') IS NOT NULL BEGIN " +
                $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{DatabaseName}]; END";
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            if (_container is not null)
            {
                await _container.DisposeAsync();
            }
        }
    }

    private static async Task<(string ConnectionString, IAsyncDisposable? Container, string Provider)> ResolveServerAsync()
    {
        var configured = Environment.GetEnvironmentVariable("ECOMMERCE_TEST_SQL_CONNECTION");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return (configured, null, "configured SQL Server");
        }

        MsSqlContainer? container = null;
        try
        {
            // Generated per run: no reusable credential is stored in source.
            var password = $"Aa1!{Guid.NewGuid():N}";

            container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
                .WithPassword(password)
                .Build();

            await container.StartAsync();
            return (container.GetConnectionString(), container, "Testcontainers SQL Server");
        }
        catch (Exception ex) when (LooksLikeDockerUnavailable(ex))
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }

            try
            {
                await using var fallback = new SqlConnection(LocalFallbackConnection);
                await fallback.OpenAsync();
            }
            catch (Exception fallbackException)
            {
                throw new InvalidOperationException(
                    "Docker is unavailable and no local SQL Server fallback could be reached. " +
                    "Start Docker Desktop or set ECOMMERCE_TEST_SQL_CONNECTION to a SQL Server master database.",
                    fallbackException);
            }

            return (LocalFallbackConnection, null, "local SQL Server fallback");
        }
        catch
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }

            throw;
        }
    }

    private static bool LooksLikeDockerUnavailable(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("docker", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("daemon", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("npipe", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("ECONNREFUSED", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("No such host", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
