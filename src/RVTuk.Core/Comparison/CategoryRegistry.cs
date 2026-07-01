using System.Collections.Generic;

namespace RVTuk.Core.Comparison
{
    /// <summary>Holds the registered category comparers, keyed by CategoryId. New categories
    /// plug in here without touching the engine.</summary>
    public class CategoryRegistry
    {
        private readonly Dictionary<string, ICategoryComparer> _comparers =
            new Dictionary<string, ICategoryComparer>();

        public void Register(ICategoryComparer comparer)
        {
            _comparers[comparer.CategoryId] = comparer;
        }

        public bool TryGet(string categoryId, out ICategoryComparer comparer)
        {
            return _comparers.TryGetValue(categoryId, out comparer!);
        }

        public IEnumerable<ICategoryComparer> All => _comparers.Values;
    }
}
