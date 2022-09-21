/*
 * TerrainDataExtractorTool.cs
 * 작성자: ewsnybchan7
 * 작성일: 2022.09.07 16:38:22
 */

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WorldMapDataBaker;

public static partial class OpenEditorFunction
{
    private static EditorWindow m_WorldMapDataBaker = null;
    private static readonly Vector2 WORLDMAP_DATA_BAKER_TOOL_SIZE = new(810f, 700f); 

    [MenuItem("PlayWith/Terrain/WorldMapDataBaker", false)]
    private static void OpenWorldMapDataBakerEditor()
    {
        if (m_WorldMapDataBaker != null)
        {
            m_WorldMapDataBaker.position = new Rect(m_WorldMapDataBaker.position.x, m_WorldMapDataBaker.position.y, WORLDMAP_DATA_BAKER_TOOL_SIZE.x, WORLDMAP_DATA_BAKER_TOOL_SIZE.y);
            m_WorldMapDataBaker.Show();
        }

        m_WorldMapDataBaker = EditorWindow.GetWindow(typeof(WorldMapDataBakerEditor), true);
        m_WorldMapDataBaker.position = new Rect(m_WorldMapDataBaker.position.x, m_WorldMapDataBaker.position.y, WORLDMAP_DATA_BAKER_TOOL_SIZE.x, WORLDMAP_DATA_BAKER_TOOL_SIZE.y);
    }
}

namespace WorldMapDataBaker
{
    [EditorWindowTitle(title = "WorldMap Data Baker")]
    public partial class WorldMapDataBakerEditor : EditorWindow
    {
        private const string SCENE_INTRO_PATH = "Assets/Scenes/SceneIntro.unity";
        //private static readonly string EXPORTED_WORLD_DATA_FILE_PATH = "Assets/Editor/Terrain/TerrainDataExtractor/Exported/{0}_WorldData.bytes";
        private static readonly string EXPORTED_WORLD_DATA_FILE_PATH = "Assets/Resources/WorldMapData/{0}_WorldData.bytes";
        
        private const int WORLD_MAX_HEIGHT = 256;
        
        private EMAP_NAVI m_SelectedMap = EMAP_NAVI.NAVI_NULL;

        private bool m_IsSuccessExported = false;
        private bool m_IsShowPreview = false;

        #region GUI Variable

        private Rect m_CachedRect = Rect.zero;
        private AutoDictionary<(int, int), Color> m_CachedPreviewRectInfo = new();
        private int m_PreviewScale = 2;

        private Vector2 m_ScrollNavMeshPosition = Vector2.zero;

        private EEXPORT_TYPE m_SelectedViewType = EEXPORT_TYPE.NONE;

        private const float CONTENT_WIDTH = 400f;
        
        [Flags]
        private enum EEXPORT_TYPE : byte
        {
            NONE = 0,
            TERRAIN_DATA = 1,
            NAVMESH = 2,
            ALL = 3
        }
        
        #endregion
        
        private void OnEnable()
        {
            m_PreviewScale = 2;
            
            m_CachedRect.width = 0.5f * m_PreviewScale;
            m_CachedRect.height = 0.5f * m_PreviewScale;
        
            OnEnableTerrainData();
            OnEnableNavMesh();

            m_IsSuccessExported = false;
        }
    
        private void OnGUI()
        {
            EditorGUILayout.BeginVertical();

            OnGUIInitialSetting();
            OnGUICollectedData();
            OnGUIExportedData();
            
            EditorGUILayout.EndVertical();
        }

        private void OnSceneGUI(SceneView InSceneView)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            
            OnSceneGUIRaycastHitObject();
        }
        
