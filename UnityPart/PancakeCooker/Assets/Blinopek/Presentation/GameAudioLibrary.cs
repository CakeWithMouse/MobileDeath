using System;
using Blinopek.Application;
using UnityEngine;

namespace Blinopek.Presentation
{
    [Serializable]
    public sealed class SoundEntry
    {
        public SoundCue cue;
        public AudioClip[] clips;
        [Range(0, 1)] public float gain = .7f;
    }

    [CreateAssetMenu(menuName = "Blinopek/Audio library")]
    public sealed class GameAudioLibrary : ScriptableObject
    {
        public SoundEntry[] sounds = new SoundEntry[0];
        public AudioClip frying, batter, butter, jam, honey, cafe, musicDay, musicMenu;
        public SoundEntry Find(SoundCue cue)
        {
            foreach (var entry in sounds) if (entry.cue == cue) return entry;
            return null;
        }
    }
}
