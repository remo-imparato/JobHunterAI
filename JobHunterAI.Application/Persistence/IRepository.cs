namespace JobHunterAI.Application.Persistence;

public interface IRepository<TEntity> where TEntity : class
{
	Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
	Task<IReadOnlyList<TEntity>> ListAsync(int skip, int take, CancellationToken cancellationToken = default);
	void Add(TEntity entity);
	void Remove(TEntity entity);
}
