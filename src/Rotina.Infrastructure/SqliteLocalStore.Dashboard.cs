using Microsoft.Data.Sqlite;
using Rotina.Domain;

namespace Rotina.Infrastructure;

public sealed partial class SqliteLocalStore
{
    public async Task<IReadOnlyList<DashboardCardSetting>> GetDashboardSettingsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key,sort_order,is_visible FROM dashboard_card_setting ORDER BY sort_order,key;";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<DashboardCardSetting>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetBoolean(2)));
        return result;
    }

    public async Task SaveDashboardSettingsAsync(IReadOnlyList<DashboardCardSetting> settings, CancellationToken cancellationToken = default)
    {
        if (settings.Count == 0 || settings.Count > 20) throw new ArgumentException("O painel precisa ter cartões válidos.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var setting in settings) { setting.Validate(); if (!keys.Add(setting.Key)) throw new ArgumentException("Não é possível repetir cartões."); }
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        using var delete = connection.CreateCommand(); delete.Transaction = transaction; delete.CommandText = "DELETE FROM dashboard_card_setting;"; await delete.ExecuteNonQueryAsync(cancellationToken);
        for (var i = 0; i < settings.Count; i++)
        {
            using var insert = connection.CreateCommand(); insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO dashboard_card_setting(key,sort_order,is_visible) VALUES($key,$order,$visible);";
            insert.Parameters.AddWithValue("$key", settings[i].Key); insert.Parameters.AddWithValue("$order", i); insert.Parameters.AddWithValue("$visible", settings[i].IsVisible ? 1 : 0);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        transaction.Commit();
    }
}
