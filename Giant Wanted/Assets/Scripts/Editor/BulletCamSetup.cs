using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GiantWanted.EditorTools
{
    /// <summary>
    /// Additive installer for the bullet kill cam. Unlike the scene builder this touches
    /// nothing that already exists - it only adds a camera object and wires its references,
    /// so it is safe to run on a scene you have hand-tuned. It does not save the scene;
    /// review the result and save (or undo) yourself.
    /// </summary>
    public static class BulletCamSetup
    {
        const string CamName = "BulletCam";

        [MenuItem("Tools/Giant Wanted/Add Bullet Kill Cam")]
        static void AddBulletCam()
        {
            BulletCam existing = Object.FindFirstObjectByType<BulletCam>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Rewire(existing);
                EditorUtility.SetDirty(existing);
                EditorSceneManager.MarkSceneDirty(existing.gameObject.scene);
                Selection.activeGameObject = existing.gameObject;

                EditorUtility.DisplayDialog("Giant Wanted",
                    "A BulletCam was already in the scene - its references were refreshed instead of adding a second one.",
                    "OK");
                return;
            }

            Camera gunCamera = FindGunCamera();
            if (gunCamera == null)
            {
                EditorUtility.DisplayDialog("Giant Wanted",
                    "No camera found in the scene. Open the game scene and try again.", "OK");
                return;
            }

            GameObject go = new GameObject(CamName);
            Undo.RegisterCreatedObjectUndo(go, "Add Bullet Kill Cam");
            go.transform.SetPositionAndRotation(gunCamera.transform.position, gunCamera.transform.rotation);

            Camera camera = go.AddComponent<Camera>();
            camera.CopyFrom(gunCamera);
            camera.enabled = false;
            camera.depth = gunCamera.depth + 1;
            camera.fieldOfView = 45f;
            go.tag = "MainCamera";

            CopyUrpSettings(gunCamera, camera);

            BulletCam bulletCam = go.AddComponent<BulletCam>();
            bulletCam.bulletCamera = camera;
            bulletCam.gunCamera = gunCamera;
            Rewire(bulletCam);

            // Keep the first-person gun out of the kill cam - it sits on its own layer.
            int gunLayer = FindGunLayer(gunCamera);
            if (gunLayer >= 0)
            {
                bulletCam.hideLayers = 1 << gunLayer;
                camera.cullingMask &= ~(1 << gunLayer);
            }

            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;

            Debug.Log("[Giant Wanted] Bullet kill cam added" +
                      (gunLayer >= 0 ? " (hiding layer " + LayerMask.LayerToName(gunLayer) + ")" : "") +
                      ". Save the scene to keep it.");
        }

        static void Rewire(BulletCam bulletCam)
        {
            if (bulletCam.game == null) bulletCam.game = Object.FindFirstObjectByType<GameManager>();
            if (bulletCam.weapon == null) bulletCam.weapon = Object.FindFirstObjectByType<WeaponController>();
            if (bulletCam.hud == null) bulletCam.hud = Object.FindFirstObjectByType<HUD>();
            if (bulletCam.spawner == null) bulletCam.spawner = Object.FindFirstObjectByType<WaveSpawner>();
            if (bulletCam.gunCamera == null) bulletCam.gunCamera = FindGunCamera();
            if (bulletCam.bulletCamera == null) bulletCam.bulletCamera = bulletCam.GetComponent<Camera>();
        }

        /// <summary>The player's camera: prefer the one the weapon aims through.</summary>
        static Camera FindGunCamera()
        {
            PlayerAim aim = Object.FindFirstObjectByType<PlayerAim>(FindObjectsInactive.Include);
            if (aim != null && aim.cam != null) return aim.cam;

            foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (camera.GetComponent<BulletCam>() != null) continue;
                if (camera.CompareTag("MainCamera")) return camera;
            }

            return Camera.main;
        }

        /// <summary>
        /// The view-model gun lives on its own layer so bullets ignore it. Read that layer
        /// back off the scene rather than assuming, in case it was changed by hand.
        /// </summary>
        static int FindGunLayer(Camera gunCamera)
        {
            WeaponController weapon = Object.FindFirstObjectByType<WeaponController>(FindObjectsInactive.Include);
            if (weapon == null || weapon.muzzle == null) return -1;

            // Walk up from the muzzle to the direct child of the camera - that is the gun rig.
            Transform node = weapon.muzzle;
            while (node != null && node.parent != null && node.parent != gunCamera.transform)
                node = node.parent;

            if (node == null || node.parent != gunCamera.transform) return -1;
            return node.gameObject.layer;
        }

        static void CopyUrpSettings(Camera source, Camera destination)
        {
            UniversalAdditionalCameraData sourceData = source.GetComponent<UniversalAdditionalCameraData>();
            UniversalAdditionalCameraData data = destination.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = destination.gameObject.AddComponent<UniversalAdditionalCameraData>();

            data.renderType = CameraRenderType.Base;

            if (sourceData != null)
            {
                data.renderPostProcessing = sourceData.renderPostProcessing;
                data.antialiasing = sourceData.antialiasing;
                data.renderShadows = sourceData.renderShadows;
                data.volumeLayerMask = sourceData.volumeLayerMask;
            }
            else
            {
                data.renderPostProcessing = true;
            }
        }
    }
}
