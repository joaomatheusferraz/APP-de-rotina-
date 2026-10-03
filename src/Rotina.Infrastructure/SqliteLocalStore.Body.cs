using Microsoft.Data.Sqlite;
using Rotina.Domain;

namespace Rotina.Infrastructure;

public sealed partial class SqliteLocalStore
{
    public async Task<IReadOnlyList<BodyMeasurement>> GetMeasurementsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,name,value,unit,measured_at,note,voided_at,corrects_id FROM body_measurement ORDER BY measured_at,id;";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<BodyMeasurement>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new BodyMeasurement(Guid.Parse(reader.GetString(0)), reader.GetString(1), ReadDecimal(reader, 2),
                reader.GetString(3), DateTimeOffset.Parse(reader.GetString(4)), TextOrNull(reader, 5),
                InstantOrNull(reader, 6), GuidOrNull(reader, 7)));
        return result;
    }

    public async Task AddMeasurementAsync(BodyMeasurement measurement, CancellationToken cancellationToken = default)
    {
        if (measurement.VoidedAt is not null || measurement.CorrectsId is not null) throw new ArgumentException("Use a operação de correção para substituir uma medida.");
        if (measurement.MeasuredAt > DateTimeOffset.UtcNow.AddMinutes(1)) throw new ArgumentException("A medida não pode ser registrada no futuro.");
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO body_measurement (id,name,value,unit,measured_at,note,corrects_id) VALUES ($id,$name,$value,$unit,$at,$note,$corrects);";
        BindMeasurement(command, measurement);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CorrectWeightAsync(Guid originalId, BodyWeightRecord replacement, DateTimeOffset correctedAt,
        CancellationToken cancellationToken = default)
    {
        if (replacement.CorrectsId != originalId || replacement.Id == originalId)
            throw new ArgumentException("A correção deve referenciar a pesagem original.");
        if (replacement.VoidedAt is not null) throw new ArgumentException("A nova pesagem não pode estar desconsiderada.");
        if (replacement.MeasuredAt > DateTimeOffset.UtcNow.AddMinutes(1)) throw new ArgumentException("A pesagem não pode ser registrada no futuro.");
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE body_weight_record SET voided_at=$at WHERE id=$id AND voided_at IS NULL;";
        update.Parameters.AddWithValue("$id", originalId.ToString());
        update.Parameters.AddWithValue("$at", correctedAt.ToUniversalTime().ToString("O"));
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("A pesagem já foi corrigida ou não existe.");
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO body_weight_record (id,kilograms,measured_at,note,corrects_id) VALUES ($id,$kg,$at,$note,$corrects);";
        insert.Parameters.AddWithValue("$id", replacement.Id.ToString());
        insert.Parameters.AddWithValue("$kg", DecimalText(replacement.Kilograms));
        insert.Parameters.AddWithValue("$at", replacement.MeasuredAt.ToUniversalTime().ToString("O"));
        insert.Parameters.AddWithValue("$note", Db(replacement.Note));
        insert.Parameters.AddWithValue("$corrects", originalId.ToString());
        await insert.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
    }

    public async Task CorrectMeasurementAsync(Guid originalId, BodyMeasurement replacement, DateTimeOffset correctedAt,
        CancellationToken cancellationToken = default)
    {
        if (replacement.CorrectsId != originalId || replacement.Id == originalId)
            throw new ArgumentException("A correção deve referenciar a medida original.");
        if (replacement.VoidedAt is not null) throw new ArgumentException("A nova medida não pode estar desconsiderada.");
        if (replacement.MeasuredAt > DateTimeOffset.UtcNow.AddMinutes(1)) throw new ArgumentException("A medida não pode ser registrada no futuro.");
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE body_measurement SET voided_at=$at WHERE id=$id AND voided_at IS NULL;";
        update.Parameters.AddWithValue("$id", originalId.ToString());
        update.Parameters.AddWithValue("$at", correctedAt.ToUniversalTime().ToString("O"));
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("A medida já foi corrigida ou não existe.");
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO body_measurement (id,name,value,unit,measured_at,note,corrects_id) VALUES ($id,$name,$value,$unit,$at,$note,$corrects);";
        BindMeasurement(insert, replacement);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
    }

    public Task VoidWeightAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default) =>
        UpdatePlanningAsync("UPDATE body_weight_record SET voided_at=$value WHERE id=$id AND voided_at IS NULL", id,
            at.ToUniversalTime().ToString("O"), cancellationToken);

    public Task VoidMeasurementAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default) =>
        UpdatePlanningAsync("UPDATE body_measurement SET voided_at=$value WHERE id=$id AND voided_at IS NULL", id,
            at.ToUniversalTime().ToString("O"), cancellationToken);

    private static void BindMeasurement(SqliteCommand command, BodyMeasurement item)
    {
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$name", item.Name);
        command.Parameters.AddWithValue("$value", DecimalText(item.Value));
        command.Parameters.AddWithValue("$unit", item.Unit);
        command.Parameters.AddWithValue("$at", item.MeasuredAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$note", Db(item.Note));
        command.Parameters.AddWithValue("$corrects", Db(item.CorrectsId?.ToString()));
    }
}
