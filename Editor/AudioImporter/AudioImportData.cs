using System;
using UnityEngine;

namespace studio.tgs.audioimporter.editor
{
    [Serializable]
    public class AudioImportData
    {
        public AudioClip AudioClip;
        public string AudioSourcePath;
        public string AudioName;
        public string AudioFolder;
        public AudioConfigPreset ConfigPreset = AudioConfigPreset.SFX2D;

        public bool IsValid()
        {
            return AudioClip != null &&
                   !string.IsNullOrWhiteSpace(AudioName) &&
                   !string.IsNullOrWhiteSpace(AudioFolder);
        }
    }

    [Serializable]
    public enum AudioConfigPreset
    {
        SFX2D,
        Music
    }
}
