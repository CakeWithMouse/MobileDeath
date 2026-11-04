using System;
using Blinopek.Presentation;
using UnityEngine;

namespace Blinopek.Bootstrap
{
    [Serializable]
    public sealed class LoadingConfig
    {
        public int schema;
        public string scene;
        public string contentKey;
        public bool unloadUnusedAssets;
        public float minimumVisibleSeconds = 1f;
        public void Validate()
        {
            if (schema != 1 || string.IsNullOrWhiteSpace(scene) || string.IsNullOrWhiteSpace(contentKey))
                throw new InvalidOperationException("Некорректный loading.json: нужны schema=1, scene и contentKey.");
            if (float.IsNaN(minimumVisibleSeconds) || float.IsInfinity(minimumVisibleSeconds) || minimumVisibleSeconds < 0)
                throw new InvalidOperationException("minimumVisibleSeconds должен быть конечным неотрицательным числом.");
        }
    }

    [Serializable]
    public sealed class LoadingResource
    {
        public string key;
        public GameContent content;
    }

    [CreateAssetMenu(menuName = "Blinopek/Loading resource catalog")]
    public sealed class LoadingCatalog : ScriptableObject
    {
        public LoadingResource[] resources = Array.Empty<LoadingResource>();
        public GameContent Resolve(string key)
        {
            var keys = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            GameContent found = null;
            foreach (var item in resources)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.key) || !keys.Add(item.key) || item.content == null)
                    throw new InvalidOperationException("Каталог загрузки содержит пустой/повторяющийся ключ или отсутствующий ресурс.");
                if (item.key == key) found = item.content;
            }
            if (found == null) throw new InvalidOperationException("Ресурс загрузки не найден: " + key);
            return found;
        }
    }
}
