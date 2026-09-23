using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GiantWanted.EditorTools
{
    /// <summary>
    /// Wires the clips in Assets/Sounds into the scene's <see cref="GameAudio"/> and builds
    /// the looping music source. Additive and idempotent - it only touches GameAudio and the
    /// Music child it creates, and it does not save the scene for you.
    /// </summary>
    public static class SoundSetup
    {
        const string SoundsFolder = "Assets/Sounds";
        const string MusicChildName = "Music";

        [MenuItem("Tools/Giant Wanted/Assign Sounds")]
        static void AssignSounds()
        {
            GameAudio audio = Object.FindFirstObjectByType<GameAudio>(FindObjectsInactive.Include);
            if (audio == null)
            {
                EditorUtility.DisplayDialog("Giant Wanted",
                    "No GameAudio component found. Open the game scene and try again.", "OK");
                return;
            }

            AudioClip fire = Load("fire");
            AudioClip complete = Load("complete");
            AudioClip enemyDeath = Load("enemydeath");
            AudioClip background = Load("BG");

            Undo.RecordObject(audio, "Assign Sounds");

            if (fire != null) audio.shot = fire;
            if (complete != null) audio.waveComplete = complete;
            if (enemyDeath != null) audio.giantDeath = enemyDeath;
            if (background != null) audio.music = background;

            // Short one-shots want to be decoded up front; a multi-megabyte music track
            // must not be, or it sits in memory fully decompressed.
            SetImport(fire, AudioClipLoadType.DecompressOnLoad, true);
            SetImport(complete, AudioClipLoadType.DecompressOnLoad, true);
            SetImport(enemyDeath, AudioClipLoadType.DecompressOnLoad, true);
            SetImport(background, AudioClipLoadType.Streaming, false);

            AudioSource musicSource = BuildMusicSource(audio, background);
            audio.musicSource = musicSource;

            EditorUtility.SetDirty(audio);
            if (musicSource != null) EditorUtility.SetDirty(musicSource);
            EditorSceneManager.MarkSceneDirty(audio.gameObject.scene);
            Selection.activeGameObject = audio.gameObject;

            Debug.Log("[Giant Wanted] Sounds assigned: " +
                      Describe("fire", fire) + ", " +
                      Describe("complete", complete) + ", " +
                      Describe("enemydeath", enemyDeath) + ", " +
                      Describe("BG", background) +
                      ". Save the scene to keep it.");
        }

        static AudioSource BuildMusicSource(GameAudio audio, AudioClip clip)
        {
            if (clip == null) return audio.musicSource;

            Transform existing = audio.transform.Find(MusicChildName);
            GameObject go;

            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = new GameObject(MusicChildName);
                Undo.RegisterCreatedObjectUndo(go, "Assign Sounds");
                go.transform.SetParent(audio.transform, false);
            }

            AudioSource source = go.GetComponent<AudioSource>();
            if (source == null) source = Undo.AddComponent<AudioSource>(go);
            else Undo.RecordObject(source, "Assign Sounds");

            source.clip = clip;
            source.loop = true;
            source.playOnAwake = true;
            source.spatialBlend = 0f;             // 2D - the track should not fall off with distance
            source.volume = audio.musicVolume;
            source.priority = 0;                  // never voice-stolen by gunfire

            return source;
        }

        static AudioClip Load(string name)
        {
            foreach (string extension in new[] { ".mp3", ".wav", ".ogg", ".aiff", ".aif" })
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SoundsFolder + "/" + name + extension);
                if (clip != null) return clip;
            }

            Debug.LogWarning("[Giant Wanted] No clip named '" + name + "' in " + SoundsFolder + ".");
            return null;
        }

        static void SetImport(AudioClip clip, AudioClipLoadType loadType, bool preload)
        {
            if (clip == null) return;

            string path = AssetDatabase.GetAssetPath(clip);
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return;

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            if (settings.loadType == loadType && settings.preloadAudioData == preload) return;

            settings.loadType = loadType;
            settings.preloadAudioData = preload;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }

        static string Describe(string name, AudioClip clip)
        {
            return name + (clip != null ? " ok" : " MISSING");
        }
    }
}
