using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

namespace ProjectT.Addressable
{
    public sealed class SpriteAtlasResource : Resource<SpriteAtlas>
    {
        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        public Sprite GetSprite(string name)
        {
            var atlas = Asset;
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Sprite name is empty.", nameof(name));

            if (sprites.TryGetValue(name, out var cached) && cached != null)
                return cached;

            var sprite = atlas.GetSprite(name);
            if (sprite != null)
                sprites[name] = sprite;

            return sprite;
        }

        protected override void OnRelease()
        {
            foreach (var sprite in sprites.Values)
                UnityEngine.Object.Destroy(sprite);

            sprites.Clear();
        }
    }
}