        private void OnDestroy()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            
            OnDestroyNavMesh();
        }

        #region GUI
        
        private void OnGUIInitialSetting()
        {
            var mapType = (EMAP_NAVI)EditorGUILayout.EnumPopup(m_SelectedMap, GUILayout.Width(300f), GUILayout.ExpandWidth(false));
            if (m_SelectedMap != mapType)
            {
                m_SelectedMap = mapType;
            }
            
            EditorGUILayout.BeginHorizontal();
            OnGUITerrainData();
            OnGUINavMesh();
            EditorGUILayout.EndHorizontal();
        }

        private void OnGUICollectedData()
        {
            if (m_TerrainData == null || m_NavMeshData == null || m_SceneAsset == null)
                return;
            
            EditorGUILayout.BeginHorizontal();
            OnGUICollectedTerrainData();
            OnGUICollectedNavMesh();
            EditorGUILayout.EndHorizontal();
        }

        private void OnGUIExportedData()
        {
            if (m_TerrainLayerList.IsNullOrEmpty() || m_ObstacleTextureTable.IsNullOrEmpty())
                return;

            EditorGUILayout.BeginVertical();
            OnGUIExportButton();
            OnGUIFloorTypeColor();
            OnGUIShowPreviewButton();
            OnGUIExportedDataPreview();
            EditorGUILayout.EndVertical();
        }

        private Texture2D PREVIEW_TEXTURE = null;

        private void OnGUIExportButton()
        {
            EditorGUILayout.BeginVertical();
            if (GUILayout.Button("Export Data"))
            {
                OnClicked_Export(EEXPORT_TYPE.ALL);
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Export Terrain Data", GUILayout.Width(CONTENT_WIDTH), GUILayout.ExpandWidth(false)))
            {
                OnClicked_Export(EEXPORT_TYPE.TERRAIN_DATA);
            }
            if (GUILayout.Button("Export NavMesh Data", GUILayout.Width(CONTENT_WIDTH), GUILayout.ExpandWidth(false)))
            {
                OnClicked_Export(EEXPORT_TYPE.NAVMESH);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void OnGUIShowPreviewButton()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Show Terrain Data", GUILayout.Width(CONTENT_WIDTH), GUILayout.ExpandWidth(false)))
            {
                OnClicked_ShowExportedData(EEXPORT_TYPE.TERRAIN_DATA);
            }
            if (GUILayout.Button("Show NavMesh Data", GUILayout.Width(CONTENT_WIDTH), GUILayout.ExpandWidth(false)))
            {
                OnClicked_ShowExportedData(EEXPORT_TYPE.NAVMESH);
            }
            EditorGUILayout.EndHorizontal();
        }
        
        private void OnGUIExportedDataPreview()
        {
            if (m_IsShowPreview == false)
                return;

            EditorGUILayout.LabelField("", GUILayout.Width(m_AlphaMapWidth), GUILayout.Height(m_AlphaMapHeight), GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
            var rect = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawPreviewTexture(rect, PREVIEW_TEXTURE);
        }

        private void OnGUIFloorTypeColor()
        {
            for (int i = 0; i < m_TerrainData.terrainLayers.Length; ++i)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("", "", GUILayout.Width(25f), GUILayout.Height(25f), GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
                var rect = GUILayoutUtility.GetLastRect();

                EditorGUI.DrawRect(rect, GetColorFromFloorType((EFLOOR_TYPE)i));
                
                EditorGUILayout.LabelField(m_TerrainData.terrainLayers[i].name);
                EditorGUILayout.EndHorizontal();
            }
        }

        #endregion

        #region File

        private bool ReadBakedDataFile(EEXPORT_TYPE InType) => InType switch
        {
            EEXPORT_TYPE.TERRAIN_DATA => ReadBakedTerrainDataFile(),
            EEXPORT_TYPE.NAVMESH => ReadBakedNavMeshDataFile(),
            _ => false
        };

        #endregion

        private void OnClicked_CollectData(EEXPORT_TYPE InType)
        {
            switch (InType)
            {
                case EEXPORT_TYPE.TERRAIN_DATA:
                    OnCollectData_TerrainData();
                    break;
                case EEXPORT_TYPE.NAVMESH:
                    OnCollectData_NavMesh();
                    break;
            }
        }
        
        private void OnClicked_Export(EEXPORT_TYPE InType)
        {
            switch (InType)
            {
                case EEXPORT_TYPE.TERRAIN_DATA:
                    OnExport_TerrainData();
                    break;
                case EEXPORT_TYPE.NAVMESH:
                    OnExport_NavMesh();
                    break;
                case EEXPORT_TYPE.ALL:
                    // OnExport_TerrainData();
                    // OnExport_NavMesh();
                    OnExport_WorldData();
                    break;
                default:
                    return;
            }
        }

        private void OnClicked_ShowExportedData(EEXPORT_TYPE InType)
        {
            if (PREVIEW_TEXTURE == null || 
                PREVIEW_TEXTURE.width != m_AlphaMapWidth ||
                PREVIEW_TEXTURE.height != m_AlphaMapHeight)
            {
                PREVIEW_TEXTURE = new(m_AlphaMapWidth, m_AlphaMapHeight);
            }
            
            m_SelectedViewType = InType;
            m_IsShowPreview = ReadBakedDataFile(m_SelectedViewType);
        }

        private static Color GetColorFromFloorType(EFLOOR_TYPE InTerrainType) => InTerrainType switch
        {
            EFLOOR_TYPE.GRASS => Color.green,
            EFLOOR_TYPE.DESSERT => Color.yellow,
            EFLOOR_TYPE.MARBLE => Color.black,
            EFLOOR_TYPE.WOOD => ColorUtils.ToRGBA(2157409356),
            EFLOOR_TYPE.WATER => Color.blue,
            EFLOOR_TYPE.NONE => Color.gray
        };

        private void OnExport_WorldData()
        {
            try
            {
                m_AlphaMap = m_TerrainData.GetAlphamaps(0, 0, m_AlphaMapWidth, m_AlphaMapHeight);
                m_TerrainLookupTable = new EFLOOR_TYPE[m_AlphaMapHeight, m_AlphaMapWidth];

                ConvertTerrainLayerToType();
                
                m_NavMeshLookupTable ??= new();
                m_NavMeshLookupTable.Clear();
                
                ConvertObstacleTextureToType();

                BakeWorldDataFile();
            }
            catch (Exception e)
            {
                RM2Logger.LogError(e.ToString());
            }
        }

        private void BakeWorldDataFile()
        {
            if (m_TerrainLookupTable.Length == 0 || m_NavMeshLookupTable.IsNullOrEmpty())
                return;

            var worldName = m_SelectedMap.ToString().Split('_').Last();
            var filePath = string.Format(EXPORTED_WORLD_DATA_FILE_PATH, worldName);

            using OneTimeWatchTimer stopwatch = new($"{worldName} World Data Bake");

            try
            {
                using FileStream stream = new(filePath, FileMode.Create);
                BinaryWriter writer = new(stream);

                writer.Write((byte)m_SelectedMap);
                writer.Write((ushort)m_AlphaMapWidth);
                writer.Write((ushort)m_ExportHeight);
                writer.Write((ushort)m_AlphaMapHeight);

                for (int z = 0; z < m_AlphaMapHeight; ++z)
                {
                    for (int x = 0; x < m_AlphaMapWidth; ++x)
                    {
                        EFLOOR_TYPE value = m_TerrainLookupTable[z, x];
                        for (int y = 0; y < m_ExportHeight; ++y)
                        {
                            var key = GetWorldDataKey(x, y, z);
                            if (m_NavMeshLookupTable.TryGetValue(key, out var navMeshValue))
                            {
                                value = navMeshValue;
                            }

                            writer.Write((byte)value);
                        }
                    }
                }

                writer.Close();
            }
            catch (Exception e)
            {
                RM2Logger.LogError(e.ToString());
            }
        }

        private const long XFactor = 100000000;
        private const long YFactor = 10000;
        private const long ZFactor = 1;
        
        private static long GetWorldDataKey(int InX, int InY, int InZ) => InX * XFactor + InY * YFactor + InZ * ZFactor;
    }
}