using JobHunterAI.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobHunterAI.Infrastructure;

internal sealed class EfRepository<TEntity>(JobHunterDbContext context) : IRepository<TEntity> where TEntity : class
{
    public Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id must not be empty.", nameof(id));
        }

        return context.Set<TEntity>().AsSplitQuery()
            .SingleOrDefaultAsync(entity => EF.Property<Guid>(entity, "Id") == id, cancellationToken);
    }

    public async Task<IReadOnlyList<TEntity>> ListAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        if (take is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(take), "Page size must be between 1 and 100.");
        }

        return await context.Set<TEntity>().AsSplitQuery()
            .OrderBy(entity => EF.Property<Guid>(entity, "Id"))
            .Skip(skip).Take(take).ToListAsync(cancellationToken);
    }

    public void Add(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        context.Set<TEntity>().Add(entity);
    }

    public void Remove(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        context.Set<TEntity>().Remove(entity);
    }
}
