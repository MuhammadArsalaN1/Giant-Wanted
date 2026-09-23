using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GiantWanted.EditorTools
{
    /// <summary>
    /// One-click scene assembly. Takes the four art assets that already live in the scene
    /// (env / gun / bullet / monster) and wires a complete, playable wave shooter around them:
    /// player rig, pooled bullets, giant prefab with hit boxes, HUD, spawner and game flow.
    ///
    /// Safe to run repeatedly - it tears down what it built last time first.
    /// </summary>
    public class GiantWantedBuilder : EditorWindow
    {
        // --- source art -------------------------------------------------------
        GameObject _envSource;
        GameObject _gunSource;
        GameObject _bulletSource;
        GameObject _monsterSource;
        RuntimeAnimatorController _monsterController;

        // --- tuning -----------------------------------------------------------
        bool _autoScale = true;
        float _giantHeight = 20f;
        float _hoverHeight = 22f;
        float _spawnRadius = 220f;
        float _spawnHeight = 60f;
        float _gunLength = 0.85f;
        float _bulletLength = 1.4f;
        int _waveCount = 8;
        bool _endless = true;
        bool _addEnvironmentColliders = true;
        bool _destroySourceObjects = true;

        Vector2 _scroll;
        string _report = "";

        const string PlayerName = "Player";
        const string SystemsName = "GameSystems";
        const string CanvasName = "GameCanvas";
        const string EventSystemName = "EventSystem";
        const string CityTargetName = "CityTarget";

        [MenuItem("Tools/Giant Wanted/Build Game Scene...")]
        static void Open()
        {
            GiantWantedBuilder window = GetWindow<GiantWantedBuilder>(true, "Giant Wanted - Scene Builder");
            window.minSize = new Vector2(430f, 620f);
            window.AutoFill();
            window.Show();
        }

        /// <summary>Same build, no dialog - the fastest path from "scripts imported" to "press Play".</summary>
        [MenuItem("Tools/Giant Wanted/Build Now (defaults)")]
        static void BuildNow()
        {
            GiantWantedBuilder builder = CreateInstance<GiantWantedBuilder>();
            builder.AutoFill();

            if (!builder.Ready())
            {
                EditorUtility.DisplayDialog("Giant Wanted",
                    "Could not find the env / gun / bullet / monster objects.\n\n" +
                    "Open the scene that contains them, or use Tools > Giant Wanted > Build Game Scene... " +
                    "and assign them by hand.", "OK");
                DestroyImmediate(builder);
                return;
            }

            builder.Build();
            DestroyImmediate(builder);
        }

        void OnEnable()
        {
            AutoFill();
        }

        // ------------------------------------------------------------------ GUI

        void AutoFill()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return;

            if (_envSource == null) _envSource = BuildUtils.FindRoot(scene, "env") ?? Load("Assets/env.prefab");
            if (_gunSource == null) _gunSource = FindSource(scene, "gun", "Assets/Models/gun.fbx");
            if (_bulletSource == null) _bulletSource = FindSource(scene, "bullet", "Assets/Models/bullet.fbx");
            if (_monsterSource == null) _monsterSource = FindSource(scene, "monster", "Assets/Models/monster.fbx");

            if (_monsterController == null)
            {
                foreach (string guid in AssetDatabase.FindAssets("t:AnimatorController"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.ToLowerInvariant().Contains("monster")) continue;
                    _monsterController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
                    break;
                }
            }

            if (_autoScale) ComputeAutoValues();
        }

        static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

        static GameObject FindSource(Scene scene, string name, string fallbackAssetPath)
        {
            GameObject inScene = BuildUtils.FindRoot(scene, name);
            return inScene != null ? inScene : Load(fallbackAssetPath);
        }

        void ComputeAutoValues()
        {
            if (_envSource == null) return;
            if (!TryGetSourceBounds(_envSource, out Bounds env)) return;

            Vector3 size = env.size;
            float footprint = Mathf.Max(size.x, size.z);

            _giantHeight = Mathf.Max(2f, size.y * 0.45f);
            _hoverHeight = Mathf.Max(3f, size.y * 0.55f);
            _spawnRadius = Mathf.Max(20f, footprint * 0.7f);
            _spawnHeight = Mathf.Max(5f, size.y * 0.8f);
            _bulletLength = Mathf.Max(0.2f, _giantHeight * 0.06f);
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Source art", EditorStyles.boldLabel);
            _envSource = (GameObject)EditorGUILayout.ObjectField("Environment", _envSource, typeof(GameObject), true);
            _gunSource = (GameObject)EditorGUILayout.ObjectField("Gun", _gunSource, typeof(GameObject), true);
            _bulletSource = (GameObject)EditorGUILayout.ObjectField("Bullet", _bulletSource, typeof(GameObject), true);
            _monsterSource = (GameObject)EditorGUILayout.ObjectField("Monster", _monsterSource, typeof(GameObject), true);
            _monsterController = (RuntimeAnimatorController)EditorGUILayout.ObjectField(
                "Monster animator", _monsterController, typeof(RuntimeAnimatorController), false);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("World scale", EditorStyles.boldLabel);

            bool auto = EditorGUILayout.Toggle("Auto from environment", _autoScale);
            if (auto != _autoScale)
            {
                _autoScale = auto;
                if (_autoScale) ComputeAutoValues();
            }

            using (new EditorGUI.DisabledScope(_autoScale))
            {
                _giantHeight = EditorGUILayout.FloatField("Giant height", _giantHeight);
                _hoverHeight = EditorGUILayout.FloatField("Giant hover height", _hoverHeight);
                _spawnRadius = EditorGUILayout.FloatField("Spawn radius", _spawnRadius);
                _spawnHeight = EditorGUILayout.FloatField("Spawn height", _spawnHeight);
                _bulletLength = EditorGUILayout.FloatField("Bullet length", _bulletLength);
            }

            _gunLength = EditorGUILayout.FloatField("Gun length (view model)", _gunLength);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Run", EditorStyles.boldLabel);
            _waveCount = EditorGUILayout.IntSlider("Designed waves", _waveCount, 1, 30);
            _endless = EditorGUILayout.Toggle("Endless after last wave", _endless);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
            _addEnvironmentColliders = EditorGUILayout.Toggle("Add environment colliders", _addEnvironmentColliders);
            _destroySourceObjects = EditorGUILayout.Toggle("Remove loose source objects", _destroySourceObjects);

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(!Ready()))
            {
                if (GUILayout.Button("Build Game Scene", GUILayout.Height(40f))) Build();
            }

            if (!Ready())
                EditorGUILayout.HelpBox("Assign the environment, gun, bullet and monster before building.",
                                        MessageType.Warning);

            if (!string.IsNullOrEmpty(_report))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Last build", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(_report, MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        bool Ready()
        {
            return _envSource != null && _gunSource != null && _bulletSource != null && _monsterSource != null;
        }

        // ---------------------------------------------------------------- build

        void Build()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                EditorUtility.DisplayDialog("Giant Wanted", "Open a scene first.", "OK");
                return;
            }

            if (_autoScale) ComputeAutoValues();

            BuildUtils.EnsureFolder(BuildUtils.PrefabFolder);
            BuildUtils.EnsureFolder(BuildUtils.GeneratedArtFolder);

            int giantLayer = BuildUtils.EnsureLayer("Giant");
            int projectileLayer = BuildUtils.EnsureLayer("Projectile");
            int environmentLayer = BuildUtils.EnsureLayer("Environment");
            LayerMask bulletMask = (1 << giantLayer) | (1 << environmentLayer);

            CleanPreviousBuild(scene);

            // A rebuild destroys the previous rig, which may have owned the gun we were
            // pointing at. Fall back to the source assets for anything that just vanished.
            if (_gunSource == null) _gunSource = Load("Assets/Models/gun.fbx");
            if (_bulletSource == null) _bulletSource = Load("Assets/Models/bullet.fbx");
            if (_monsterSource == null) _monsterSource = Load("Assets/Models/monster.fbx");
            if (_envSource == null) _envSource = BuildUtils.FindRoot(scene, "env") ?? Load("Assets/env.prefab");

            if (!Ready())
            {
                EditorUtility.DisplayDialog("Giant Wanted",
                    "Could not resolve the source art (gun / bullet / monster / env). Assign them by hand and build again.",
                    "OK");
                return;
            }

            // --- art ---------------------------------------------------------
            Sprite white = BuildUtils.CreateSprite("gw_white", 8, BuildUtils.SolidWhite);
            Sprite circle = BuildUtils.CreateSprite("gw_circle", 128, BuildUtils.Circle);
            Sprite ring = BuildUtils.CreateSprite("gw_ring", 128, BuildUtils.Ring);
            Sprite crosshair = BuildUtils.CreateSprite("gw_crosshair", 128, BuildUtils.Crosshair);
            Sprite glowSprite = BuildUtils.CreateSprite("gw_glow", 128, BuildUtils.SoftGlow);

            Texture glowTexture = glowSprite != null ? glowSprite.texture : null;
            Material fxMaterial = BuildUtils.CreateAdditiveMaterial("gw_fx_additive", glowTexture, Color.white);

            float fxScale = Mathf.Max(0.05f, _giantHeight * 0.05f);

            ParticleSystem impactFx = BuildUtils.CreateBurstPrefab(new BuildUtils.BurstSpec
            {
                name = "FX_Impact", material = fxMaterial,
                startColor = new Color(1f, 0.85f, 0.5f, 1f),
                burstCount = 10, lifetime = 0.35f,
                speed = fxScale * 12f, size = fxScale * 1.4f,
                coneAngle = 35f, gravity = 0.3f
            }).GetComponent<ParticleSystem>();

            ParticleSystem bloodFx = BuildUtils.CreateBurstPrefab(new BuildUtils.BurstSpec
            {
                name = "FX_Hit", material = fxMaterial,
                startColor = new Color(1f, 0.25f, 0.18f, 1f),
                burstCount = 16, lifetime = 0.45f,
                speed = fxScale * 16f, size = fxScale * 2.2f,
                coneAngle = 45f, gravity = 0.15f
            }).GetComponent<ParticleSystem>();

            ParticleSystem muzzleFx = BuildUtils.CreateBurstPrefab(new BuildUtils.BurstSpec
            {
                name = "FX_MuzzleFlash", material = fxMaterial,
                startColor = new Color(1f, 0.9f, 0.55f, 1f),
                burstCount = 6, lifetime = 0.08f,
                speed = _gunLength * 2.5f, size = _gunLength * 0.5f,
                coneAngle = 18f, gravity = 0f
            }).GetComponent<ParticleSystem>();

            DamagePopup popupPrefab = BuildDamagePopupPrefab();

            // --- prefabs -----------------------------------------------------
            Projectile bulletPrefab = BuildBulletPrefab(fxMaterial, projectileLayer, bulletMask);
            Giant giantPrefab = BuildGiantPrefab(white, giantLayer, out float giantScale);

            // --- scene -------------------------------------------------------
            Bounds envBounds;
            bool hasEnv = TryGetSourceBounds(_envSource, out envBounds);
            Vector3 cityCenter = hasEnv
                ? new Vector3(envBounds.center.x, envBounds.min.y, envBounds.center.z)
                : Vector3.zero;

            Camera camera = FindSceneCamera(scene);
            Vector3 playerPosition = camera != null ? camera.transform.position : cityCenter + new Vector3(0f, 20f, -60f);
            float playerYaw = camera != null ? camera.transform.eulerAngles.y : 0f;
            float playerPitch = camera != null ? NormalizeAngle(camera.transform.eulerAngles.x) : 8f;

            GameObject player = BuildPlayerRig(camera, playerPosition, playerYaw, playerPitch,
                                               muzzleFx, bulletPrefab, bulletMask, projectileLayer,
                                               out PlayerAim aim, out WeaponController weapon);

            GameObject cityTarget = new GameObject(CityTargetName);
            cityTarget.transform.position = cityCenter;
            Vector3 toCity = cityCenter - playerPosition;
            toCity.y = 0f;
            if (toCity.sqrMagnitude < 0.01f) toCity = Vector3.forward;
            cityTarget.transform.rotation = Quaternion.LookRotation(toCity.normalized, Vector3.up);

            GameObject systems = new GameObject(SystemsName);

            WaveSpawner spawner = systems.AddComponent<WaveSpawner>();
            spawner.giantPrefab = giantPrefab;
            spawner.target = cityTarget.transform;
            spawner.spawnRadius = _spawnRadius;
            spawner.spawnRadiusJitter = _spawnRadius * 0.2f;
            spawner.spawnHeight = _spawnHeight;
            spawner.spawnHeightJitter = _spawnHeight * 0.3f;
            spawner.spawnArc = 70f;
            spawner.prewarm = 4;

            FxPool fx = systems.AddComponent<FxPool>();
            fx.impactPrefab = impactFx;
            fx.bloodPrefab = bloodFx;
            fx.popupPrefab = popupPrefab;

            GameManager game = systems.AddComponent<GameManager>();
            game.spawner = spawner;
            game.weapon = weapon;
            game.cityMaxHealth = 100;
            game.waves = MakeWaves(_waveCount);
            game.endless = _endless;

            // Silent until clips are dropped in, but already wired to every event.
            GameAudio audio = systems.AddComponent<GameAudio>();
            audio.game = game;
            audio.weapon = weapon;

            BuildCanvas(game, weapon, aim, white, circle, ring, crosshair);
            EnsureEventSystem(scene);

            if (_addEnvironmentColliders) AddEnvironmentColliders(environmentLayer);

            if (_destroySourceObjects) RemoveLooseSources(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            _report =
                "Environment: " + (hasEnv ? envBounds.size.ToString("F1") : "not measured") + "\n" +
                "City centre: " + cityCenter.ToString("F1") + "\n" +
                "Player: " + playerPosition.ToString("F1") + "\n" +
                "Giant height " + _giantHeight.ToString("F1") + " (prefab scale " + giantScale.ToString("F3") + ")\n" +
                "Spawn ring " + _spawnRadius.ToString("F0") + " @ height " + _spawnHeight.ToString("F0") + "\n" +
                "Waves: " + _waveCount + (_endless ? " then endless" : "");

            Debug.Log("[Giant Wanted] Scene built.\n" + _report);
            Selection.activeGameObject = player;
        }

        static float NormalizeAngle(float angle) => angle > 180f ? angle - 360f : angle;

        void CleanPreviousBuild(Scene scene)
        {
            BuildUtils.DestroyIfExists(
                BuildUtils.FindRoot(scene, PlayerName),
                BuildUtils.FindRoot(scene, SystemsName),
                BuildUtils.FindRoot(scene, CanvasName),
                BuildUtils.FindRoot(scene, CityTargetName));
        }

        void RemoveLooseSources(Scene scene)
        {
            // The gun was re-parented into the player rig; bullet and monster became prefabs.
            foreach (string name in new[] { "bullet", "monster" })
            {
                GameObject go = BuildUtils.FindRoot(scene, name);
                if (go != null) Object.DestroyImmediate(go);
            }

            GameObject looseGun = BuildUtils.FindRoot(scene, "gun");
            if (looseGun != null) Object.DestroyImmediate(looseGun);
        }

        static Camera FindSceneCamera(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Camera camera = root.GetComponentInChildren<Camera>(true);
                if (camera != null) return camera;
            }
            return null;
        }

        static bool TryGetSourceBounds(GameObject source, out Bounds bounds)
        {
            bool temporary = !source.scene.IsValid();
            GameObject probe = temporary ? Object.Instantiate(source) : source;

            bool ok = BuildUtils.TryGetModelBounds(probe, out bounds);

            if (temporary) Object.DestroyImmediate(probe);
            return ok;
        }

        // ------------------------------------------------------------- prefabs

        /// <summary>Clone a source (scene object or asset) into the scene, fully unpacked.</summary>
        static GameObject CloneSource(GameObject source)
        {
            GameObject clone = Object.Instantiate(source);
            clone.hideFlags = HideFlags.None;
            clone.SetActive(true);

            if (PrefabUtility.IsPartOfPrefabInstance(clone))
                PrefabUtility.UnpackPrefabInstance(clone, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            clone.transform.SetParent(null, false);
            clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            clone.transform.localScale = Vector3.one;
            return clone;
        }

        /// <summary>Rotate a model so its longest axis points down +Z, and centre it on its parent.</summary>
        static void OrientAlongForward(GameObject model, float targetLength)
        {
            if (model.transform.parent == null) return;

            model.transform.localRotation = Quaternion.identity;
            model.transform.localPosition = Vector3.zero;
            model.transform.localScale = Vector3.one;

            if (!BuildUtils.TryGetLocalBounds(model, out Bounds local)) return;

            int axis = BuildUtils.LongestAxis(local.size);
            if (axis == 0) model.transform.localRotation = Quaternion.FromToRotation(Vector3.right, Vector3.forward);
            else if (axis == 1) model.transform.localRotation = Quaternion.FromToRotation(Vector3.up, Vector3.forward);

            // Re-measure in the parent's space now that the model is turned.
            if (!BuildUtils.TryGetLocalBounds(model.transform.parent.gameObject, out Bounds parentSpace)) return;

            float length = Mathf.Max(0.0001f, parentSpace.size.z);
            float scale = targetLength / length;
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = -parentSpace.center * scale;
        }

        Projectile BuildBulletPrefab(Material tracerMaterial, int layer, LayerMask hitMask)
        {
            GameObject root = new GameObject("Bullet");
            GameObject model = CloneSource(_bulletSource);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);

            OrientAlongForward(model, _bulletLength);

            TrailRenderer trail = root.AddComponent<TrailRenderer>();
            trail.time = 0.09f;
            trail.startWidth = _bulletLength * 0.30f;
            trail.endWidth = 0f;
            trail.minVertexDistance = _bulletLength * 0.2f;
            trail.sharedMaterial = tracerMaterial;
            trail.numCapVertices = 2;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.autodestruct = false;

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.92f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.5f, 0.15f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;

            Projectile projectile = root.AddComponent<Projectile>();
            projectile.model = model.transform;
            projectile.hitMask = hitMask;
            projectile.maxLifetime = 3f;

            BuildUtils.SetLayerRecursively(root, layer);

            string path = BuildUtils.PrefabFolder + "/Bullet.prefab";
            AssetDatabase.DeleteAsset(path);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            return prefab.GetComponent<Projectile>();
        }

        Giant BuildGiantPrefab(Sprite barSprite, int layer, out float giantScale)
        {
            GameObject root = CloneSource(_monsterSource);
            root.name = "Giant";

            // Measure at scale 1 so every derived number stays in the prefab's local space.
            BuildUtils.TryGetLocalBounds(root, out Bounds local);
            float rawHeight = Mathf.Max(0.001f, local.size.y);
            giantScale = _giantHeight / rawHeight;
            root.transform.localScale = Vector3.one * giantScale;

            Animator animator = root.GetComponent<Animator>();
            if (animator == null) animator = root.AddComponent<Animator>();
            if (_monsterController != null) animator.runtimeAnimatorController = _monsterController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Centre marker: where damage numbers pop and the health bar tracks.
            GameObject center = new GameObject("Center");
            center.transform.SetParent(root.transform, false);
            center.transform.localPosition = local.center;

            // Body hit box - a capsule roughly matching the torso.
            GameObject body = new GameObject("BodyHitBox");
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = local.center;
            CapsuleCollider bodyCollider = body.AddComponent<CapsuleCollider>();
            bodyCollider.direction = 1;
            bodyCollider.height = local.size.y * 0.9f;
            bodyCollider.radius = Mathf.Max(local.size.x, local.size.z) * 0.3f;

            Giant giant = root.GetComponent<Giant>();
            if (giant == null) giant = root.AddComponent<Giant>();

            HitBox bodyHit = body.AddComponent<HitBox>();
            bodyHit.owner = giant;
            bodyHit.damageMultiplier = 1f;
            bodyHit.isWeakPoint = false;

            // Weak point - the head, if the rig has one we can find by name.
            Transform head = FindHeadBone(root.transform);
            GameObject weak = new GameObject("HeadHitBox");
            if (head != null)
            {
                weak.transform.SetParent(head, false);
                float boneScale = SafeRatio(head.lossyScale.x, root.transform.lossyScale.x);
                SphereCollider sphere = weak.AddComponent<SphereCollider>();
                sphere.radius = (local.size.y * 0.09f) / Mathf.Max(0.0001f, boneScale);
            }
            else
            {
                weak.transform.SetParent(root.transform, false);
                weak.transform.localPosition = new Vector3(local.center.x, local.max.y - local.size.y * 0.1f, local.center.z);
                SphereCollider sphere = weak.AddComponent<SphereCollider>();
                sphere.radius = local.size.y * 0.1f;
            }

            HitBox weakHit = weak.AddComponent<HitBox>();
            weakHit.owner = giant;
            weakHit.damageMultiplier = 3f;
            weakHit.isWeakPoint = true;

            // Floating health bar.
            WorldHealthBar healthBar = BuildHealthBar(root.transform, barSprite, giantScale);
            healthBar.anchor = center.transform;
            healthBar.worldOffset = new Vector3(0f, _giantHeight * 0.62f, 0f);
            healthBar.referenceDistance = Mathf.Max(10f, _spawnRadius * 0.35f);

            // Stats sized against the world we measured.
            giant.animator = animator;
            giant.healthBar = healthBar;
            giant.centerPoint = center.transform;
            giant.hitColliders = new Collider[] { bodyCollider, weak.GetComponent<Collider>() };
            giant.maxHealth = 260f;
            // Aim for roughly nine seconds from the spawn ring to the city on wave one.
            giant.moveSpeed = Mathf.Max(2f, _spawnRadius / 9f);
            giant.attackRange = Mathf.Max(4f, _giantHeight * 1.6f);
            giant.attackInterval = 2.6f;
            giant.attackDamage = 9;
            giant.hoverHeight = _hoverHeight;
            giant.hoverAmplitude = _giantHeight * 0.07f;
            giant.hoverFrequency = 0.55f;
            giant.lungeDistance = _giantHeight * 0.35f;
            giant.deathSinkSpeed = _giantHeight * 0.25f;
            giant.scoreValue = 100;
            giant.coinValue = 10;

            ReadAnimatorStateNames(out string flyState, out string dieState);
            giant.flyState = flyState;
            giant.dieState = dieState;

            BuildUtils.SetLayerRecursively(root, layer);

            string path = BuildUtils.PrefabFolder + "/Giant.prefab";
            AssetDatabase.DeleteAsset(path);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            return prefab.GetComponent<Giant>();
        }

        static float SafeRatio(float a, float b) => Mathf.Approximately(b, 0f) ? 1f : a / b;

        static Transform FindHeadBone(Transform root)
        {
            Transform best = null;
            float bestHeight = float.NegativeInfinity;

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string name = t.name.ToLowerInvariant();
                if (!name.Contains("head") || name.Contains("headgear")) continue;

                // Several rigs have head / head_end / headtop - take the lowest one, which is the skull.
                float height = t.position.y;
                if (best == null || height < bestHeight)
                {
                    best = t;
                    bestHeight = height;
                }
            }

            return best;
        }

        void ReadAnimatorStateNames(out string flyState, out string dieState)
        {
            flyState = "Fly";
            dieState = "die";

            AnimatorController controller = _monsterController as AnimatorController;
            if (controller == null || controller.layers.Length == 0) return;

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            if (machine.defaultState != null) flyState = machine.defaultState.name;

            foreach (ChildAnimatorState child in machine.states)
            {
                if (child.state == null) continue;
                if (child.state.name.ToLowerInvariant().Contains("die"))
                {
                    dieState = child.state.name;
                    break;
                }
            }
        }

        WorldHealthBar BuildHealthBar(Transform parent, Sprite sprite, float giantScale)
        {
            GameObject go = new GameObject("HealthBar", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            RectTransform rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(220f, 26f);

            // World size is authored here; divide out the giant's scale so the bar is scale independent.
            float worldScale = _giantHeight * 0.004f;
            rt.localScale = Vector3.one * (worldScale / Mathf.Max(0.0001f, giantScale));
            rt.localPosition = Vector3.zero;

            Image background = BuildUtils.NewImage("BG", rt, sprite, new Color(0f, 0f, 0f, 0.62f));
            BuildUtils.Stretch(background.rectTransform);

            Image fill = BuildUtils.NewImage("Fill", rt, sprite, new Color(1f, 0.28f, 0.2f, 1f));
            BuildUtils.Stretch(fill.rectTransform);
            fill.rectTransform.offsetMin = new Vector2(3f, 3f);
            fill.rectTransform.offsetMax = new Vector2(-3f, -3f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;

            WorldHealthBar bar = go.AddComponent<WorldHealthBar>();
            bar.fill = fill;
            bar.group = go.GetComponent<CanvasGroup>();
            bar.hideUntilDamaged = true;

            return bar;
        }

        DamagePopup BuildDamagePopupPrefab()
        {
            GameObject go = new GameObject("DamagePopup", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            RectTransform rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(240f, 80f);
            rt.localScale = Vector3.one * (_giantHeight * 0.005f);

            Text label = BuildUtils.NewText("Label", rt, "0", 60, TextAnchor.MiddleCenter, Color.white);
            BuildUtils.Stretch(label.rectTransform);

            DamagePopup popup = go.AddComponent<DamagePopup>();
            popup.label = label;
            popup.group = go.GetComponent<CanvasGroup>();
            popup.riseSpeed = Mathf.Max(1f, _giantHeight * 0.14f);
            popup.randomSpread = Mathf.Max(0.2f, _giantHeight * 0.05f);
            popup.referenceDistance = Mathf.Max(10f, _spawnRadius * 0.3f);

            string path = BuildUtils.PrefabFolder + "/DamagePopup.prefab";
            AssetDatabase.DeleteAsset(path);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);

            return prefab.GetComponent<DamagePopup>();
        }

        // -------------------------------------------------------------- player

        GameObject BuildPlayerRig(Camera camera, Vector3 position, float yaw, float pitch,
                                  ParticleSystem muzzleFxPrefab, Projectile bulletPrefab,
                                  LayerMask bulletMask, int projectileLayer,
                                  out PlayerAim aim, out WeaponController weapon)
        {
            GameObject player = new GameObject(PlayerName);
            player.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            GameObject pivot = new GameObject("CameraPivot");
            pivot.transform.SetParent(player.transform, false);
            pivot.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            if (camera == null)
            {
                GameObject cameraGO = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                cameraGO.tag = "MainCamera";
                camera = cameraGO.GetComponent<Camera>();
            }

            camera.transform.SetParent(pivot.transform, false);
            camera.transform.localPosition = Vector3.zero;
            camera.transform.localRotation = Quaternion.identity;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = Mathf.Max(1000f, _spawnRadius * 4f);
            camera.fieldOfView = 62f;

            if (camera.GetComponent<CameraShaker>() == null) camera.gameObject.AddComponent<CameraShaker>();
            CameraShaker shaker = camera.GetComponent<CameraShaker>();
            shaker.positionStrength = _gunLength * 0.08f;

            // --- view model ---------------------------------------------------
            GameObject gunRig = new GameObject("GunRig");
            gunRig.transform.SetParent(camera.transform, false);

            GameObject gun = CloneSource(_gunSource);
            gun.name = "gun";
            gun.transform.SetParent(gunRig.transform, false);
            OrientAlongForward(gun, _gunLength);

            float k = _gunLength / 0.85f;
            gunRig.transform.localPosition = new Vector3(0.26f * k, -0.22f * k, 0.46f * k);
            gunRig.transform.localRotation = Quaternion.Euler(0f, -2.5f, 0f);

            WeaponRecoil recoil = gunRig.AddComponent<WeaponRecoil>();
            recoil.kickBack = _gunLength * 0.08f;
            recoil.kickUp = _gunLength * 0.025f;
            recoil.swayAmount = _gunLength * 0.007f;

            GameObject muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(gunRig.transform, false);
            if (BuildUtils.TryGetLocalBounds(gunRig, out Bounds gunBounds))
                muzzle.transform.localPosition = new Vector3(gunBounds.center.x, gunBounds.center.y, gunBounds.max.z);
            else
                muzzle.transform.localPosition = new Vector3(0f, 0f, _gunLength * 0.5f);

            if (muzzleFxPrefab != null)
            {
                GameObject flash = (GameObject)PrefabUtility.InstantiatePrefab(muzzleFxPrefab.gameObject);
                flash.transform.SetParent(muzzle.transform, false);
                flash.name = "MuzzleFlash";
            }

            // Keep the view model out of the bullet's way.
            BuildUtils.SetLayerRecursively(gunRig, 2);   // Ignore Raycast

            // --- components ---------------------------------------------------
            aim = player.AddComponent<PlayerAim>();
            aim.yawPivot = player.transform;
            aim.pitchPivot = pivot.transform;
            aim.cam = camera;
            aim.assistRange = _spawnRadius * 2f;
            aim.minPitch = -45f;
            aim.maxPitch = 75f;
            aim.yawLimit = 180f;

            weapon = player.AddComponent<WeaponController>();
            weapon.aim = aim;
            weapon.muzzle = muzzle.transform;
            weapon.bulletPrefab = bulletPrefab;
            weapon.recoil = recoil;
            weapon.hitMask = bulletMask;
            weapon.range = _spawnRadius * 3f;
            weapon.bulletSpeed = Mathf.Max(80f, _spawnRadius * 1.6f);
            weapon.damage = 30f;
            weapon.fireRate = 7f;
            weapon.magazineSize = 30;
            weapon.reloadTime = 1.5f;

            ParticleSystem flashPs = muzzle.GetComponentInChildren<ParticleSystem>(true);
            weapon.muzzleFlash = flashPs;

            return player;
        }

        // ------------------------------------------------------------------ UI

        void BuildCanvas(GameManager game, WeaponController weapon, PlayerAim aim,
                         Sprite white, Sprite circle, Sprite ring, Sprite crosshairSprite)
        {
            GameObject canvasGO = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas),
                                                 typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform root = (RectTransform)canvasGO.transform;
            HUD hud = canvasGO.AddComponent<HUD>();
            hud.game = game;
            hud.weapon = weapon;
            hud.aim = aim;

            Color ink = Color.white;
            Color accent = new Color(1f, 0.55f, 0.2f);

            // --- always-on layer ---------------------------------------------
            Image damageFlash = BuildUtils.NewImage("DamageFlash", root, white, new Color(0.85f, 0.05f, 0.05f, 0f));
            BuildUtils.Stretch(damageFlash.rectTransform);
            hud.damageFlash = damageFlash;

            Image crosshair = BuildUtils.NewImage("Crosshair", root, crosshairSprite, new Color(1f, 1f, 1f, 0.65f));
            BuildUtils.Anchor(crosshair.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                              new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110f, 110f));
            hud.crosshair = crosshair;

            Text announcement = BuildUtils.NewText("Announcement", root, "", 78, TextAnchor.MiddleCenter, accent);
            BuildUtils.Anchor(announcement.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                              new Vector2(0.5f, 0.5f), new Vector2(0f, 340f), new Vector2(900f, 120f));
            hud.announcement = announcement;

            // --- gameplay layer ----------------------------------------------
            RectTransform gameplay = BuildUtils.NewRect("Gameplay", root);
            BuildUtils.Stretch(gameplay);
            hud.gameplayPanel = gameplay.gameObject;

            Text wave = BuildUtils.NewText("WaveLabel", gameplay, "WAVE 1", 46, TextAnchor.UpperLeft, ink);
            BuildUtils.Anchor(wave.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                              new Vector2(42f, -40f), new Vector2(460f, 60f));
            hud.waveLabel = wave;

            Text score = BuildUtils.NewText("ScoreLabel", gameplay, "0", 68, TextAnchor.UpperCenter, ink);
            BuildUtils.Anchor(score.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                              new Vector2(0f, -36f), new Vector2(500f, 80f));
            hud.scoreLabel = score;

            Text best = BuildUtils.NewText("BestLabel", gameplay, "BEST 0", 32, TextAnchor.UpperCenter,
                                           new Color(1f, 1f, 1f, 0.6f), FontStyle.Normal);
            BuildUtils.Anchor(best.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                              new Vector2(0f, -116f), new Vector2(500f, 44f));
            hud.bestLabel = best;

            Text coins = BuildUtils.NewText("CoinLabel", gameplay, "0", 46, TextAnchor.UpperRight, new Color(1f, 0.85f, 0.35f));
            BuildUtils.Anchor(coins.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                              new Vector2(-42f, -40f), new Vector2(360f, 60f));
            hud.coinLabel = coins;

            // City health bar.
            RectTransform cityBar = BuildUtils.NewRect("CityBar", gameplay);
            BuildUtils.Anchor(cityBar, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                              new Vector2(42f, -112f), new Vector2(520f, 40f));

            Image cityBg = BuildUtils.NewImage("BG", cityBar, white, new Color(0f, 0f, 0f, 0.5f));
            BuildUtils.Stretch(cityBg.rectTransform);

            Image cityFill = BuildUtils.NewImage("Fill", cityBar, white, new Color(0.35f, 0.85f, 0.4f));
            BuildUtils.Stretch(cityFill.rectTransform);
            cityFill.rectTransform.offsetMin = new Vector2(4f, 4f);
            cityFill.rectTransform.offsetMax = new Vector2(-4f, -4f);
            cityFill.type = Image.Type.Filled;
            cityFill.fillMethod = Image.FillMethod.Horizontal;
            cityFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            hud.cityFill = cityFill;

            Text cityLabel = BuildUtils.NewText("CityLabel", cityBar, "100 / 100", 26, TextAnchor.MiddleCenter, ink);
            BuildUtils.Stretch(cityLabel.rectTransform);
            hud.cityLabel = cityLabel;

            Gradient cityGradient = new Gradient();
            cityGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.9f, 0.15f, 0.1f), 0f),
                    new GradientColorKey(new Color(0.95f, 0.75f, 0.15f), 0.45f),
                    new GradientColorKey(new Color(0.35f, 0.85f, 0.4f), 0.8f)
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            hud.cityGradient = cityGradient;

            // Fire button.
            HoldButton fire = MakeHoldButton("FireButton", gameplay, circle, new Color(1f, 0.35f, 0.22f, 0.9f), "FIRE",
                                             new Vector2(1f, 0f), new Vector2(-70f, 150f), new Vector2(280f, 280f));
            hud.fireButton = fire;

            Image reloadDial = BuildUtils.NewImage("ReloadFill", fire.transform, ring, new Color(1f, 1f, 1f, 0.9f));
            BuildUtils.Stretch(reloadDial.rectTransform);
            reloadDial.type = Image.Type.Filled;
            reloadDial.fillMethod = Image.FillMethod.Radial360;
            reloadDial.fillOrigin = (int)Image.Origin360.Top;
            reloadDial.fillClockwise = true;
            reloadDial.fillAmount = 0f;
            reloadDial.enabled = false;
            hud.reloadFill = reloadDial;

            Button reload = MakeButton("ReloadButton", gameplay, circle, new Color(0.2f, 0.25f, 0.32f, 0.9f), "RELOAD",
                                       new Vector2(1f, 0f), new Vector2(-370f, 180f), new Vector2(170f, 170f), 30);
            hud.reloadButton = reload;

            Button zoom = MakeButton("ZoomButton", gameplay, circle, new Color(0.2f, 0.25f, 0.32f, 0.9f), "ZOOM",
                                     new Vector2(0f, 0f), new Vector2(70f, 170f), new Vector2(180f, 180f), 32);
            hud.zoomButton = zoom;
            hud.zoomIcon = zoom.GetComponent<Image>();

            Text ammo = BuildUtils.NewText("AmmoLabel", gameplay, "30 / 30", 52, TextAnchor.LowerRight, ink);
            BuildUtils.Anchor(ammo.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                              new Vector2(-70f, 470f), new Vector2(460f, 70f));
            hud.ammoLabel = ammo;

            // --- start panel ---------------------------------------------------
            RectTransform start = BuildUtils.NewRect("StartPanel", root);
            BuildUtils.Stretch(start);
            hud.startPanel = start.gameObject;

            Image startDim = BuildUtils.NewImage("Dim", start, white, new Color(0.02f, 0.03f, 0.06f, 0.72f));
            BuildUtils.Stretch(startDim.rectTransform);
            startDim.raycastTarget = true;

            Text title = BuildUtils.NewText("Title", start, "GIANT WANTED", 104, TextAnchor.MiddleCenter, accent);
            BuildUtils.Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                              new Vector2(0.5f, 0.5f), new Vector2(0f, 360f), new Vector2(1000f, 160f));

            Text hint = BuildUtils.NewText("Hint", start,
                "Drag anywhere to aim\nHold FIRE to shoot   -   ZOOM to steady\nKeep the giants off the city",
                38, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.8f), FontStyle.Normal);
            BuildUtils.Anchor(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                              new Vector2(0.5f, 0.5f), new Vector2(0f, 140f), new Vector2(1000f, 220f));

            Button play = MakeButton("PlayButton", start, circle, new Color(1f, 0.45f, 0.2f, 0.95f), "PLAY",
                                     new Vector2(0.5f, 0.5f), new Vector2(0f, -180f), new Vector2(440f, 150f), 56);
            hud.startButton = play;

            // --- end panel -----------------------------------------------------
            RectTransform end = BuildUtils.NewRect("EndPanel", root);
            BuildUtils.Stretch(end);
            hud.endPanel = end.gameObject;

            Image endDim = BuildUtils.NewImage("Dim", end, white, new Color(0.02f, 0.03f, 0.06f, 0.78f));
            BuildUtils.Stretch(endDim.rectTransform);
            endDim.raycastTarget = true;

            Text endTitle = BuildUtils.NewText("EndTitle", end, "CITY LOST", 92, TextAnchor.MiddleCenter, accent);
            BuildUtils.Anchor(endTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                              new Vector2(0.5f, 0.5f), new Vector2(0f, 330f), new Vector2(1000f, 140f));
            hud.endTitle = endTitle;

            Text endBody = BuildUtils.NewText("EndBody", end, "", 46, TextAnchor.MiddleCenter, ink, FontStyle.Normal);
            BuildUtils.Anchor(endBody.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                              new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1000f, 380f));
            hud.endBody = endBody;

            Button retry = MakeButton("RetryButton", end, circle, new Color(1f, 0.45f, 0.2f, 0.95f), "RETRY",
                                      new Vector2(0.5f, 0.5f), new Vector2(0f, -300f), new Vector2(440f, 150f), 56);
            hud.retryButton = retry;

            end.gameObject.SetActive(false);
        }

        static Button MakeButton(string name, Transform parent, Sprite sprite, Color color, string label,
                                 Vector2 anchor, Vector2 position, Vector2 size, int fontSize)
        {
            Image image = BuildUtils.NewImage(name, parent, sprite, color);
            image.raycastTarget = true;
            image.type = Image.Type.Simple;
            BuildUtils.Anchor(image.rectTransform, anchor, anchor, anchor, position, size);

            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            Text text = BuildUtils.NewText("Label", image.transform, label, fontSize, TextAnchor.MiddleCenter, Color.white);
            BuildUtils.Stretch(text.rectTransform);

            return button;
        }

        static HoldButton MakeHoldButton(string name, Transform parent, Sprite sprite, Color color, string label,
                                         Vector2 anchor, Vector2 position, Vector2 size)
        {
            Image image = BuildUtils.NewImage(name, parent, sprite, color);
            image.raycastTarget = true;
            BuildUtils.Anchor(image.rectTransform, anchor, anchor, anchor, position, size);

            Text text = BuildUtils.NewText("Label", image.transform, label, 50, TextAnchor.MiddleCenter, Color.white);
            BuildUtils.Stretch(text.rectTransform);

            return image.gameObject.AddComponent<HoldButton>();
        }

        static void EnsureEventSystem(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<EventSystem>(true) != null) return;

            // The project has both input backends enabled, so the legacy module is fine here.
            new GameObject(EventSystemName, typeof(EventSystem), typeof(StandaloneInputModule));
        }

        // -------------------------------------------------------------- content

        static Wave[] MakeWaves(int count)
        {
            Wave[] waves = new Wave[count];

            for (int i = 0; i < count; i++)
            {
                waves[i] = new Wave
                {
                    count = 2 + i,
                    healthMultiplier = 1f + 0.22f * i,
                    speedMultiplier = 1f + 0.05f * i,
                    spawnInterval = Mathf.Max(0.9f, 3.2f - 0.22f * i),
                    scaleRange = new Vector2(0.85f + 0.015f * i, 1.15f + 0.03f * i),
                    maxConcurrent = Mathf.Min(3 + i / 2, 6)
                };
            }

            return waves;
        }

        void AddEnvironmentColliders(int layer)
        {
            if (_envSource == null || !_envSource.scene.IsValid()) return;

            int added = 0;
            List<MeshFilter> filters = new List<MeshFilter>(_envSource.GetComponentsInChildren<MeshFilter>(true));

            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                if (filter.name.ToLowerInvariant().Contains("water")) continue;
                if (filter.GetComponent<Collider>() != null) continue;

                MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = false;
                added++;
            }

            BuildUtils.SetLayerRecursively(_envSource, layer);
            GameObjectUtility.SetStaticEditorFlags(_envSource, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic);

            Debug.Log("[Giant Wanted] Added " + added + " mesh colliders to the environment.");
        }
    }
}
