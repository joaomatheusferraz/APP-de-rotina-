using Rotina.Domain;

namespace Rotina.Application;

public interface IBodyStore
{
    Task<IReadOnlyList<BodyMeasurement>> GetMeasurementsAsync(CancellationToken cancellationToken = default);
    Task AddMeasurementAsync(BodyMeasurement measurement, CancellationToken cancellationToken = default);
    Task CorrectWeightAsync(Guid originalId, BodyWeightRecord replacement, DateTimeOffset correctedAt, CancellationToken cancellationToken = default);
    Task CorrectMeasurementAsync(Guid originalId, BodyMeasurement replacement, DateTimeOffset correctedAt, CancellationToken cancellationToken = default);
    Task VoidWeightAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default);
    Task VoidMeasurementAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default);
}
