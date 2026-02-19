using Microsoft.EntityFrameworkCore;
using SensorPal.Server.Entities;

namespace SensorPal.Server.Storage;

sealed class SensorPalDbContext(DbContextOptions<SensorPalDbContext> options) : DbContext(options)
{
    public DbSet<MonitoringSession> MonitoringSessions => Set<MonitoringSession>();
    public DbSet<NoiseEvent> NoiseEvents => Set<NoiseEvent>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
}
