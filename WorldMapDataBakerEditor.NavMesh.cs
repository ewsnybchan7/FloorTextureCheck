/*
 * TerrainDataExtractorTool.Navmesh.cs
 * 작성자: ewsnybchan7
 * 작성일: 2022.09.08 12:17:22
 */

using Defines;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WorldMapDataBaker
{
    public partial class WorldMapDataBakerEditor
    {
        private NavMeshData m_NavMeshData = null;
        private NavMeshDataInstance m_NavMeshDataInstance = default;

        private SceneAsset m_SceneAsset = null;
        
        private int m_ExportHeight = 256;
        private float m_SamplingHeight = 0.3f;

        private const float MAX_SAMPLING_HEIGHT = 2f;

        private List<(RaycastHit HitInfo, Vector3 SamplePosition)> m_CachedObstacleInfos = new();
        private Dictionary<string, EFLOOR_TYPE> m_ObstacleTextureTable = new();

        private Dictionary<long, EFLOOR_TYPE> m_NavMeshLookupTable = null;

        private static readonly string EXPORTED_NAVMESH_FILE_NAME = "Assets/Editor/Terrain/TerrainDataExtractor/Exported/NavMesh/{0}.bytes";

        #region GUI

        private AutoDictionary<string, ObstacleTextureGUIInfo> m_CachedObstacleTextureGUIInfo = new();
        
        private GameObject m_SelectedGameObject;
        private string m_SelectedTextureID = string.Empty;
        private int m_SelectedObjectInstanceID = -1; 
        
        #endregion

        private class ObstacleTextureGUIInfo
        {
            public Texture Texture { get; } = null;

            public AutoDictionary<int, List<Vector3>> UsingGameObject { get; } = null;

            public EFLOOR_TYPE FloorType = EFLOOR_TYPE.NONE; 
            public bool FoldOut = false;

            public ObstacleTextureGUIInfo(in Texture InTexture)
            {
                if(InTexture == null)
                    return;

                Texture = InTexture;
                UsingGameObject = new();
            }

            public void AddSamplePosition(in int InInstanceID, in Vector3 InSamplePosition)
            {
                UsingGameObject[InInstanceID] ??= new();
                UsingGameObject[InInstanceID].Add(InSamplePosition);
            }
        }
        
        private void OnEnableNavMesh()
        {
            m_NavMeshDataInstance = new();
        }
        
        private void OnDestroyNavMesh()
        {
            m_NavMeshDataInstance.Remove();
            NavMesh.RemoveAllNavMeshData();
            
            var scene = SceneManager.GetActiveScene();
            if (SCENE_INTRO_PATH.Contains(scene.name))
                return;
            
            EditorSceneManager.OpenScene(SCENE_INTRO_PATH, OpenSceneMode.Additive);
            EditorSceneManager.CloseScene(scene, true);
        }

        #region GUI

        private void OnGUINavMesh()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(CONTENT_WIDTH), GUILayout.ExpandWidth(false));

            var navMeshData = (NavMeshData)EditorGUILayout.ObjectField(m_NavMeshData, typeof(NavMeshData), false);
            if (navMeshData != m_NavMeshData)
            {
                m_IsSuccessExported = false;
                m_NavMeshData = navMeshData;
                if(m_NavMeshData != null)
                    SetupNavMeshInfo();
            }

            if (m_NavMeshData != null)
            {
                var scene = (SceneAsset)EditorGUILayout.ObjectField(m_SceneAsset, typeof(SceneAsset), false);
                if (scene != m_SceneAsset)
                {
                    m_IsSuccessExported = false;
                    m_SceneAsset = scene;
                    if(m_SceneAsset != null)
                        OpenScene();
                }
            }

            if (m_NavMeshData != null && m_SceneAsset != null)
            {
                var distance = EditorGUILayout.IntField("Export Height", m_ExportHeight);
                if (distance is >= 0 and <= WORLD_MAX_HEIGHT)
                    m_ExportHeight = distance;
                
                var samplingHeight = EditorGUILayout.FloatField("NavMesh Sampling Height", m_SamplingHeight);
                if (samplingHeight is >= 0 and <= MAX_SAMPLING_HEIGHT)
                    m_SamplingHeight = samplingHeight;
            }

            EditorGUILayout.EndVertical();
        }

        private void OnGUICollectedNavMesh()
        {
            if (m_NavMeshData == null || m_SceneAsset == null)
                return;

            EditorGUILayout.BeginVertical(GUILayout.Width(CONTENT_WIDTH), GUILayout.ExpandWidth(false));
            
            if (GUILayout.Button("Collect NavMesh Data"))
            {
                OnClicked_CollectData(EEXPORT_TYPE.NAVMESH);
            }

            if (m_ObstacleTextureTable.IsNullOrEmpty() == false)
            {
                m_ScrollNavMeshPosition = EditorGUILayout.BeginScrollView(m_ScrollNavMeshPosition);

                foreach (var key in m_CachedObstacleTextureGUIInfo.Keys)
                {
                    var guiInfo = m_CachedObstacleTextureGUIInfo[key];

                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("", "", GUILayout.Height(45f), GUILayout.Width(45f), GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
                    var rect = GUILayoutUtility.GetLastRect();
                    EditorGUI.DrawPreviewTexture(rect, guiInfo.Texture);
                    guiInfo.FoldOut = EditorGUILayout.Foldout(guiInfo.FoldOut, guiInfo.Texture.name, true);
                    
                    m_ObstacleTextureTable[key] = (EFLOOR_TYPE)EditorGUILayout.EnumPopup(m_ObstacleTextureTable[key], GUILayout.Width(100f), GUILayout.ExpandWidth(false));
                    
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(2);
                    if (guiInfo.FoldOut)
                    {
                        foreach (var instanceID in guiInfo.UsingGameObject.Keys)
                        {
                            var @object = EditorUtility.InstanceIDToObject(instanceID);
                            if (GUILayout.Button(@object.name))
                            {
                                OnClick_GameObject(@object, key, instanceID);
                            }
                        }
                    }
                    
                    EditorGUILayout.EndVertical();
                }
                
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndVertical();
        }

        private void OnClick_GameObject(in Object InObject, string InTextureID, in int InInstanceID)
        {
            var go = (Transform)InObject;
            
            EditorGUIUtility.PingObject(go.GetComponent<Collider>().gameObject);
            Selection.activeGameObject = go.GetComponent<Collider>().gameObject;
            SceneView.lastActiveSceneView.LookAt(go.transform.position);
            
            m_SelectedTextureID = InTextureID;
            m_SelectedObjectInstanceID = InInstanceID;
            
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
        }
        
        #endregion

        #region Scene GUI

        private void OnSceneGUIRaycastHitObject()
        {
            var usingGameObject = m_CachedObstacleTextureGUIInfo[m_SelectedTextureID].UsingGameObject;
            var pointList = usingGameObject[m_SelectedObjectInstanceID];
            if (pointList.IsNullOrEmpty())
                return;
            
            foreach (var samplePoint in pointList)
            {
                Handles.Label(samplePoint + Vector3.up * 0.5f, $"Hit Point: {samplePoint}");
            
                Handles.color = Color.blue;
                Handles.SphereHandleCap(0, samplePoint, Quaternion.identity, 0.2f, EventType.Repaint);
                Handles.DrawLine(samplePoint - Vector3.up * 5f, samplePoint + Vector3.up * 5f, 0.05f);
            
                Handles.color = Color.green;
                Handles.DrawLine(samplePoint - Vector3.right, samplePoint + Vector3.right, 0.05f);
                Handles.DrawLine(samplePoint - Vector3.forward, samplePoint + Vector3.forward, 0.05f);
            }

            var hitList = new List<Vector3>();
            var gameObject = (Transform)EditorUtility.InstanceIDToObject(m_SelectedObjectInstanceID);
            for (int i = (int)gameObject.transform.position.x - 20; i < gameObject.transform.position.x + 20; ++i)
            {
                for (int j = (int)gameObject.transform.position.z - 20; j < gameObject.transform.position.z + 20; ++j)
                {
                    var result = Physics.RaycastAll(new Vector3(i, 500, j), Vector3.down, 500);
                    hitList.AddRange(result.Where(x => x.transform.GetInstanceID() == m_SelectedObjectInstanceID).Select(x => x.point));
                }
            }

            foreach (var hitPoint in hitList)
            {
                Handles.color = Color.red;
                Handles.SphereHandleCap(0, hitPoint, Quaternion.identity, 0.1f, EventType.Repaint);
            }
        }

        #endregion

        private void SetupNavMeshInfo()
        {
            if (m_NavMeshData == null)
                return;
        
            using OneTimeWatchTimer stopWatch = new($"Load {m_NavMeshData.name} NavMesh");

            var instance = NavMesh.AddNavMeshData(m_NavMeshData);
                
            m_NavMeshDataInstance.Remove();
            m_NavMeshDataInstance = instance;
        }

        private void OpenScene()
        {
            if (m_SceneAsset == null)
                return;

            using OneTimeWatchTimer stopWatch = new($"Open {m_SceneAsset.name} Scene");

            var instanceID = m_SceneAsset.GetInstanceID();
            var assetPath = AssetDatabase.GetAssetPath(instanceID);

            EditorSceneManager.OpenScene(assetPath, OpenSceneMode.Single);
        }

        #region Collect Data
        
        private void OnCollectData_NavMesh()
        {
            try
            {
                if (Terrain.activeTerrain.terrainData.name.Equals(m_TerrainData.name) == false)
                    return;
                
                m_CachedObstacleInfos ??= new();
                m_CachedObstacleInfos.Clear();

                m_ObstacleTextureTable ??= new();
                m_ObstacleTextureTable.Clear();

                GetObstacleInfosOnNavMesh();
                CachedObstacleTextureInfo();
            }
            catch (Exception e)
            {
                RM2Logger.LogError(e.ToString());
            }
        }
        
        private void GetObstacleInfosOnNavMesh()
        {
            ForeachTerrainAlpha(InternalGetSamplePositionOnTerrain);

            void InternalGetSamplePositionOnTerrain(int InX, int InY)
            {
                var startPosition = Vector3.right * InX + Vector3.forward * InY + Vector3.up * m_ExportHeight;
                var propList = Physics.RaycastAll(startPosition, Vector3.down, m_ExportHeight, 1 << Layer.LAYER_PROP_STATIC);

                foreach (var prop in propList)
                {
                    if(TryGetSamplePosition(prop.point, out var outPosition) == false)
                        continue;
                    
                    m_CachedObstacleInfos.Add((prop, outPosition));
                }
            }
        }

        private void CachedObstacleTextureInfo()
        {
            if (m_CachedObstacleInfos.IsNullOrEmpty())
                return;

            foreach (var obstacleInfo in m_CachedObstacleInfos)
            {
                var cachedTextures = new Dictionary<string, Texture>();
                GetTextureRecursively(obstacleInfo.HitInfo.transform, cachedTextures);

                foreach (var key in cachedTextures.Keys)
                {
                    if (m_ObstacleTextureTable.ContainsKey(key))
                        continue;

                    m_ObstacleTextureTable.Add(key, EFLOOR_TYPE.NONE);
                }
                
                foreach (var key in cachedTextures.Keys)
                {
                    if (m_CachedObstacleTextureGUIInfo.ContainsKey(key) == false)
                    {
                        m_CachedObstacleTextureGUIInfo[key] = new(cachedTextures[key]);
                    }

                    m_CachedObstacleTextureGUIInfo[key].AddSamplePosition(obstacleInfo.HitInfo.transform.GetInstanceID(), obstacleInfo.SamplePosition);
                }
            }
        }
        
        #endregion

        #region Bake

        private void OnExport_NavMesh()
        {
            try
            {
                m_NavMeshLookupTable ??= new();
                m_NavMeshLookupTable.Clear();
                
                ConvertObstacleTextureToType();
                BakeNavMeshDataFile();
            }
            catch (Exception e)
            {
                RM2Logger.LogError(e.ToString());
            }
        }

        private void ConvertObstacleTextureToType()
        {
            if (m_ObstacleTextureTable.IsNullOrEmpty())
                return;
            
            foreach (var key in m_ObstacleTextureTable.Keys)
            {
                var type = m_ObstacleTextureTable[key];
                var samplePositions = m_CachedObstacleTextureGUIInfo[key].UsingGameObject.SelectMany(x => x.Value);

                foreach (var samplePosition in samplePositions)
                {
                    var positionKey = GetWorldDataKey((int)samplePosition.x, (int)samplePosition.y, (int)samplePosition.z);
                    if (m_NavMeshLookupTable.ContainsKey(positionKey))
                        continue;
                    
                    m_NavMeshLookupTable.Add(positionKey, type);
                }
            }
        }

        private void BakeNavMeshDataFile()
        {
            if (m_NavMeshLookupTable.IsNullOrEmpty())
                return;

            var filePath = string.Format(EXPORTED_NAVMESH_FILE_NAME, m_NavMeshData.name);
            using OneTimeWatchTimer stopwatch = new($"{m_NavMeshData.name} Convert");
            
            try
            {
                using FileStream stream = new(filePath, FileMode.Create);
                BinaryWriter writer = new(stream);

                // foreach (var value in m_NavMeshLookupTable)
                // {
                //     writer.Write(value.x);
                //     writer.Write(value.y);
                //     writer.Write(value.z);
                //     writer.Write((byte)value.type);
                // }

                writer.Close();
            }
            catch (Exception e)
            {
                RM2Logger.LogError($"{m_NavMeshData.name} Terrain Data Convert Failed: {e}");
            }
        }

        private bool ReadBakedNavMeshDataFile()
        {
            var filePath = string.Format(EXPORTED_NAVMESH_FILE_NAME, m_NavMeshData.name);

            FileInfo fileInfo = new(filePath);
            if (fileInfo.Exists == false)
                return false;

            ForeachTerrainAlpha((x, y) =>
            {
                m_CachedPreviewRectInfo[(y, x)] = Color.gray;
            });
            
            try
            {
                using FileStream fileStream = new(filePath, FileMode.Open);
                BinaryReader reader = new(fileStream);

                var length = reader.BaseStream.Length;
                while(reader.BaseStream.Position < length)
                {
                    var x = reader.ReadInt32();
                    var y = reader.ReadSingle();
                    var z = reader.ReadInt32();
                    var type = reader.ReadByte();

                    Debug.DrawRay(new Vector3(x,y,z), Vector3.up, Color.blue, 100f);
                    
                    PREVIEW_TEXTURE.SetPixel(x, z, GetColorFromFloorType((EFLOOR_TYPE)type));
                }
                
                PREVIEW_TEXTURE.Apply();

                reader.Close();

                return true;
            }
            catch (Exception e)
            {
                RM2Logger.LogError($"{m_TerrainData.name} TerrainData Read Fail: {e}]");
                return false;
            }
        }

        #endregion

        #region Static

        private bool TryGetSamplePosition(in Vector3 InPosition, out Vector3 OutPosition)
        {
            OutPosition = Vector3.zero;
            if (NavMesh.SamplePosition(InPosition, out var hit, m_SamplingHeight, NavMesh.AllAreas))
            {
                var hitPosition = hit.position;

                if (Math.Abs(InPosition.x - hitPosition.x) > 0 || Math.Abs(InPosition.z - hitPosition.z) > 0)
                    return false;
                
                OutPosition = hitPosition;
                return true;
            }
            return false;
        }
        
        private static void GetTextureRecursively(Transform InTransform, Dictionary<string, Texture> InCachedTextures)
        {
            var renderers = InTransform.GetComponents<Renderer>();

            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                foreach (var material in materials)
                {
                    var texture = material.mainTexture;
                    if (texture == null)
                        continue;
                    
                    var texturePath = AssetDatabase.GetAssetPath(texture);
                    var guid = AssetDatabase.AssetPathToGUID(texturePath);
                    if (InCachedTextures.ContainsKey(guid))
                        continue;

                    InCachedTextures.Add(guid, texture);
                }
            }

            foreach (Transform child in InTransform)
            {
                GetTextureRecursively(child, InCachedTextures);
            }
        }

        #endregion
    }
}